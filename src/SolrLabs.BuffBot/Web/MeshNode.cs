using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SolrLabs.BuffBot.Web;

/// <summary>A sealed record so <see cref="MeshNode.Publish"/> can swap it in with one reference assignment, rather than a hub and a spoke ever reading a half-updated identity/status pair.</summary>
internal sealed record MeshSelf(string BotId, string Name, string World, MeshStatus Status);

/// <summary>One node on the BuffBot web console mesh: elects itself hub or spoke by trying to bind the fixed loopback port, and from then on exposes exactly two things to <c>BuffBotPlugin</c> — <see cref="Publish"/> for the tick's status, and <see cref="TryDequeueCommand"/> for whatever the console asked for. Everything else — the HTTP server, the heartbeat loop, the takeover — lives entirely inside this class and never calls <see cref="AcDream.Plugin.Abstractions.IPluginHost"/>.</summary>
internal sealed class MeshNode : IDisposable
{
    private readonly MeshNodeOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentQueue<MeshCommand> _inbound = new();
    private readonly object _roleGate = new();

    private volatile bool _isHub;
    private volatile MeshSelf? _self;
    private TcpListener? _listener;
    private MeshServer? _server;
    private MeshRegistry? _registry;
    private CancellationTokenSource? _spokeCts;
    private Task? _spokeTask;
    private bool _stopped;

    internal MeshNode(MeshNodeOptions options)
    {
        _options = options;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    internal bool IsHub => _isHub;

    internal void Start()
    {
        lock (_roleGate)
        {
            if (TryBindListener(out TcpListener listener))
                BecomeHubLocked(listener, takeover: false);
            else
                BecomeSpokeLocked();
        }
    }

    /// <summary>A hub folds the status straight into its own registry entry; a spoke only remembers it, for the heartbeat loop to send on its own schedule.</summary>
    internal void Publish(string botId, string name, string world, MeshStatus status)
    {
        var self = new MeshSelf(botId, name, world, status);
        _self = self;
        if (_isHub)
            _registry?.Report(botId, name, world, isHub: true, status);
    }

    internal bool TryDequeueCommand(out MeshCommand command) => _inbound.TryDequeue(out command!);

    /// <summary>A link is offered the instant this node knows its role — hub or spoke.</summary>
    internal MeshConsoleLink DescribeConsole() =>
        new(IsHub ? MeshConsoleState.Hub : MeshConsoleState.Spoke, BuildLink());

    private string BuildLink() => $"http://127.0.0.1:{_options.Port}/";

    /// <summary>Stops the listener synchronously before anything else, so the port is free the instant this returns. The rest is a bounded, best-effort join: the calling thread never blocks more than a fraction of a second here.</summary>
    internal void Stop()
    {
        lock (_roleGate)
        {
            if (_stopped)
                return;
            _stopped = true;
            _spokeCts?.Cancel();
            _server?.Stop();
            try
            {
                _listener?.Stop();
            }
            catch (SocketException)
            {
            }
        }

        try
        {
            _spokeTask?.Wait(TimeSpan.FromMilliseconds(90));
        }
        catch (AggregateException)
        {
        }
        _httpClient.Dispose();
    }

    public void Dispose() => Stop();

    private bool TryBindListener(out TcpListener listener)
    {
        var candidate = new TcpListener(IPAddress.Loopback, _options.Port);
        if (OperatingSystem.IsWindows())
            candidate.ExclusiveAddressUse = true;
        try
        {
            candidate.Start();
            listener = candidate;
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            listener = null!;
            return false;
        }
    }

    private void BecomeHubLocked(TcpListener listener, bool takeover)
    {
        _listener = listener;
        _registry = new MeshRegistry(_options.Clock, _options.StaleAfter, _options.RemoveAfter);
        if (_self is { } self)
            _registry.Report(self.BotId, self.Name, self.World, isHub: true, self.Status);

        _server = new MeshServer(
            listener, _registry, _options.Port,
            hubBotId: () => _self?.BotId ?? string.Empty,
            localSink: _inbound.Enqueue,
            pageBytes: _options.PageBytes,
            logInfo: _options.LogInfo,
            readContributors: _options.ReadContributors);
        _server.Start();
        _isHub = true;

        if (takeover)
            _options.LogInfo("BuffBot took over the web console hub");
        Announce();
    }

    /// <summary>Written once whenever this node binds as hub, and the opener page is rewritten
    /// only when it is stale — the link never changes for a given port.</summary>
    private void Announce()
    {
        string url = BuildLink();
        _options.LogInfo($"BuffBot web console: {url}");
        if (OpenerPageIsCurrent(url))
            return;

        try
        {
            MeshOpenerPage.Write(_options.LinkFilePath, url);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _options.LogInfo($"BuffBot web console: opener page write failed ({ex.Message})");
        }
    }

    private bool OpenerPageIsCurrent(string url)
    {
        try
        {
            string existing = File.ReadAllText(_options.LinkFilePath);
            return existing.Contains(url, StringComparison.Ordinal)
                && !existing.Contains("token=", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void BecomeSpokeLocked()
    {
        _isHub = false;
        _options.LogInfo($"BuffBot joined the web console on 127.0.0.1:{_options.Port} as a spoke");
        _spokeCts = new CancellationTokenSource();
        _spokeTask = Task.Run(() => SpokeLoopAsync(_spokeCts.Token));
    }

    private async Task SpokeLoopAsync(CancellationToken token)
    {
        TimeSpan interval = _options.HeartbeatInterval ?? TimeSpan.FromSeconds(2);
        while (!token.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatAsync(token).ConfigureAwait(false);
                await Task.Delay(interval, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                // The connection itself failed -- the hub is gone. Anything else (a wrong-shaped
                // 200, a non-2xx status) is handled inside SendHeartbeatAsync without throwing.
                if (!await TryJitterThenBecomeHubAsync(token).ConfigureAwait(false))
                    continue;
                return;
            }
        }
    }

    /// <summary>Waits a random jitter, then tries to bind. Losing the race just means staying a spoke and trying again next cycle.</summary>
    private async Task<bool> TryJitterThenBecomeHubAsync(CancellationToken token)
    {
        int maxJitterMs = (int)(_options.MaxJitter ?? TimeSpan.FromSeconds(1)).TotalMilliseconds;
        int jitterMs = maxJitterMs > 0 ? Random.Shared.Next(0, maxJitterMs + 1) : 0;
        try
        {
            await Task.Delay(jitterMs, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return true;
        }

        lock (_roleGate)
        {
            if (_stopped || token.IsCancellationRequested)
                return true;
            if (!TryBindListener(out TcpListener listener))
                return false;
            try
            {
                BecomeHubLocked(listener, takeover: true);
            }
            catch (Exception error)
            {
                // Never hold the port without a server behind it: release it and stay a spoke.
                _server?.Stop();
                _server = null;
                _isHub = false;
                listener.Stop();
                _listener = null;
                _options.LogInfo($"BuffBot web console takeover failed: {error.Message}");
                return false;
            }
            return true;
        }
    }

    private async Task SendHeartbeatAsync(CancellationToken token)
    {
        MeshSelf? self = _self;
        if (self is null)
            return;

        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"http://127.0.0.1:{_options.Port}/mesh/heartbeat")
        {
            Content = new StringContent(
                MeshJson.Heartbeat(self.BotId, self.Name, self.World, self.Status),
                Encoding.UTF8, "application/json"),
        };

        using HttpResponseMessage response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

        // Any response at all means something is answering; only a failed connection (caught by
        // the loop above) counts as the hub being gone.
        if (response.StatusCode != HttpStatusCode.OK)
            return;

        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        // Anything that doesn't parse as schema 1 with a commands array is not a BuffBot hub
        // reply -- ignored outright, never applied.
        IReadOnlyList<MeshCommand> commands = MeshJson.TryParseHeartbeatResponse(body) ?? Array.Empty<MeshCommand>();
        foreach (MeshCommand command in commands)
            _inbound.Enqueue(command);
    }
}
