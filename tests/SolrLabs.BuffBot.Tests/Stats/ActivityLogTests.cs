using SolrLabs.BuffBot.Stats;

namespace SolrLabs.BuffBot.Tests.Stats;

/// <summary>Host-free coverage of <see cref="ActivityLog"/>'s own bound and order: the last
/// <see cref="ActivityLog.Capacity"/> events, newest first.</summary>
public sealed class ActivityLogTests
{
    [Fact]
    public void EventsComeBackNewestFirst()
    {
        var log = new ActivityLog();
        log.Add(RecentEvent.Fizzle(Time(0), "First"));
        log.Add(RecentEvent.Fizzle(Time(1), "Second"));
        log.Add(RecentEvent.Fizzle(Time(2), "Third"));

        IReadOnlyList<RecentEvent> snapshot = log.Snapshot();

        Assert.Equal(["Third", "Second", "First"], snapshot.Select(e => e.Line));
    }

    [Fact]
    public void TheRingNeverGrowsPastItsCapacity()
    {
        var log = new ActivityLog();
        for (int i = 0; i < ActivityLog.Capacity + 25; i++)
            log.Add(RecentEvent.Fizzle(Time(i), $"Line {i}"));

        IReadOnlyList<RecentEvent> snapshot = log.Snapshot();

        Assert.Equal(ActivityLog.Capacity, snapshot.Count);
    }

    [Fact]
    public void PastCapacityTheOldestEventsAreTheOnesDropped()
    {
        var log = new ActivityLog();
        for (int i = 0; i < ActivityLog.Capacity + 25; i++)
            log.Add(RecentEvent.Fizzle(Time(i), $"Line {i}"));

        IReadOnlyList<RecentEvent> snapshot = log.Snapshot();

        // Newest first, so index 0 is the very last one added, and the ring holds exactly the
        // most recent Capacity entries -- the oldest 25 (Line 0..24) are gone.
        Assert.Equal($"Line {ActivityLog.Capacity + 24}", snapshot[0].Line);
        Assert.DoesNotContain(snapshot, e => e.Line == "Line 0");
    }

    private static DateTimeOffset Time(int offsetSeconds) =>
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + TimeSpan.FromSeconds(offsetSeconds);
}
