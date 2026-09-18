using System.Net;
using System.Net.Sockets;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class MeshConsoleLinkTests : IDisposable
{
    private static readonly TimeSpan ShortGrace = TimeSpan.FromMilliseconds(80);
    private static readonly TimeSpan PollTimeout = TimeSpan.FromSeconds(3);

    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "buffbot-mesh-console-link-" + Guid.NewGuid());
    private readonly List<MeshNode> _nodes = [];

    [Fact]
    public void AHubStillDecidingReportsStartingWithNoLink()
    {
        MeshNode hub = NewNode(ReserveFreeLoopbackPort(), grace: ShortGrace, existingKey: true);
        hub.Start();

        MeshConsoleLink console = hub.DescribeConsole();

        Assert.Equal(MeshConsoleState.Starting, console.State);
        Assert.Null(console.Link);
    }

    [Fact]
    public async Task AHubThatHasDecidedReportsHubWithTheCurrentKeysLink()
    {
        int port = ReserveFreeLoopbackPort();
        MeshNode hub = NewNode(port, grace: ShortGrace, existingKey: true);
        hub.Start();

        await PollUntilAsync(() => !hub.IsDeciding);
        MeshConsoleLink console = hub.DescribeConsole();

        Assert.Equal(MeshConsoleState.Hub, console.State);
        Assert.Equal($"http://127.0.0.1:{port}/?token={hub.CurrentKey}", console.Link);
    }

    [Fact]
    public void AFreshHubWithNoPriorKeySkipsStartingAndReportsHubAtOnce()
    {
        int port = ReserveFreeLoopbackPort();
        MeshNode hub = NewNode(port, existingKey: false);
        hub.Start();

        MeshConsoleLink console = hub.DescribeConsole();

        Assert.Equal(MeshConsoleState.Hub, console.State);
        Assert.Equal($"http://127.0.0.1:{port}/?token={hub.CurrentKey}", console.Link);
    }

    [Fact]
    public void ASpokeWithAValidKeyOffersTheSameLinkTheHubWould()
    {
        int port = ReserveFreeLoopbackPort();
        MeshNode hub = NewNode(port, existingKey: false);
        hub.Start();
        MeshNode spoke = NewNode(port, existingKey: false);
        spoke.Start();
        Assert.False(spoke.IsHub);

        MeshConsoleLink console = spoke.DescribeConsole();

        Assert.Equal(MeshConsoleState.Spoke, console.State);
        Assert.Equal($"http://127.0.0.1:{port}/?token={hub.CurrentKey}", console.Link);
    }

    /// <summary>A spoke that could never read a valid key is still a spoke, but never offers a
    /// link built on an invalid one.</summary>
    [Fact]
    public void ASpokeThatNeverReadAValidKeyReportsSpokeWithNoLink()
    {
        // No key file and a held port: it becomes a spoke with CurrentKey empty.
        var decoy = new TcpListener(IPAddress.Loopback, 0);
        decoy.Start();
        int port = ((IPEndPoint)decoy.LocalEndpoint).Port;
        try
        {
            MeshNode spoke = NewNode(port, existingKey: false);
            spoke.Start();
            Assert.False(spoke.IsHub);
            Assert.Equal(string.Empty, spoke.CurrentKey);

            MeshConsoleLink console = spoke.DescribeConsole();

            Assert.Equal(MeshConsoleState.Spoke, console.State);
            Assert.Null(console.Link);
        }
        finally
        {
            decoy.Stop();
        }
    }

    [Fact]
    public void ASupervisorWithNoNodeReportsUnavailable()
    {
        var supervisor = new MeshSupervisor(SystemClock.Instance, static _ => { }, static _ => { }, () =>
            throw new InvalidOperationException("never asked to start in this test"));

        MeshConsoleLink console = supervisor.DescribeConsole();

        Assert.Equal(MeshConsoleState.Unavailable, console.State);
        Assert.Null(console.Link);
    }

    [Fact]
    public void ASupervisorReportsWhateverItsRunningNodeReports()
    {
        int port = ReserveFreeLoopbackPort();
        var supervisor = new MeshSupervisor(SystemClock.Instance, static _ => { }, static _ => { }, () =>
        {
            MeshNode node = NewNode(port, existingKey: false);
            node.Start();
            return node;
        });
        supervisor.Tick(enabled: true);

        MeshConsoleLink console = supervisor.DescribeConsole();

        Assert.Equal(MeshConsoleState.Hub, console.State);
        Assert.NotNull(console.Link);
    }

    private MeshNode NewNode(int port, bool existingKey, TimeSpan? grace = null)
    {
        Directory.CreateDirectory(_tempDirectory);
        string keyPath = Path.Combine(_tempDirectory, "mesh.key");
        if (existingKey)
            MeshKeyStore.EnsureKey(keyPath);

        var node = new MeshNode(new MeshNodeOptions(
            Port: port,
            KeyPath: keyPath,
            LinkFilePath: Path.Combine(_tempDirectory, "opener.html"),
            Clock: SystemClock.Instance,
            PageBytes: "<html></html>"u8.ToArray(),
            LogInfo: static _ => { },
            HeartbeatInterval: TimeSpan.FromMilliseconds(15),
            MaxJitter: TimeSpan.FromMilliseconds(20),
            Grace: grace));
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
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
