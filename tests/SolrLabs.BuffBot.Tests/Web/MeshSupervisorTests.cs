using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>A failing web console must never take buffing down with it: a factory that throws is one Warn and a retry no sooner than 30s later, never an
/// exception out of <see cref="MeshSupervisor.Tick"/>.</summary>
public sealed class MeshSupervisorTests
{
    [Fact]
    public void ATickThatFailsToStartLogsOneWarnAndDoesNotThrow()
    {
        var clock = new FakeClock();
        var warnings = new List<string>();
        int factoryCalls = 0;
        var supervisor = new MeshSupervisor(clock, _ => { }, warnings.Add, () =>
        {
            factoryCalls++;
            throw new InvalidOperationException("port already in use by something else");
        });

        var exception = Record.Exception(() => supervisor.Tick(enabled: true));

        Assert.Null(exception);
        Assert.Equal(1, factoryCalls);
        Assert.Single(warnings);
        Assert.Contains("port already in use by something else", warnings[0]);
    }

    [Fact]
    public void TheFactoryIsNotRetriedBeforeThirtySecondsButIsAfter()
    {
        var clock = new FakeClock();
        var warnings = new List<string>();
        int factoryCalls = 0;
        var supervisor = new MeshSupervisor(clock, _ => { }, warnings.Add, () =>
        {
            factoryCalls++;
            throw new InvalidOperationException("boom");
        });

        supervisor.Tick(enabled: true);
        Assert.Equal(1, factoryCalls);

        clock.Advance(TimeSpan.FromSeconds(29));
        supervisor.Tick(enabled: true);
        Assert.Equal(1, factoryCalls); // too soon -- no second attempt, no second warning

        clock.Advance(TimeSpan.FromSeconds(2));
        supervisor.Tick(enabled: true);
        Assert.Equal(2, factoryCalls);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void RecoveringAfterAFailureLogsOneInfo()
    {
        var clock = new FakeClock();
        var infos = new List<string>();
        var warnings = new List<string>();
        int factoryCalls = 0;
        var supervisor = new MeshSupervisor(clock, infos.Add, warnings.Add, () =>
        {
            factoryCalls++;
            if (factoryCalls == 1)
                throw new InvalidOperationException("first attempt fails");
            return NewThrowawayNode();
        });

        supervisor.Tick(enabled: true);
        Assert.Single(warnings);
        Assert.Empty(infos);

        clock.Advance(TimeSpan.FromSeconds(30));
        supervisor.Tick(enabled: true);

        Assert.Single(infos);
        supervisor.Stop();
    }

    [Fact]
    public void StopAfterAFailedStartDoesNotThrow()
    {
        var clock = new FakeClock();
        var supervisor = new MeshSupervisor(clock, _ => { }, _ => { }, () =>
            throw new InvalidOperationException("never starts"));

        supervisor.Tick(enabled: true);
        var exception = Record.Exception(supervisor.Stop);

        Assert.Null(exception);
    }

    [Fact]
    public void StoppingTwiceDoesNotThrow()
    {
        var clock = new FakeClock();
        var supervisor = new MeshSupervisor(clock, _ => { }, _ => { }, NewThrowawayNode);

        supervisor.Tick(enabled: true);
        supervisor.Stop();
        var exception = Record.Exception(supervisor.Stop);

        Assert.Null(exception);
    }

    /// <summary>Never started, never touches a socket.</summary>
    private static MeshNode NewThrowawayNode() => new(new MeshNodeOptions(
        Port: 0,
        KeyPath: Path.Combine(Path.GetTempPath(), "buffbot-mesh-supervisor-tests-" + Guid.NewGuid(), "mesh.key"),
        LinkFilePath: Path.Combine(Path.GetTempPath(), "buffbot-mesh-supervisor-tests-" + Guid.NewGuid(), "opener.html"),
        Clock: SystemClock.Instance,
        PageBytes: Array.Empty<byte>(),
        LogInfo: static _ => { }));
}
