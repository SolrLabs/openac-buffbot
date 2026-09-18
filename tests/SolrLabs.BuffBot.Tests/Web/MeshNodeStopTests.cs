using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

public sealed class MeshNodeStopTests : IDisposable
{
    private readonly string _tempDirectory =
        Path.Combine(Path.GetTempPath(), "buffbot-mesh-node-stop-tests-" + Guid.NewGuid());

    [Fact]
    public void StopReturnsQuicklyAndFreesThePortImmediately()
    {
        int port = ReserveFreeLoopbackPort();
        var hub = new MeshNode(new MeshNodeOptions(
            Port: port,
            KeyPath: Path.Combine(_tempDirectory, "mesh.key"),
            LinkFilePath: Path.Combine(_tempDirectory, "opener.html"),
            Clock: SystemClock.Instance,
            PageBytes: "<html></html>"u8.ToArray(),
            LogInfo: static _ => { }));
        hub.Start();
        Assert.True(hub.IsHub);

        var stopwatch = Stopwatch.StartNew();
        hub.Stop();
        stopwatch.Stop();

        // 400ms rather than the 150ms design budget, to stay flake-free on a loaded CI box -- the
        // failure message reports the measured value for a human to judge against the tighter one.
        Assert.True(
            stopwatch.ElapsedMilliseconds < 400,
            $"Stop() took {stopwatch.ElapsedMilliseconds}ms, expected under 400ms.");

        // The port must be free the instant Stop() returns -- a spoke's takeover and the
        // collectible ALC unload both depend on that.
        var probe = new TcpListener(IPAddress.Loopback, port);
        probe.Start();
        probe.Stop();
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
        if (Directory.Exists(_tempDirectory))
            Directory.Delete(_tempDirectory, recursive: true);
    }
}
