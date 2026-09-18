using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>Host-free, fake-clock-driven coverage of the hub's registry: staleness
/// at 6s, removal at 120s, and the per-bot pending-command hand-out a spoke's heartbeat drains.</summary>
public sealed class MeshRegistryTests
{
    private static readonly MeshStatus Empty = new(
        Enabled: true, Activity: "idle", CurrentRequesterName: null, CurrentRequesterObjectId: null, CurrentSpellLine: null,
        CurrentSpellId: 0u, StepIndex: 0, StepCount: 0, CurrentMana: 0u, MaxMana: 0u,
        Waiting: [], Counters: new MeshCounters(0, 0, 0, 0, 0), Stats: MeshStats.Empty, Recent: Array.Empty<MeshRecentEvent>());

    [Fact]
    public void ASnapshotWithNoReportsYetIsEmpty()
    {
        var registry = NewRegistry(out _);

        Assert.Empty(registry.Snapshot().Bots);
    }

    [Fact]
    public void ReportedBotsAppearInTheSnapshotWithTheHubFlagged()
    {
        var registry = NewRegistry(out _);

        registry.Report("local/1", "Alice", "local", isHub: true, Empty);
        registry.Report("world/2", "Bob", "world", isHub: false, Empty);

        MeshBotsSnapshot snapshot = registry.Snapshot();
        Assert.Equal("local/1", snapshot.HubBotId);
        Assert.Equal(2, snapshot.Bots.Count);
        MeshBot hub = Assert.Single(snapshot.Bots, b => b.BotId == "local/1");
        Assert.True(hub.IsHub);
        MeshBot spoke = Assert.Single(snapshot.Bots, b => b.BotId == "world/2");
        Assert.False(spoke.IsHub);
    }

    [Fact]
    public void ABotIsNotStaleImmediatelyAfterReporting()
    {
        var registry = NewRegistry(out FakeClock clock);
        registry.Report("local/1", "Alice", "local", isHub: true, Empty);

        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.False(registry.Snapshot().Bots.Single().Stale);
    }

    [Fact]
    public void ABotGoesStaleAfterSixSecondsWithoutAReport()
    {
        var registry = NewRegistry(out FakeClock clock);
        registry.Report("local/1", "Alice", "local", isHub: true, Empty);

        clock.Advance(TimeSpan.FromSeconds(6).Add(TimeSpan.FromMilliseconds(1)));

        Assert.True(registry.Snapshot().Bots.Single().Stale);
    }

    [Fact]
    public void AFreshReportClearsStaleness()
    {
        var registry = NewRegistry(out FakeClock clock);
        registry.Report("local/1", "Alice", "local", isHub: true, Empty);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.True(registry.Snapshot().Bots.Single().Stale);

        registry.Report("local/1", "Alice", "local", isHub: true, Empty);

        Assert.False(registry.Snapshot().Bots.Single().Stale);
    }

    [Fact]
    public void ABotIsRemovedEntirelyAfterOneHundredAndTwentySeconds()
    {
        var registry = NewRegistry(out FakeClock clock);
        registry.Report("local/1", "Alice", "local", isHub: true, Empty);

        clock.Advance(TimeSpan.FromSeconds(120).Add(TimeSpan.FromMilliseconds(1)));

        Assert.Empty(registry.Snapshot().Bots);
    }

    [Fact]
    public void ACommandQueuedForAKnownBotIsHandedOutOnceThenDrained()
    {
        var registry = NewRegistry(out _);
        registry.Report("world/2", "Bob", "world", isHub: false, Empty);

        registry.EnqueueCommand("world/2", new MeshCommand(MeshCommandKind.Drain, null));

        IReadOnlyList<MeshCommand> first = registry.DrainCommands("world/2");
        Assert.Single(first);
        Assert.Empty(registry.DrainCommands("world/2"));
    }

    [Fact]
    public void ACommandQueuedForOneBotDoesNotReachAnother()
    {
        var registry = NewRegistry(out _);
        registry.Report("world/2", "Bob", "world", isHub: false, Empty);
        registry.Report("world/3", "Carol", "world", isHub: false, Empty);

        registry.EnqueueCommand("world/2", new MeshCommand(MeshCommandKind.Mute, 9u));

        Assert.Single(registry.DrainCommands("world/2"));
        Assert.Empty(registry.DrainCommands("world/3"));
    }

    [Fact]
    public void EnqueueingForAnUnknownBotIsANoOp()
    {
        var registry = NewRegistry(out _);

        registry.EnqueueCommand("nobody/1", new MeshCommand(MeshCommandKind.Drain, null));

        Assert.False(registry.Exists("nobody/1"));
    }

    private static MeshRegistry NewRegistry(out FakeClock clock)
    {
        clock = new FakeClock();
        return new MeshRegistry(clock);
    }
}
