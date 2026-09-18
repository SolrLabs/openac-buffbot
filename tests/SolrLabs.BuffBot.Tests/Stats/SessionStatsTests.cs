using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Tests.Timing;

namespace SolrLabs.BuffBot.Tests.Stats;

public sealed class SessionStatsTests
{
    [Fact]
    public void RefusalsCountByReasonAndByTotal()
    {
        var stats = new SessionStats(new FakeClock());
        stats.RecordRefusal(RefusalReason.OutOfRange);
        stats.RecordRefusal(RefusalReason.OutOfRange);
        stats.RecordRefusal(RefusalReason.UnknownLine);
        stats.RecordRefusal(RefusalReason.NothingLearned);

        RefusalCounts refusals = stats.Snapshot().Refusals;

        Assert.Equal(2, refusals.OutOfRange);
        Assert.Equal(1, refusals.UnknownLine);
        Assert.Equal(1, refusals.NothingLearned);
        Assert.Equal(4, refusals.Total);
    }

    [Fact]
    public void OnlyLinesWithAtLeastOneAttemptReachTheSnapshot()
    {
        var stats = new SessionStats(new FakeClock());
        stats.RecordCastLanded("Strength Other", isPlayerRequest: true);

        IReadOnlyList<SpellLineStats> lines = stats.Snapshot().Lines;

        SpellLineStats line = Assert.Single(lines);
        Assert.Equal("Strength Other", line.Line);
        Assert.Equal(1, line.Attempts);
        Assert.Equal(1, line.Landed);
        Assert.Equal(0, line.Fizzles);
    }

    [Fact]
    public void FizzlesAndOtherFailuresCountAsAttemptsWithoutCountingAsLanded()
    {
        var stats = new SessionStats(new FakeClock());
        stats.RecordCastLanded("Focus Other", isPlayerRequest: true);
        stats.RecordFizzle("Focus Other");
        stats.RecordFizzle("Focus Other");
        stats.RecordOtherAttemptFailure("Focus Other");

        SpellLineStats line = Assert.Single(stats.Snapshot().Lines);
        Assert.Equal(4, line.Attempts);
        Assert.Equal(1, line.Landed);
        Assert.Equal(2, line.Fizzles);
    }

    [Fact]
    public void ASkippedOrNeverAttemptedLineNeverReachesTheSnapshot()
    {
        // Skips and "not learned" reach the Recent ring, never SessionStats.
        var stats = new SessionStats(new FakeClock());

        Assert.Empty(stats.Snapshot().Lines);
    }

    [Fact]
    public void RequestedAndUpkeepCastsLandInDifferentHourlyBuckets()
    {
        var stats = new SessionStats(new FakeClock());
        stats.RecordCastLanded("Strength Other", isPlayerRequest: true);
        stats.RecordCastLanded("Focus Self", isPlayerRequest: false);
        stats.RecordCastLanded("Focus Self", isPlayerRequest: false);

        HourlyBucket currentHour = stats.Snapshot().Hours[^1];

        Assert.Equal(1, currentHour.Requested);
        Assert.Equal(2, currentHour.Upkeep);
    }

    [Fact]
    public void HourlyBucketsCoverExactlyTheLastTwelveHoursAndRollOverAsTheClockAdvances()
    {
        var clock = new FakeClock();
        var stats = new SessionStats(clock);
        stats.RecordCastLanded("Strength Other", isPlayerRequest: true); // hour 0

        clock.Advance(TimeSpan.FromHours(11));
        stats.RecordCastLanded("Strength Other", isPlayerRequest: true); // hour 11 -- still visible

        IReadOnlyList<HourlyBucket> hours = stats.Snapshot().Hours;
        Assert.Equal(12, hours.Count);
        Assert.Equal(1, hours[0].Requested); // hour 0, still the oldest of the 12
        Assert.Equal(1, hours[^1].Requested); // hour 11, the current one

        clock.Advance(TimeSpan.FromHours(1)); // hour 0 rolls off the 12-hour window
        hours = stats.Snapshot().Hours;
        Assert.Equal(0, hours[0].Requested); // what was hour 1 (always empty here)
        Assert.Equal(1, hours[^2].Requested); // what was hour 11, now second-from-last
    }

    [Fact]
    public void FizzlesLandInTheCurrentHourlyBucketAlongsideRequestedAndUpkeep()
    {
        var stats = new SessionStats(new FakeClock());
        stats.RecordCastLanded("Strength Other", isPlayerRequest: true);
        stats.RecordFizzle("Strength Other");
        stats.RecordFizzle("Focus Other");

        HourlyBucket currentHour = stats.Snapshot().Hours[^1];

        Assert.Equal(1, currentHour.Requested);
        Assert.Equal(0, currentHour.Upkeep);
        Assert.Equal(2, currentHour.Fizzles);
    }

    [Fact]
    public void AFizzleInAnOlderHourStaysInThatHoursBucketAsTheClockAdvances()
    {
        var clock = new FakeClock();
        var stats = new SessionStats(clock);
        stats.RecordFizzle("Strength Other"); // hour 0

        clock.Advance(TimeSpan.FromHours(3));
        stats.RecordFizzle("Strength Other"); // hour 3

        IReadOnlyList<HourlyBucket> hours = stats.Snapshot().Hours;
        Assert.Equal(1, hours[^4].Fizzles); // hour 0, four back from the current (hour 3)
        Assert.Equal(1, hours[^1].Fizzles); // hour 3, the current one
        Assert.Equal(0, hours[^2].Fizzles); // hour 2, untouched
    }

    [Fact]
    public void WaitStatsAreNullUntilAtLeastOneSampleExists()
    {
        var stats = new SessionStats(new FakeClock());

        WaitStats wait = stats.Snapshot().Wait;

        Assert.Null(wait.MedianSeconds);
        Assert.Null(wait.LongestSeconds);
    }

    [Fact]
    public void MedianOfAnOddNumberOfSamplesIsTheMiddleOne()
    {
        var stats = new SessionStats(new FakeClock());
        foreach (double seconds in new[] { 5d, 1d, 3d })
            stats.RecordWaitSeconds(seconds);

        WaitStats wait = stats.Snapshot().Wait;

        Assert.Equal(3d, wait.MedianSeconds);
        Assert.Equal(5d, wait.LongestSeconds);
    }

    [Fact]
    public void MedianOfAnEvenNumberOfSamplesAveragesTheMiddleTwo()
    {
        var stats = new SessionStats(new FakeClock());
        foreach (double seconds in new[] { 1d, 2d, 3d, 4d })
            stats.RecordWaitSeconds(seconds);

        Assert.Equal(2.5d, stats.Snapshot().Wait.MedianSeconds);
    }

    [Fact]
    public void TheWaitSampleIsBoundedToTheLastFiveHundred()
    {
        var stats = new SessionStats(new FakeClock());
        for (int i = 1; i <= SessionStats.WaitSampleCapacity + 10; i++)
            stats.RecordWaitSeconds(i);

        // The bound evicts the oldest sample, not the largest.
        Assert.Equal((double)(SessionStats.WaitSampleCapacity + 10), stats.Snapshot().Wait.LongestSeconds);
    }

    [Fact]
    public void PlayersServedCountsDistinctRequestersWhoseRunLandedACast()
    {
        var stats = new SessionStats(new FakeClock());
        stats.RecordPlayerServed(1);
        stats.RecordPlayerServed(2);
        stats.RecordPlayerServed(1); // Archer asked again

        SessionStatsSnapshot snapshot = stats.Snapshot();
        Assert.Equal(2, snapshot.PlayersServed);
        Assert.Equal(1, snapshot.PlayersReturning); // only requester 1 asked more than once
    }
}
