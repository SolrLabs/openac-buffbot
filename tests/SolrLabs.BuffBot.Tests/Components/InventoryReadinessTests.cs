using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary>Host-free coverage of <see cref="InventoryReadiness"/> — the arming gate that keeps
/// <see cref="PeaTopUpLoop"/> from acting before a full inventory snapshot has arrived.</summary>
public sealed class InventoryReadinessTests
{
    private static PluginInventoryItem Item(uint objectId, int stackSize) =>
        new(
            objectId, WeenieClassId: objectId, "Item", ItemType: 0, ContainerObjectId: 1, WielderObjectId: 0,
            ValidLocations: 0, EquippedLocation: 0, Useability: 0, TargetType: 0, PublicFlags: 0,
            StackSize: stackSize, Structure: 0, MaximumStructure: 0, SpellId: 0, PetClass: 0,
            SummoningMastery: 0, ProcSpellId: 0, ProcSpellSelfTargeted: false, ProcSpellRate: 0,
            WeaponSkill: 0, DamageType: 0, Damage: 0, DamageVariance: 0, UseRequiresSkill: 0,
            UseRequiresSkillLevel: 0, UseRequiresSkillSpecialized: 0);

    private const uint CharacterObjectId = 999u;
    private const double Interval = InventoryReadiness.StableCaptureIntervalSeconds;

    [Fact]
    public void NotReadyOnOneCapture()
    {
        var readiness = new InventoryReadiness();
        IReadOnlyList<PluginInventoryItem> sample = [Item(1, 5)];

        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);

        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void NotReadyOnTwoDifferentCaptures()
    {
        var readiness = new InventoryReadiness();
        IReadOnlyList<PluginInventoryItem> first = [Item(1, 5)];
        IReadOnlyList<PluginInventoryItem> second = [Item(1, 6)]; // stack size changed

        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => first);
        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => second);

        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void ReadyOnTwoIdenticalCaptures()
    {
        var readiness = new InventoryReadiness();
        IReadOnlyList<PluginInventoryItem> sample = [Item(1, 5), Item(2, 10)];

        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);
        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);

        Assert.True(readiness.IsReady);
    }

    [Fact]
    public void NeverReadyOnAnEmptyCapture()
    {
        var readiness = new InventoryReadiness();
        IReadOnlyList<PluginInventoryItem> empty = [];

        for (int i = 0; i < 5; i++)
            readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => empty);

        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void ResetsOnLeavingTheWorld()
    {
        var readiness = new InventoryReadiness();
        IReadOnlyList<PluginInventoryItem> sample = [Item(1, 5)];

        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);
        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);
        Assert.True(readiness.IsReady);

        readiness.Tick(1, isInWorld: false, CharacterObjectId, () => sample);
        Assert.False(readiness.IsReady);

        // Back in world: the wait starts over, not resumed from where it left off.
        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);
        Assert.False(readiness.IsReady);
        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);
        Assert.True(readiness.IsReady);
    }

    [Fact]
    public void ResetsOnACharacterChangeWithoutEverLeavingWorld()
    {
        var readiness = new InventoryReadiness();
        IReadOnlyList<PluginInventoryItem> sample = [Item(1, 5)];

        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);
        readiness.Tick(Interval, isInWorld: true, CharacterObjectId, () => sample);
        Assert.True(readiness.IsReady);

        readiness.Tick(Interval, isInWorld: true, CharacterObjectId + 1, () => sample);
        Assert.False(readiness.IsReady);
    }

    [Fact]
    public void NeverSamplesMoreOftenThanTheStableInterval()
    {
        var readiness = new InventoryReadiness();
        int captureCalls = 0;
        IReadOnlyList<PluginInventoryItem> sample = [Item(1, 5)];

        // 9 ticks of Interval/5 each: just short of a second interval's worth of elapsed time
        // since the one capture the first interval already triggered.
        for (int i = 0; i < 9; i++)
            readiness.Tick(Interval / 5, isInWorld: true, CharacterObjectId, () =>
            {
                captureCalls++;
                return sample;
            });

        Assert.False(readiness.IsReady);
        Assert.Equal(1, captureCalls);
    }
}
