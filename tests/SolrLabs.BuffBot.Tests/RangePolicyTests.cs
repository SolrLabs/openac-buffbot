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
}
