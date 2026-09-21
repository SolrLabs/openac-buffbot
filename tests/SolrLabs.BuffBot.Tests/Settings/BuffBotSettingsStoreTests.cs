using SolrLabs.BuffBot.Portals;
using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class BuffBotSettingsStoreTests
{
    [Fact]
    public void ACharacterNeverSeenBeforeLoadsTheDefault()
    {
        var store = new BuffBotSettingsStore(new FakeStorage());

        Assert.Equal(BuffBotSettings.Default, store.Load(1234));
    }

    [Fact]
    public void SavingPersistsAcrossANewInstanceOverTheSameStorage()
    {
        var storage = new FakeStorage();
        var saved = BuffBotSettings.Default with
        {
            SelfBuffUpkeep = false,
            RefusalRangeMeters = 50d,
            RepliesPerSenderPerMinute = 8,
            IntakePaused = true,
            TargetTier = 3,
            TierFallback = false,
            FizzlesBeforeSkip = 4,
            ComponentLowStock = 500,
            ManaBounceLowWaterFraction = 0.3,
            ManaBounceHighWaterFraction = 0.9,
            SplitPeas = false,
        };
        new BuffBotSettingsStore(storage).Save(1234, saved);

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);

        Assert.Equal(saved, loaded);
    }

    /// <summary>Flipped off explicitly, so a store that always returned the default would fail.
    /// </summary>
    [Fact]
    public void SplitPeasRoundTripsOffAndOn()
    {
        var storage = new FakeStorage();
        var store = new BuffBotSettingsStore(storage);
        Assert.True(BuffBotSettings.Default.SplitPeas);

        store.Save(1234, BuffBotSettings.Default with { SplitPeas = false });
        Assert.False(store.Load(1234).SplitPeas);

        store.Save(1234, BuffBotSettings.Default with { SplitPeas = true });
        Assert.True(store.Load(1234).SplitPeas);
    }

    [Fact]
    public void EachCharacterHasItsOwnSettings()
    {
        var storage = new FakeStorage();
        var store = new BuffBotSettingsStore(storage);
        store.Save(1111, BuffBotSettings.Default with { RepliesPerSenderPerMinute = 3 });

        Assert.Equal(3, store.Load(1111).RepliesPerSenderPerMinute);
        Assert.Equal(BuffBotSettings.Default.RepliesPerSenderPerMinute, store.Load(2222).RepliesPerSenderPerMinute);
    }

    [Fact]
    public void UnavailableStorageLoadsTheDefaultAndIgnoresWrites()
    {
        var storage = new FakeStorage { IsAvailable = false };
        var store = new BuffBotSettingsStore(storage);

        store.Save(1234, BuffBotSettings.Default with { RepliesPerSenderPerMinute = 3 });

        Assert.Equal(BuffBotSettings.Default, store.Load(1234));
    }

    [Fact]
    public void MalformedJsonLoadsTheDefaultRatherThanThrowing()
    {
        var storage = new FakeStorage();
        storage.WriteText("settings/1234", "not json at all {{{");

        Assert.Equal(BuffBotSettings.Default, new BuffBotSettingsStore(storage).Load(1234));
    }

    [Fact]
    public void AWrongTypedFieldFallsBackToItsDefaultRatherThanThrowing()
    {
        var storage = new FakeStorage();
        storage.WriteText("settings/1234", "{\"refusalRangeMeters\":\"not a number\",\"repliesPerSenderPerMinute\":8}");

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);

        Assert.Equal(BuffBotSettings.DefaultRefusalRangeMeters, loaded.RefusalRangeMeters);
        Assert.Equal(8, loaded.RepliesPerSenderPerMinute);
    }

    [Fact]
    public void AnOutOfRangeStoredValueClampsRatherThanThrowing()
    {
        var storage = new FakeStorage();
        storage.WriteText("settings/1234", "{\"refusalRangeMeters\":1000,\"fizzlesBeforeSkip\":0}");

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);

        Assert.Equal(75d, loaded.RefusalRangeMeters);
        Assert.Equal(1, loaded.FizzlesBeforeSkip);
    }

    [Fact]
    public void ANullTargetTierRoundTripsAsTopLearned()
    {
        var storage = new FakeStorage();
        new BuffBotSettingsStore(storage).Save(1234, BuffBotSettings.Default with { TargetTier = null });

        Assert.Null(new BuffBotSettingsStore(storage).Load(1234).TargetTier);
    }

    [Fact]
    public void AMissingComponentLowStockLoadsItsOwnDefaultRatherThanZero()
    {
        var storage = new FakeStorage();
        storage.WriteText("settings/1234", "{\"repliesPerSenderPerMinute\":8}");

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);

        Assert.Equal(BuffBotSettings.DefaultComponentLowStock, loaded.ComponentLowStock);
    }

    [Fact]
    public void ComponentLowStockRoundTripsAndClamps()
    {
        var storage = new FakeStorage();
        new BuffBotSettingsStore(storage).Save(1234, BuffBotSettings.Default with { ComponentLowStock = 40000 });

        Assert.Equal(BuffBotSettings.MaxComponentLowStock, new BuffBotSettingsStore(storage).Load(1234).ComponentLowStock);
    }

    [Fact]
    public void AMissingManaBounceFractionPairLoadsTheDefaultTwentyEightyRatherThanZero()
    {
        var storage = new FakeStorage();
        storage.WriteText("settings/1234", "{\"repliesPerSenderPerMinute\":8}");

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);

        Assert.Equal(BuffBotSettings.DefaultManaBounceLowWaterFraction, loaded.ManaBounceLowWaterFraction);
        Assert.Equal(BuffBotSettings.DefaultManaBounceHighWaterFraction, loaded.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void ManaBounceFractionsRoundTripAndClampTheirGap()
    {
        var storage = new FakeStorage();
        new BuffBotSettingsStore(storage).Save(
            1234,
            BuffBotSettings.Default with { ManaBounceLowWaterFraction = 0.3, ManaBounceHighWaterFraction = 0.9 });

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);
        Assert.Equal(0.3, loaded.ManaBounceLowWaterFraction);
        Assert.Equal(0.9, loaded.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void AStoredManaBounceGapNarrowerThanTenPointsClampsOnLoad()
    {
        var storage = new FakeStorage();
        storage.WriteText(
            "settings/1234",
            "{\"manaBounceLowWaterFraction\":0.55,\"manaBounceHighWaterFraction\":0.58}");

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);

        Assert.Equal(0.55, loaded.ManaBounceLowWaterFraction);
        Assert.Equal(0.65, loaded.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void BothPortalTiesRoundTrip()
    {
        var storage = new FakeStorage();
        var saved = BuffBotSettings.Default with
        {
            PrimaryPortal = new PortalTie("Temple of Enlightenment", PortalDirection.Right),
            SecondaryPortal = new PortalTie("Holtburg", PortalDirection.Behind),
        };
        new BuffBotSettingsStore(storage).Save(1234, saved);

        BuffBotSettings loaded = new BuffBotSettingsStore(storage).Load(1234);

        Assert.Equal(saved.PrimaryPortal, loaded.PrimaryPortal);
        Assert.Equal(saved.SecondaryPortal, loaded.SecondaryPortal);
    }

    [Fact]
    public void ACharacterNeverSeenBeforeLoadsEmptyPortalTies()
    {
        var store = new BuffBotSettingsStore(new FakeStorage());

        BuffBotSettings loaded = store.Load(1234);

        Assert.Equal(PortalTie.Empty, loaded.PrimaryPortal);
        Assert.Equal(PortalTie.Empty, loaded.SecondaryPortal);
    }
}
