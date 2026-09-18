using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary><see cref="ComponentSampleCadence"/> refreshes every interval of ticks,
/// immediately on the first call, and immediately again after a run closes.</summary>
public sealed class ComponentSampleCadenceTests
{
    [Fact]
    public void TheFirstAdvanceIsAlwaysDueRegardlessOfDelta()
    {
        var cadence = new ComponentSampleCadence(intervalSeconds: 10d);

        Assert.True(cadence.Advance(0.001d));
    }

    [Fact]
    public void NotDueAgainUntilTheIntervalElapses()
    {
        var cadence = new ComponentSampleCadence(intervalSeconds: 10d);
        Assert.True(cadence.Advance(0d)); // consume the initial due state.

        Assert.False(cadence.Advance(5d));
        Assert.False(cadence.Advance(4.9d));
        Assert.True(cadence.Advance(0.2d)); // 5 + 4.9 + 0.2 = 10.1, at the interval.
    }

    [Fact]
    public void ForceDueMakesTheNextAdvanceTrueRegardlessOfElapsedTime()
    {
        var cadence = new ComponentSampleCadence(intervalSeconds: 10d);
        Assert.True(cadence.Advance(0d));
        Assert.False(cadence.Advance(1d)); // well short of the interval.

        cadence.ForceDue();

        Assert.True(cadence.Advance(0.001d));
    }

    [Fact]
    public void ADueSampleResetsTheIntervalForTheNextOne()
    {
        var cadence = new ComponentSampleCadence(intervalSeconds: 10d);
        Assert.True(cadence.Advance(15d)); // due immediately (first call), interval resets.

        Assert.False(cadence.Advance(9d)); // only 9 s since the reset -- not due yet.
        Assert.True(cadence.Advance(1d)); // now at 10 s since the reset.
    }
}
