using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>Rotate only when a startup hub finds no survivor of a previous mesh, never on
/// takeover. Every window here is a handful of milliseconds; only the port and sockets are real.</summary>
public sealed class MeshKeyRotationTests : IDisposable
{
    private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan ShortTolerance = TimeSpan.FromMilliseconds(30);
    private static readonly TimeSpan FastHeartbeat = TimeSpan.FromMilliseconds(15);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(3);

    private static readonly MeshStatus SampleStatus = new(
        Enabled: true, Activity: "idle", CurrentRequesterName: null, CurrentRequesterObjectId: null, CurrentSpellLine: null,
        CurrentSpellId: 0u, StepIndex: 0, StepCount: 0, CurrentMana: 0u, MaxMana: 0u,
        Waiting: [], Counters: new MeshCounters(0, 0, 0, 0, 0), Stats: MeshStats.Empty, Recent: Array.Empty<MeshRecentEvent>());

    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "buffbot-mesh-key-rotation-" + Guid.NewGuid());
    private readonly List<MeshNode> _nodes = [];
    private readonly HttpClient _http = new();

    [Fact]
    public async Task AKeyFilePresentWithNoSpokesRotatesAfterGraceAndLogsTheLinkOnce()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string original = MeshKeyStore.EnsureKey(keyPath);
        var logs = new List<string>();

        MeshNode hub = NewNode(ReserveFreeLoopbackPort(), keyPath, linkPath, logs.Add, grace: ShortGrace);
        hub.Start();
        Assert.True(hub.IsHub);
        Assert.True(hub.IsDeciding);

        await PollUntilAsync(() => !hub.IsDeciding);

        Assert.NotEqual(original, hub.CurrentKey);
        MeshKeyStore.TryRead(keyPath, out string onDisk);
        Assert.Equal(hub.CurrentKey, onDisk);

        string opener = File.ReadAllText(linkPath);
        Assert.Contains($"token={hub.CurrentKey}", opener);

        List<string> announcements = logs.FindAll(l => l.StartsWith("BuffBot web console: http", StringComparison.Ordinal));
        Assert.Single(announcements);
        Assert.Contains(hub.CurrentKey, announcements[0]);
    }

    [Fact]
    public void NoKeyFileGeneratesOneAtOnceWithNoGraceAndLogsTheLinkOnce()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        var logs = new List<string>();

        MeshNode hub = NewNode(ReserveFreeLoopbackPort(), keyPath, linkPath, logs.Add, grace: TimeSpan.FromSeconds(30));
        hub.Start();

        Assert.True(hub.IsHub);
        Assert.False(hub.IsDeciding);
        Assert.True(MeshKeyStore.TryRead(keyPath, out string onDisk));
        Assert.Equal(hub.CurrentKey, onDisk);

        List<string> announcements = logs.FindAll(l => l.StartsWith("BuffBot web console: http", StringComparison.Ordinal));
        Assert.Single(announcements);
    }

    [Fact]
    public async Task ASurvivorHeartbeatEndsGraceEarlyAndTheSpokeNeverSeesA401()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string key = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();

        MeshNode hub = NewNode(port, keyPath, linkPath, static _ => { }, grace: ShortGrace, tolerance: ShortTolerance);
        hub.Start();
        Assert.True(hub.IsDeciding);

        // A node whose own Start() ran five seconds before this hub bound -- comfortably outside
        // the tolerance -- proves it survived whatever mesh was here before.
        DateTimeOffset longAgo = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5);
        using HttpResponseMessage response = await PostHeartbeatAsync(port, key, "local/999", longAgo);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await PollUntilAsync(() => !hub.IsDeciding);
        Assert.Equal(key, hub.CurrentKey);

        await Task.Delay(ShortGrace + ShortGrace);
        MeshKeyStore.TryRead(keyPath, out string onDisk);
        Assert.Equal(key, onDisk);
    }

    [Fact]
    public async Task HubDiesAndASpokeTakesOverKeepingTheKey()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string key = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();

        MeshNode hub = NewNode(port, keyPath, linkPath, static _ => { }, grace: TimeSpan.FromSeconds(30));
        hub.Start();
        MeshNode spoke = NewNode(port, keyPath, linkPath, static _ => { }, heartbeatInterval: FastHeartbeat, maxJitter: TimeSpan.FromMilliseconds(20));
        spoke.Publish("local/2", "Spoke", "local", SampleStatus);
        spoke.Start();
        Assert.False(spoke.IsHub);

        hub.Stop();
        await PollUntilAsync(() => spoke.IsHub);

        Assert.Equal(key, spoke.CurrentKey);
        MeshKeyStore.TryRead(keyPath, out string onDisk);
        Assert.Equal(key, onDisk);
    }

    /// <summary>Even without rotating the key, a stale opener page must still be
    /// rewritten, or the link a user double-clicks keeps answering 401.</summary>
    [Fact]
    public async Task ATakeoverRewritesAStaleOpenerPageToCarryTheKeyItActuallyServesWith()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string key = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();
        MeshOpenerPage.Write(linkPath, $"http://127.0.0.1:{port}/?token=some-older-sessions-token");

        MeshNode hub = NewNode(port, keyPath, linkPath, static _ => { }, grace: TimeSpan.FromSeconds(30));
        hub.Start();
        MeshNode spoke = NewNode(port, keyPath, linkPath, static _ => { }, heartbeatInterval: FastHeartbeat, maxJitter: TimeSpan.FromMilliseconds(20));
        spoke.Publish("local/2", "Spoke", "local", SampleStatus);
        spoke.Start();
        Assert.False(spoke.IsHub);

        hub.Stop();
        await PollUntilAsync(() => spoke.IsHub);

        Assert.Equal(key, spoke.CurrentKey); // takeover still never rotates
        string opener = File.ReadAllText(linkPath);
        Assert.Contains($"token={key}", opener); // ...but the opener page now carries what it serves
    }

    [Fact]
    public async Task ATakeoverLeavesAnOpenerPageThatAlreadyCarriesTheCurrentKeyAlone()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string key = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();
        MeshOpenerPage.Write(linkPath, $"http://127.0.0.1:{port}/?token={key}");
        DateTime writtenAtUtc = File.GetLastWriteTimeUtc(linkPath);

        MeshNode hub = NewNode(port, keyPath, linkPath, static _ => { }, grace: TimeSpan.FromSeconds(30));
        hub.Start();
        MeshNode spoke = NewNode(port, keyPath, linkPath, static _ => { }, heartbeatInterval: FastHeartbeat, maxJitter: TimeSpan.FromMilliseconds(20));
        spoke.Publish("local/2", "Spoke", "local", SampleStatus);
        spoke.Start();
        Assert.False(spoke.IsHub);

        hub.Stop();
        await PollUntilAsync(() => spoke.IsHub);

        Assert.Equal(key, spoke.CurrentKey);
        Assert.Equal(writtenAtUtc, File.GetLastWriteTimeUtc(linkPath)); // never rewritten
    }

    [Fact]
    public void ANewBotJoiningARunningMeshIsASpokeAndKeepsTheKey()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        int port = ReserveFreeLoopbackPort();

        MeshNode hub = NewNode(port, keyPath, linkPath, static _ => { });
        hub.Start();
        Assert.True(hub.IsHub);

        MeshNode joiner = NewNode(port, keyPath, linkPath, static _ => { });
        joiner.Start();

        Assert.False(joiner.IsHub);
        Assert.Equal(hub.CurrentKey, joiner.CurrentKey);
    }

    [Fact]
    public async Task SeveralColdStartedNodesRotateExactlyOnceAndEveryNodeConverges()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string original = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();
        var logs = new List<string>();

        MeshNode hub = NewNode(port, keyPath, linkPath, logs.Add, grace: ShortGrace, tolerance: ShortTolerance);
        hub.Start();
        MeshNode spokeA = NewNode(port, keyPath, linkPath, static _ => { }, heartbeatInterval: FastHeartbeat);
        MeshNode spokeB = NewNode(port, keyPath, linkPath, static _ => { }, heartbeatInterval: FastHeartbeat);
        spokeA.Publish("local/2", "SpokeA", "local", SampleStatus);
        spokeB.Publish("local/3", "SpokeB", "local", SampleStatus);
        spokeA.Start();
        spokeB.Start();

        await PollUntilAsync(() => !hub.IsDeciding);
        Assert.NotEqual(original, hub.CurrentKey);

        await PollUntilAsync(() => spokeA.CurrentKey == hub.CurrentKey && spokeB.CurrentKey == hub.CurrentKey);

        List<string> announcements = logs.FindAll(l => l.StartsWith("BuffBot web console: http", StringComparison.Ordinal));
        Assert.Single(announcements);
    }

    [Fact]
    public async Task ASpokeHoldingAStaleKeyGetsA401ThenSelfHealsAndHeartbeatsSuccessfully()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string original = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();

        MeshNode hub = NewNode(port, keyPath, linkPath, static _ => { }, grace: ShortGrace, tolerance: ShortTolerance);
        hub.Start();
        MeshNode spoke = NewNode(port, keyPath, linkPath, static _ => { }, heartbeatInterval: FastHeartbeat);
        spoke.Publish("local/2", "Spoke", "local", SampleStatus);
        spoke.Start();

        Assert.Equal(original, spoke.CurrentKey);

        await PollUntilAsync(() => !hub.IsDeciding);
        Assert.NotEqual(original, hub.CurrentKey);

        await PollUntilAsync(() => spoke.CurrentKey == hub.CurrentKey);
        Assert.False(spoke.IsHub); // the stale key never looked like a dead hub to the spoke
    }

    [Fact]
    public async Task ASpokeStartingWithAnUnreadableKeyFileTakesOverWithAFreshValidKey()
    {
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        Directory.CreateDirectory(_tempDirectory);
        File.WriteAllText(keyPath, "not-a-valid-key");
        int port = ReserveFreeLoopbackPort();
        var logs = new List<string>();

        // Something already holds the port -- with no key of its own to hand out -- so Start()
        // finds a spoke role while the key file on disk stays exactly as broken as it started.
        var decoy = new TcpListener(IPAddress.Loopback, port);
        decoy.Start();
        var decoyCts = new CancellationTokenSource();
        Task decoyLoop = AcceptAndCloseLoopAsync(decoy, decoyCts.Token);

        MeshNode spoke = NewNode(
            port, keyPath, linkPath, logs.Add, heartbeatInterval: FastHeartbeat, maxJitter: TimeSpan.FromMilliseconds(20));
        spoke.Publish("local/2", "Spoke", "local", SampleStatus);
        spoke.Start();
        Assert.False(spoke.IsHub);
        Assert.Equal(string.Empty, spoke.CurrentKey);

        decoyCts.Cancel();
        try
        {
            decoy.Stop();
        }
        catch (SocketException)
        {
        }
        await decoyLoop;

        await PollUntilAsync(() => spoke.IsHub);

        Assert.True(MeshKeyStore.IsValidKey(spoke.CurrentKey));
        MeshKeyStore.TryRead(keyPath, out string onDisk);
        Assert.Equal(spoke.CurrentKey, onDisk);

        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/bots");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer ");
        using HttpResponseMessage response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        List<string> announcements = logs.FindAll(l => l.StartsWith("BuffBot web console: http", StringComparison.Ordinal));
        Assert.Single(announcements);
        Assert.Contains(spoke.CurrentKey, announcements[0]);
    }

    [Fact]
    public async Task RotationWithAnUnwritableKeyDirectoryKeepsTheOriginalKeyAndStillAnnouncesOnce()
    {
        if (OperatingSystem.IsWindows())
            return;

        string keyDirectory = Path.Combine(_tempDirectory, "locked");
        string keyPath = Path.Combine(keyDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string original = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();
        var logs = new List<string>();

        new DirectoryInfo(keyDirectory).UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserExecute;
        try
        {
            MeshNode hub = NewNode(port, keyPath, linkPath, logs.Add, grace: ShortGrace);
            hub.Start();
            Assert.True(hub.IsDeciding);

            await PollUntilAsync(() => !hub.IsDeciding);

            Assert.Equal(original, hub.CurrentKey);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/bots");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", original);
            using HttpResponseMessage response = await _http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            List<string> announcements = logs.FindAll(l => l.StartsWith("BuffBot web console: http", StringComparison.Ordinal));
            Assert.Single(announcements);
            Assert.Contains(original, announcements[0]);
        }
        finally
        {
            new DirectoryInfo(keyDirectory).UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }
    }

    [Fact]
    public async Task ATakeoverThatCannotRestoreAMissingKeyFileStillServesWithTheKeyInMemory()
    {
        if (OperatingSystem.IsWindows())
            return;

        string keyDirectory = Path.Combine(_tempDirectory, "locked-takeover");
        string keyPath = Path.Combine(keyDirectory, "mesh.key");
        string linkPath = Path.Combine(_tempDirectory, "opener.html");
        string key = MeshKeyStore.EnsureKey(keyPath);
        int port = ReserveFreeLoopbackPort();

        MeshNode hub = NewNode(port, keyPath, linkPath, static _ => { }, grace: TimeSpan.FromSeconds(30));
        hub.Start();
        MeshNode spoke = NewNode(port, keyPath, linkPath, static _ => { }, heartbeatInterval: FastHeartbeat, maxJitter: TimeSpan.FromMilliseconds(20));
        spoke.Publish("local/2", "Spoke", "local", SampleStatus);
        spoke.Start();
        Assert.False(spoke.IsHub);

        File.Delete(keyPath);
        new DirectoryInfo(keyDirectory).UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserExecute;
        try
        {
            hub.Stop();
            await PollUntilAsync(() => spoke.IsHub);

            Assert.Equal(key, spoke.CurrentKey);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"http://127.0.0.1:{port}/api/bots");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using HttpResponseMessage response = await _http.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            new DirectoryInfo(keyDirectory).UnixFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        }
    }

    private static async Task AcceptAndCloseLoopAsync(TcpListener listener, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using TcpClient client = await listener.AcceptTcpClientAsync(token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return;
            }
        }
    }

    private async Task<HttpResponseMessage> PostHeartbeatAsync(int port, string key, string botId, DateTimeOffset nodeStartedUtc)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"http://127.0.0.1:{port}/mesh/heartbeat")
        {
            Content = new StringContent(MeshJson.Heartbeat(botId, "Survivor", "local", SampleStatus, nodeStartedUtc)),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Headers.Add("X-BuffBot-Nonce", MeshProof.GenerateNonce());
        return await _http.SendAsync(request);
    }

    private MeshNode NewNode(
        int port, string keyPath, string linkPath, Action<string> logInfo,
        TimeSpan? grace = null, TimeSpan? tolerance = null,
        TimeSpan? heartbeatInterval = null, TimeSpan? maxJitter = null)
    {
        var node = new MeshNode(new MeshNodeOptions(
            Port: port,
            KeyPath: keyPath,
            LinkFilePath: linkPath,
            Clock: SystemClock.Instance,
            PageBytes: "<html></html>"u8.ToArray(),
            LogInfo: logInfo,
            HeartbeatInterval: heartbeatInterval ?? FastHeartbeat,
            MaxJitter: maxJitter ?? TimeSpan.FromMilliseconds(20),
            Grace: grace,
            Tolerance: tolerance));
        _nodes.Add(node);
        return node;
    }

    private static async Task PollUntilAsync(Func<bool> predicate)
    {
        DateTime deadline = DateTime.UtcNow + PollTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (predicate())
                return;
            await Task.Delay(10);
        }
        throw new TimeoutException("condition never became true within the poll timeout.");
    }

    private static int ReserveFreeLoopbackPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public void Dispose()
    {
        foreach (MeshNode node in _nodes)
            node.Dispose();
        _http.Dispose();
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
