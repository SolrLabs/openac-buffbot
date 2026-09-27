using System.Net;
using System.Net.Sockets;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class MeshConsoleLinkTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "buffbot-mesh-console-link-" + Guid.NewGuid());
    private readonly List<MeshNode> _nodes = [];

    [Fact]
    public void AHubReportsItsLinkAtOnceWithNoGraceWait()
    {
        int port = ReserveFreeLoopbackPort();
        MeshNode hub = NewNode(port);
        hub.Start();

        MeshConsoleLink console = hub.DescribeConsole();

        Assert.Equal(MeshConsoleState.Hub, console.State);
        Assert.Equal($"http://127.0.0.1:{port}/", console.Link);
    }

    [Fact]
    public void ASpokeOffersTheSameLinkTheHubWould()
    {
        int port = ReserveFreeLoopbackPort();
        MeshNode hub = NewNode(port);
        hub.Start();
        MeshNode spoke = NewNode(port);
        spoke.Start();
        Assert.False(spoke.IsHub);

        MeshConsoleLink console = spoke.DescribeConsole();

        Assert.Equal(MeshConsoleState.Spoke, console.State);
        Assert.Equal(hub.DescribeConsole().Link, console.Link);
    }

    /// <summary>No token, no query string at all — the link is just scheme, loopback and port.</summary>
    [Fact]
    public void TheLinkNeverCarriesAToken()
    {
        int port = ReserveFreeLoopbackPort();
        MeshNode hub = NewNode(port);
        hub.Start();

        string? link = hub.DescribeConsole().Link;

        Assert.NotNull(link);
        Assert.DoesNotContain("token", link);
        Assert.DoesNotContain('?', link);
        Assert.Equal($"http://127.0.0.1:{port}/", link);
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
            MeshNode node = NewNode(port);
            node.Start();
            return node;
        });
        supervisor.Tick(enabled: true);

        MeshConsoleLink console = supervisor.DescribeConsole();

        Assert.Equal(MeshConsoleState.Hub, console.State);
        Assert.NotNull(console.Link);
    }

    private MeshNode NewNode(int port)
    {
        Directory.CreateDirectory(_tempDirectory);

        var node = new MeshNode(new MeshNodeOptions(
            Port: port,
            LinkFilePath: Path.Combine(_tempDirectory, "opener.html"),
            Clock: SystemClock.Instance,
            PageBytes: "<html></html>"u8.ToArray(),
            LogInfo: static _ => { },
            HeartbeatInterval: TimeSpan.FromMilliseconds(15),
            MaxJitter: TimeSpan.FromMilliseconds(20)));
        _nodes.Add(node);
        return node;
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
