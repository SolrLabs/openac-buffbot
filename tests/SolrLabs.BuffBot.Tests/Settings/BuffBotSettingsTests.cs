using SolrLabs.BuffBot.Portals;
using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class BuffBotSettingsTests
{
    [Fact]
    public void DefaultMatchesTodaysHardcodedBehaviour()
    {
        BuffBotSettings settings = BuffBotSettings.Default;

        Assert.True(settings.SelfBuffUpkeep);
        Assert.Equal(67.5, settings.RefusalRangeMeters);
        Assert.Equal(12, settings.RepliesPerSenderPerMinute);
        Assert.False(settings.IntakePaused);
        Assert.Null(settings.TargetTier);
        Assert.True(settings.TierFallback);
        Assert.Equal(6, settings.FizzlesBeforeSkip);
        Assert.Equal(25, settings.ComponentLowStock);
        Assert.Equal(0.20, settings.ManaBounceLowWaterFraction);
        Assert.Equal(0.80, settings.ManaBounceHighWaterFraction);
        Assert.Equal(PortalTie.Empty, settings.PrimaryPortal);
        Assert.Equal(PortalTie.Empty, settings.SecondaryPortal);
    }

    [Theory]
    [InlineData(0d, 40d)]
    [InlineData(39.9, 40d)]
    [InlineData(40d, 40d)]
    [InlineData(75d, 75d)]
    [InlineData(100d, 75d)]
    public void ClampedBoundsTheRefusalRange(double stored, double expected) =>
        Assert.Equal(expected, (BuffBotSettings.Default with { RefusalRangeMeters = stored }).Clamped().RefusalRangeMeters);

    [Theory]
    [InlineData(0d, 5d)]
    [InlineData(4.9, 5d)]
    [InlineData(5d, 5d)]
    [InlineData(10d, 10d)]
    [InlineData(30d, 10d)]
    public void ClampedBoundsQueuePauseSeconds(double stored, double expected) =>
        Assert.Equal(expected, (BuffBotSettings.Default with { QueuePauseSeconds = stored }).Clamped().QueuePauseSeconds);

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    [InlineData(20, 20)]
    [InlineData(999, 20)]
    public void ClampedBoundsRepliesPerSenderPerMinute(int stored, int expected) =>
        Assert.Equal(
            expected,
            (BuffBotSettings.Default with { RepliesPerSenderPerMinute = stored }).Clamped().RepliesPerSenderPerMinute);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(8, 8)]
    [InlineData(50, 8)]
    public void ClampedBoundsTargetTierWhenSet(int stored, int expected) =>
        Assert.Equal(
            expected, (BuffBotSettings.Default with { TargetTier = stored }).Clamped().TargetTier);

    [Fact]
    public void ClampedLeavesANullTargetTierAsTopLearned() =>
        Assert.Null(BuffBotSettings.Default.Clamped().TargetTier);

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(20, 20)]
    [InlineData(500, 20)]
    public void ClampedBoundsFizzlesBeforeSkip(int stored, int expected) =>
        Assert.Equal(
            expected, (BuffBotSettings.Default with { FizzlesBeforeSkip = stored }).Clamped().FizzlesBeforeSkip);

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(25, 25)]
    [InlineData(10000, 10000)]
    [InlineData(50000, 10000)]
    public void ClampedBoundsComponentLowStock(int stored, int expected) =>
        Assert.Equal(
            expected, (BuffBotSettings.Default with { ComponentLowStock = stored }).Clamped().ComponentLowStock);

    [Fact]
    public void ClampedNeverThrowsOnWildlyOutOfRangeValues()
    {
        var garbage = new BuffBotSettings(
            SelfBuffUpkeep: false,
            RefusalRangeMeters: double.MaxValue,
            RepliesPerSenderPerMinute: int.MinValue,
            IntakePaused: true,
            TargetTier: int.MaxValue,
            TierFallback: false,
            FizzlesBeforeSkip: int.MinValue,
            PrimaryPortal: PortalTie.Empty,
            SecondaryPortal: PortalTie.Empty,
            ComponentLowStock: int.MinValue,
            ManaBounceLowWaterFraction: double.MaxValue,
            ManaBounceHighWaterFraction: double.MinValue);

        BuffBotSettings clamped = garbage.Clamped();

        Assert.Equal(75d, clamped.RefusalRangeMeters);
        Assert.Equal(2, clamped.RepliesPerSenderPerMinute);
        Assert.Equal(8, clamped.TargetTier);
        Assert.Equal(1, clamped.FizzlesBeforeSkip);
        Assert.Equal(0, clamped.ComponentLowStock);
        Assert.Equal(BuffBotSettings.MaxManaBounceLowWaterFraction, clamped.ManaBounceLowWaterFraction);
        // The gap rule, not the high handle's own floor, is what pushes this to 70%.
        Assert.Equal(
            BuffBotSettings.MaxManaBounceLowWaterFraction + BuffBotSettings.MinManaBounceGapFraction,
            clamped.ManaBounceHighWaterFraction);
    }

    [Theory]
    [InlineData(0d, 0.05)]
    [InlineData(0.04, 0.05)]
    [InlineData(0.05, 0.05)]
    [InlineData(0.60, 0.60)]
    [InlineData(1d, 0.60)]
    public void ClampedBoundsManaBounceLowWaterFraction(double stored, double expected) =>
        Assert.Equal(
            expected,
            (BuffBotSettings.Default with { ManaBounceLowWaterFraction = stored }).Clamped().ManaBounceLowWaterFraction);

    [Theory]
    [InlineData(0d, 0.40)]
    [InlineData(0.39, 0.40)]
    [InlineData(0.40, 0.40)]
    [InlineData(1d, 1d)]
    [InlineData(2d, 1d)]
    public void ClampedBoundsManaBounceHighWaterFraction(double stored, double expected) =>
        Assert.Equal(
            expected,
            (BuffBotSettings.Default with { ManaBounceHighWaterFraction = stored }).Clamped().ManaBounceHighWaterFraction);

    [Fact]
    public void ClampedEnforcesATenPointGapWhenTheStoredHandlesAreCloserThanThat()
    {
        // Both individually legal (55% and 60% are each inside their own range), but only 5
        // points apart -- the gap rule, not either handle's own bound, is what has to widen this.
        BuffBotSettings clamped = (BuffBotSettings.Default with
        {
            ManaBounceLowWaterFraction = 0.55,
            ManaBounceHighWaterFraction = 0.60,
        }).Clamped();

        Assert.Equal(0.55, clamped.ManaBounceLowWaterFraction);
        Assert.Equal(0.65, clamped.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void ClampedLeavesAnAlreadyWideEnoughGapAlone()
    {
        BuffBotSettings clamped = (BuffBotSettings.Default with
        {
            ManaBounceLowWaterFraction = 0.20,
            ManaBounceHighWaterFraction = 0.80,
        }).Clamped();

        Assert.Equal(0.20, clamped.ManaBounceLowWaterFraction);
        Assert.Equal(0.80, clamped.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void WithPatchChangesOnlyThePresentFields()
    {
        BuffBotSettings updated = BuffBotSettings.Default.WithPatch(
            selfBuffUpkeep: false,
            refusalRangeMeters: null,
            repliesPerSenderPerMinute: null,
            intakePaused: null,
            hasTargetTier: false,
            targetTier: null,
            tierFallback: null,
            fizzlesBeforeSkip: null);

        Assert.False(updated.SelfBuffUpkeep);
        Assert.Equal(BuffBotSettings.Default.RefusalRangeMeters, updated.RefusalRangeMeters);
        Assert.Equal(BuffBotSettings.Default.RepliesPerSenderPerMinute, updated.RepliesPerSenderPerMinute);
        Assert.Equal(BuffBotSettings.Default.TierFallback, updated.TierFallback);
    }

    [Fact]
    public void WithPatchSettingTargetTierToNullIsDifferentFromLeavingItAlone()
    {
        BuffBotSettings withTier = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: true, targetTier: 4, null, null);
        Assert.Equal(4, withTier.TargetTier);

        BuffBotSettings backToTopLearned = withTier.WithPatch(
            null, null, null, null, hasTargetTier: true, targetTier: null, null, null);
        Assert.Null(backToTopLearned.TargetTier);

        BuffBotSettings leftAlone = withTier.WithPatch(
            null, null, null, null, hasTargetTier: false, targetTier: null, null, null);
        Assert.Equal(4, leftAlone.TargetTier);
    }

    [Fact]
    public void WithPatchClampsTheResult()
    {
        BuffBotSettings updated = BuffBotSettings.Default.WithPatch(
            null, refusalRangeMeters: 1000d, null, null, hasTargetTier: false, null, null, null);

        Assert.Equal(75d, updated.RefusalRangeMeters);
    }

    [Fact]
    public void WithPatchLeavesComponentLowStockAloneWhenAbsentAndChangesItWhenPresent()
    {
        BuffBotSettings left = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null, componentLowStock: null);
        Assert.Equal(BuffBotSettings.DefaultComponentLowStock, left.ComponentLowStock);

        BuffBotSettings changed = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null, componentLowStock: 500);
        Assert.Equal(500, changed.ComponentLowStock);
    }

    [Fact]
    public void WithPatchLeavesManaBounceFractionsAloneWhenAbsentAndChangesThemWhenPresent()
    {
        BuffBotSettings left = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null,
            componentLowStock: null, manaBounceLowWaterFraction: null, manaBounceHighWaterFraction: null);
        Assert.Equal(BuffBotSettings.DefaultManaBounceLowWaterFraction, left.ManaBounceLowWaterFraction);
        Assert.Equal(BuffBotSettings.DefaultManaBounceHighWaterFraction, left.ManaBounceHighWaterFraction);

        BuffBotSettings changed = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null,
            componentLowStock: null, manaBounceLowWaterFraction: 0.3, manaBounceHighWaterFraction: 0.9);
        Assert.Equal(0.3, changed.ManaBounceLowWaterFraction);
        Assert.Equal(0.9, changed.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void WithPatchClampsAManaBounceFractionThatWouldCloseTheGapTooFar()
    {
        BuffBotSettings changed = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null,
            componentLowStock: null, manaBounceLowWaterFraction: 0.5, manaBounceHighWaterFraction: 0.52);

        Assert.Equal(0.5, changed.ManaBounceLowWaterFraction);
        Assert.Equal(0.6, changed.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void SplitPeasDefaultsOnAndWithPatchLeavesItAloneWhenAbsent()
    {
        Assert.True(BuffBotSettings.Default.SplitPeas);

        BuffBotSettings left = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null,
            componentLowStock: null, manaBounceLowWaterFraction: null, manaBounceHighWaterFraction: null,
            splitPeas: null);
        Assert.True(left.SplitPeas);

        BuffBotSettings changed = BuffBotSettings.Default.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null,
            componentLowStock: null, manaBounceLowWaterFraction: null, manaBounceHighWaterFraction: null,
            splitPeas: false);
        Assert.False(changed.SplitPeas);
    }

    [Fact]
    public void WithPatchLeavesEachTieAloneWhenItsPatchIsNullAndReplacesItWhenPresent()
    {
        var tied = BuffBotSettings.Default with
        {
            PrimaryPortal = new PortalTie("Temple of Enlightenment", PortalDirection.Right),
            SecondaryPortal = new PortalTie("Holtburg", PortalDirection.Left),
        };

        BuffBotSettings left = tied.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null,
            primaryPortal: null, secondaryPortal: null);
        Assert.Equal(tied.PrimaryPortal, left.PrimaryPortal);
        Assert.Equal(tied.SecondaryPortal, left.SecondaryPortal);

        BuffBotSettings changed = tied.WithPatch(
            null, null, null, null, hasTargetTier: false, null, null, null,
            primaryPortal: new PortalTie("Shoushi", PortalDirection.Behind), secondaryPortal: null);
        Assert.Equal(new PortalTie("Shoushi", PortalDirection.Behind), changed.PrimaryPortal);
        Assert.Equal(tied.SecondaryPortal, changed.SecondaryPortal);
    }
}
