using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace SolrLabs.BuffBot.Web;

/// <summary>A sealed record so <see cref="MeshNode.Publish"/> can swap it in with one reference assignment, rather than a hub and a spoke ever reading a half-updated identity/status pair.</summary>
internal sealed record MeshSelf(string BotId, string Name, string World, MeshStatus Status);

/// <summary>One node on the BuffBot web console mesh: elects itself hub or spoke by trying to bind the fixed loopback port, and from then on exposes exactly two things to <c>BuffBotPlugin</c> — <see cref="Publish"/> for the tick's status, and <see cref="TryDequeueCommand"/> for whatever the console asked for. Everything else — the HTTP server, the heartbeat loop, the takeover, the key's lifecycle — lives entirely inside this class and never calls <see cref="AcDream.Plugin.Abstractions.IPluginHost"/>.</summary>
internal sealed class MeshNode : IDisposable
{
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(2);

    private readonly MeshNodeOptions _options;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentQueue<MeshCommand> _inbound = new();
    private readonly object _roleGate = new();

    private volatile bool _isHub;
    private volatile bool _deciding;
    private volatile string _key = string.Empty;
    private volatile MeshSelf? _self;
    private TcpListener? _listener;
    private MeshServer? _server;
    private MeshRegistry? _registry;
    private CancellationTokenSource? _spokeCts;
    private Task? _spokeTask;
    private CancellationTokenSource? _graceCts;
    private Task? _graceTask;
    private DateTimeOffset _startedUtc;
    private DateTimeOffset _boundAtUtc;
    private int _decided;
    private bool _stopped;

    internal MeshNode(MeshNodeOptions options)
    {
        _options = options;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    }

    internal bool IsHub => _isHub;

    /// <summary>Whether this hub is still inside its startup grace window — <see langword="false"/> on a spoke, on a takeover hub, and once a startup hub has decided either way.</summary>
    internal bool IsDeciding => _deciding;

    /// <summary>For a spoke this can change under it (self-heal), and for a startup hub it can change once, at the moment it decides to rotate.</summary>
    internal string CurrentKey => _key;

    internal void Start()
    {
        lock (_roleGate)
        {
            _startedUtc = _options.Clock.UtcNow;
            if (TryBindListener(out TcpListener listener))
                BecomeHubAtStartupLocked(listener);
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

    /// <summary>Never returns a link while <see cref="IsDeciding"/>, since the key might still rotate, nor for a spoke whose key copy is not yet <see cref="MeshKeyStore.IsValidKey"/>.</summary>
    internal MeshConsoleLink DescribeConsole()
    {
        if (IsHub)
            return IsDeciding
                ? new MeshConsoleLink(MeshConsoleState.Starting, null)
                : new MeshConsoleLink(MeshConsoleState.Hub, BuildLink(CurrentKey));

        string key = CurrentKey;
        return new MeshConsoleLink(
            MeshConsoleState.Spoke,
            MeshKeyStore.IsValidKey(key) ? BuildLink(key) : null);
    }

    private string BuildLink(string key) => $"http://127.0.0.1:{_options.Port}/?token={key}";

    /// <summary>Stops the listener synchronously before anything else, so the port is free the instant this returns. The rest is a bounded, best-effort join: the calling thread never blocks more than a fraction of a second here.</summary>
    internal void Stop()
    {
        lock (_roleGate)
        {
            if (_stopped)
                return;
            _stopped = true;
            _spokeCts?.Cancel();
            _graceCts?.Cancel();
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
        try
        {
            _graceTask?.Wait(TimeSpan.FromMilliseconds(90));
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

    /// <summary>The only path allowed to rotate the key: reads whatever is on disk, serves with it immediately either way, and only enters a grace window if that read found something valid to serve. A fresh install skips the window entirely.</summary>
    private void BecomeHubAtStartupLocked(TcpListener listener)
    {
        _boundAtUtc = _options.Clock.UtcNow;
        bool hadValidKey = MeshKeyStore.TryRead(_options.KeyPath, out string existingKey);
        _key = hadValidKey ? existingKey : GenerateAndPersistNewKey();

        StartServerLocked(listener);
        _isHub = true;

        if (!hadValidKey)
        {
            Announce();
            return;
        }

        _decided = 0;
        _deciding = true;
        _graceCts = new CancellationTokenSource();
        _graceTask = Task.Run(() => GraceTimerAsync(_options.Grace ?? DefaultGrace, _graceCts.Token));
    }

    /// <summary>Never rotates and never waits a grace window — a takeover keeps whatever key this node already held as a spoke, restoring the file from it if the file itself is gone or broken. If the in-memory key never became valid, this re-reads the file once more, and only manufactures a fresh key if that read is still invalid.</summary>
    private void BecomeHubOnTakeoverLocked(TcpListener listener)
    {
        _boundAtUtc = _options.Clock.UtcNow;
        bool rotated = false;
        if (!MeshKeyStore.IsValidKey(_key))
        {
            if (MeshKeyStore.TryRead(_options.KeyPath, out string fileKey))
                _key = fileKey;
            else
            {
                _key = MeshKeyStore.GenerateHexKey();
                TryPersistKey(_key);
                rotated = true;
            }
        }
        else if (!MeshKeyStore.TryRead(_options.KeyPath, out _))
            TryPersistKey(_key);

        StartServerLocked(listener);
        _isHub = true;
        _deciding = false;
        _options.LogInfo("BuffBot took over the web console hub");
        if (rotated || !OpenerPageCarriesCurrentKey())
            Announce();
    }

    /// <summary>Missing, unreadable, or simply stale all answer <see langword="false"/> alike — the opener page does not self-heal on a 401 the way a spoke does, so every one of these needs the rewrite <see cref="BecomeHubOnTakeoverLocked"/> gives it.</summary>
    private bool OpenerPageCarriesCurrentKey()
    {
        try
        {
            return File.ReadAllText(_options.LinkFilePath).Contains($"token={_key}", StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void StartServerLocked(TcpListener listener)
    {
        _listener = listener;
        _registry = new MeshRegistry(_options.Clock, _options.StaleAfter, _options.RemoveAfter);
        if (_self is { } self)
            _registry.Report(self.BotId, self.Name, self.World, isHub: true, self.Status);

        _server = new MeshServer(
            listener, _registry, currentKey: () => _key, _options.Port,
            hubBotId: () => _self?.BotId ?? string.Empty,
            localSink: _inbound.Enqueue,
            pageBytes: _options.PageBytes,
            logInfo: _options.LogInfo,
            reportNodeStarted: OnHeartbeatNodeStarted,
            isDeciding: () => _deciding,
            linkFilePath: _options.LinkFilePath,
            readContributors: _options.ReadContributors);
        _server.Start();
    }

    /// <summary>Only matters while this hub is still deciding: a node whose <c>Start()</c> ran more than <see cref="MeshNodeOptions.Tolerance"/> before this hub bound is a survivor of whatever mesh was here before, and ends the window early with the key kept.</summary>
    private void OnHeartbeatNodeStarted(DateTimeOffset nodeStartedUtc)
    {
        if (!_deciding)
            return;

        TimeSpan tolerance = _options.Tolerance ?? DefaultTolerance;
        if (_boundAtUtc - nodeStartedUtc > tolerance)
            DecideKeep();
    }

    private async Task GraceTimerAsync(TimeSpan grace, CancellationToken token)
    {
        try
        {
            await Task.Delay(grace, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        DecideRotate();
    }

    private void DecideKeep()
    {
        if (Interlocked.Exchange(ref _decided, 1) != 0)
            return;
        _deciding = false;
        _graceCts?.Cancel();
        Announce();
    }

    /// <summary>A failed rotation write never wedges the hub in "deciding": if persisting the fresh key throws, this logs it once and keeps serving with whatever key was already valid.</summary>
    private void DecideRotate()
    {
        if (Interlocked.Exchange(ref _decided, 1) != 0)
            return;
        try
        {
            _key = GenerateAndPersistNewKey();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _options.LogInfo($"BuffBot web console: key rotation failed, keeping the existing key ({ex.Message})");
        }
        _deciding = false;
        Announce();
    }

    /// <summary>A failed write must never stop this node serving with the valid key it holds in memory.</summary>
    private void TryPersistKey(string key)
    {
        try
        {
            MeshKeyStore.WriteAtomic(_options.KeyPath, key);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _options.LogInfo($"BuffBot web console: could not write the mesh key ({error.Message}); serving with the key in memory.");
        }
    }

    private string GenerateAndPersistNewKey()
    {
        string key = MeshKeyStore.GenerateHexKey();
        MeshKeyStore.WriteAtomic(_options.KeyPath, key);
        return key;
    }

    /// <summary>Written exactly once, at the moment the key is settled, so a user never opens a link the hub is about to invalidate. The link is always logged first; a failed opener-page write is caught and logged on its own, never losing the announcement line.</summary>
    private void Announce()
    {
        string url = BuildLink(_key);
        _options.LogInfo($"BuffBot web console: {url}");
        try
        {
            MeshOpenerPage.Write(_options.LinkFilePath, url);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _options.LogInfo($"BuffBot web console: opener page write failed ({ex.Message})");
        }
    }

    private void BecomeSpokeLocked()
    {
        _isHub = false;
        _deciding = false;
        _key = MeshKeyStore.TryRead(_options.KeyPath, out string key) ? key : string.Empty;
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
                BecomeHubOnTakeoverLocked(listener);
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

        string key = _key;
        string nonce = MeshProof.GenerateNonce();
        using var request = new HttpRequestMessage(
            HttpMethod.Post, $"http://127.0.0.1:{_options.Port}/mesh/heartbeat")
        {
            Content = new StringContent(
                MeshJson.Heartbeat(self.BotId, self.Name, self.World, self.Status, _startedUtc),
                Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("Authorization", $"Bearer {key}");
        request.Headers.Add("X-BuffBot-Nonce", nonce);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, token).ConfigureAwait(false);

        // A stale key never counts as the hub being gone: re-read the file and try again next cycle.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            SelfHealKey();
            return;
        }
        response.EnsureSuccessStatusCode();

        string body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        IReadOnlyList<MeshCommand> commands = MeshJson.TryParseHeartbeatResponse(body) ?? Array.Empty<MeshCommand>();
        if (commands.Count == 0)
            return;

        if (!response.Headers.TryGetValues("X-BuffBot-Proof", out IEnumerable<string>? proofValues))
            return;
        string? proof = proofValues.FirstOrDefault();
        // Only commands whose proof verifies are trusted — this stops a process squatting on the port without the key from driving the bots.
        if (proof is null || !MeshProof.Verify(key, nonce, proof))
        {
            _options.LogInfo("BuffBot web console: heartbeat commands failed proof verification, discarded.");
            SelfHealKey();
            return;
        }

        foreach (MeshCommand command in commands)
            _inbound.Enqueue(command);
    }

    /// <summary>Re-reads the key file and swaps it in if it changed; called whenever the hub's answer suggests this node's key is stale.</summary>
    private void SelfHealKey()
    {
        if (MeshKeyStore.TryRead(_options.KeyPath, out string fresh) && fresh != _key)
            _key = fresh;
    }
}
