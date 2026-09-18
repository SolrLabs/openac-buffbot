using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Web;

/// <summary>Owns a <see cref="MeshNode"/>'s lifecycle so the web console — optional by design — can never take buffing down with it. <see cref="Tick"/>, <see cref="Publish"/>, <see cref="TryDequeueCommand"/> and <see cref="Stop"/> are the whole surface, and none of them ever throws: a node that fails to start, or fails mid-flight, is a Warn on this tick and a retry later.</summary>
internal sealed class MeshSupervisor
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    private readonly IClock _clock;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logWarn;
    private readonly Func<MeshNode> _startNode;

    private MeshNode? _node;
    private bool _startFailedLastTime;
    private DateTimeOffset? _nextStartAttempt;
    private DateTimeOffset? _lastPublishFailureLog;
    private DateTimeOffset? _lastDrainFailureLog;

    /// <summary><paramref name="startNode"/> is the entire "read the key, load the page, construct the node, start it" pipeline — this class only decides when to call it and what to do when it throws.</summary>
    internal MeshSupervisor(IClock clock, Action<string> logInfo, Action<string> logWarn, Func<MeshNode> startNode)
    {
        _clock = clock;
        _logInfo = logInfo;
        _logWarn = logWarn;
        _startNode = startNode;
    }

    /// <summary>Starts the node when the bot is enabled and none is running, stops it when disabled. A start that throws is logged once and retried no sooner than <see cref="RetryInterval"/> later; a success that follows a failure is logged once too.</summary>
    internal void Tick(bool enabled)
    {
        if (!enabled)
        {
            Stop();
            return;
        }

        if (_node is not null)
            return;

        DateTimeOffset now = _clock.UtcNow;
        if (_nextStartAttempt is { } next && now < next)
            return;

        try
        {
            _node = _startNode();
            if (_startFailedLastTime)
            {
                _startFailedLastTime = false;
                _logInfo("BuffBot web console mesh recovered.");
            }
            _nextStartAttempt = null;
        }
        catch (Exception error)
        {
            _startFailedLastTime = true;
            _nextStartAttempt = now + RetryInterval;
            _logWarn($"BuffBot web console mesh failed to start: {error.Message}");
        }
    }

    internal void Publish(string botId, string name, string world, MeshStatus status)
    {
        if (_node is null)
            return;

        try
        {
            _node.Publish(botId, name, world, status);
        }
        catch (Exception error)
        {
            LogThrottled(ref _lastPublishFailureLog, "publish", error);
        }
    }

    /// <summary>Throw-free: no node yet, or a node that throws reading its own state, reports <see cref="MeshConsoleState.Unavailable"/> rather than raising into the caller's tick.</summary>
    internal MeshConsoleLink DescribeConsole()
    {
        if (_node is null)
            return new MeshConsoleLink(MeshConsoleState.Unavailable, null);

        try
        {
            return _node.DescribeConsole();
        }
        catch (Exception)
        {
            return new MeshConsoleLink(MeshConsoleState.Unavailable, null);
        }
    }

    internal bool TryDequeueCommand(out MeshCommand command)
    {
        if (_node is null)
        {
            command = default!;
            return false;
        }

        try
        {
            return _node.TryDequeueCommand(out command);
        }
        catch (Exception error)
        {
            LogThrottled(ref _lastDrainFailureLog, "drain", error);
            command = default!;
            return false;
        }
    }

    internal void Stop()
    {
        MeshNode? node = _node;
        _node = null;
        if (node is null)
            return;

        try
        {
            node.Stop();
        }
        catch (Exception)
        {
        }
    }

    private void LogThrottled(ref DateTimeOffset? lastLogged, string what, Exception error)
    {
        DateTimeOffset now = _clock.UtcNow;
        if (lastLogged is { } previous && now - previous < RetryInterval)
            return;

        lastLogged = now;
        _logWarn($"BuffBot web console mesh {what} failed: {error.Message}");
    }
}
