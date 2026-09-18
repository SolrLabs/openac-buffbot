using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Tests.Timing;

namespace SolrLabs.BuffBot.Tests.Stats;

/// <summary>Host-free coverage of <see cref="BotStats"/> — the one call each real event site
/// makes, and that it reaches both <see cref="SessionStats"/> and the Recent ring.</summary>
public sealed class BotStatsTests
{
    [Fact]
    public void ALandedCastUpdatesTheLineTallyAndTheRingInOneCall()
    {
        var stats = new BotStats(new FakeClock());
        stats.RecordCastLanded("Strength Other", "Archer", isPlayerRequest: true);

        BotStatsSnapshot snapshot = stats.Snapshot();
        Assert.Equal(1, Assert.Single(snapshot.Session.Lines).Landed);

        RecentEvent entry = Assert.Single(snapshot.Recent);
        Assert.Equal(RecentEventKind.Cast, entry.Kind);
        Assert.Equal("Strength Other", entry.Line);
        Assert.Equal("Archer", entry.TargetName);
    }

    [Fact]
    public void ARefusalNamesItsReasonOnTheRingAndCountsItOnTheSession()
    {
        var stats = new BotStats(new FakeClock());
        stats.RecordRefusal(RefusalReason.OutOfRange, "Archer");

        Assert.Equal(1, stats.Snapshot().Session.Refusals.OutOfRange);
        RecentEvent entry = Assert.Single(stats.Snapshot().Recent);
        Assert.Equal(RecentEventKind.Refused, entry.Kind);
        Assert.Equal("Archer", entry.Who);
        Assert.Equal("out of range", entry.Reason);
    }

    [Fact]
    public void MuteReleaseAndUnwedgeCarryWhoAndSource()
    {
        var stats = new BotStats(new FakeClock());
        stats.RecordMute("Nuisance", "web console");
        stats.RecordRelease("Nuisance", "operator panel");
        stats.RecordUnwedge("/buffbot unwedge");

        IReadOnlyList<RecentEvent> recent = stats.Snapshot().Recent; // newest first
        Assert.Equal(RecentEventKind.Unwedge, recent[0].Kind);
        Assert.Equal("/buffbot unwedge", recent[0].Source);
        Assert.Equal(RecentEventKind.Release, recent[1].Kind);
        Assert.Equal("operator panel", recent[1].Source);
        Assert.Equal(RecentEventKind.Mute, recent[2].Kind);
        Assert.Equal("web console", recent[2].Source);
    }

    [Fact]
    public void ManaBounceCarriesBeforeAndAfterOnTheRingOnly()
    {
        var stats = new BotStats(new FakeClock());
        stats.RecordManaBounce(before: 100u, after: 400u);

        RecentEvent entry = Assert.Single(stats.Snapshot().Recent);
        Assert.Equal(100u, entry.ManaBefore);
        Assert.Equal(400u, entry.ManaAfter);
        Assert.Empty(stats.Snapshot().Session.Lines); // never a per-line statistic
    }

    /// <summary>A donations counter and an items-received counter, both accumulating across
    /// more than one completed trade.</summary>
    [Fact]
    public void DonationsAccumulateAcrossMoreThanOneCompletedTrade()
    {
        var stats = new BotStats(new FakeClock());
        stats.RecordDonation(itemCount: 3);
        stats.RecordDonation(itemCount: 1);

        BotStatsSnapshot snapshot = stats.Snapshot();
        Assert.Equal(2, snapshot.DonationsCompleted);
        Assert.Equal(4, snapshot.DonationItemsReceived);
    }
}
