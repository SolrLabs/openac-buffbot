using SolrLabs.BuffBot.Policy;

namespace SolrLabs.BuffBot.Tests;

public sealed class RangePolicyTests
{
    [Fact]
    public void AllowsExactlyNinetyPercentOfCastRange()
    {
        double boundary = RangePolicy.MaxCastRangeMeters * 0.9;

        Assert.True(RangePolicy.IsInRange(boundary));
    }

    [Fact]
    public void RefusesJustBeyondNinetyPercentOfCastRange()
    {
        double justOver = RangePolicy.MaxCastRangeMeters * 0.9 + 0.01;

        Assert.False(RangePolicy.IsInRange(justOver));
    }

    [Fact]
    public void AllowsWellWithinRange() => Assert.True(RangePolicy.IsInRange(1d));

    [Fact]
    public void RefusesWellBeyondRange() =>
        Assert.False(RangePolicy.IsInRange(RangePolicy.MaxCastRangeMeters * 2));

    // ── a settings-driven threshold ─────────────────────────────────────────────────────

    [Fact]
    public void AllowsExactlyTheGivenThreshold() =>
        Assert.True(RangePolicy.IsInRange(40d, refusalRangeMeters: 40d));

    [Fact]
    public void RefusesJustBeyondTheGivenThreshold() =>
        Assert.False(RangePolicy.IsInRange(40.01, refusalRangeMeters: 40d));

    [Fact]
    public void ANarrowerThresholdRefusesADistanceTheDefaultWouldAllow()
    {
        double distance = 50d;

        Assert.True(RangePolicy.IsInRange(distance)); // default 67.5 m allows it
        Assert.False(RangePolicy.IsInRange(distance, refusalRangeMeters: 40d));
    }
}
