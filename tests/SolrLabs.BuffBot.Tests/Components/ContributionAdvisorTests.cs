using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary>The join between an all-tiers <see cref="ComponentReport"/> and raw inventory
/// stock for the Splitting Tool, which is never a spell's own reagent.</summary>
public sealed class ContributionAdvisorTests
{
    private const int LowStock = 25;

    private static ComponentUsage Row(uint weenieClassId, string name, int stock) =>
        new(weenieClassId, name, stock, UsedBy: 1);

    private static PluginInventoryItem Stack(uint weenieClassId, int stackSize) =>
        new(
            ObjectId: 1,
            WeenieClassId: weenieClassId,
            Name: "item",
            ItemType: 0,
            ContainerObjectId: 0,
            WielderObjectId: 0,
            ValidLocations: 0,
            EquippedLocation: 0,
            Useability: 0,
            TargetType: 0,
            PublicFlags: 0,
            StackSize: stackSize,
            Structure: 1,
            MaximumStructure: 1,
            SpellId: 0,
            PetClass: 0,
            SummoningMastery: 0,
            ProcSpellId: 0,
            ProcSpellSelfTargeted: false,
            ProcSpellRate: 0,
            WeaponSkill: 0,
            DamageType: 0,
            Damage: 0,
            DamageVariance: 0,
            UseRequiresSkill: 0,
            UseRequiresSkillLevel: 0,
            UseRequiresSkillSpecialized: 0);

    [Fact]
    public void AScarabBelowThresholdIsLowAndNamesItsMatchingPea()
    {
        var report = new ComponentReport(true, [Row(690u, "Pyreal Scarabs", 4)]); // Pyreal Scarab
        ContributionSummary summary = ContributionAdvisor.Build(
            report, ownedItems: [], inventoryReadable: true, LowStock);

        ContributionNeed need = Assert.Single(summary.LowReagents);
        Assert.Equal("Pyreal Scarabs", need.Name);
        Assert.Equal(4, need.Stock);
        Assert.Equal("Pyreal Peas", need.PeaAlternative);
    }

    [Fact]
    public void AScarabWithNoPeaIsLowWithoutOne()
    {
        var report = new ComponentReport(true, [Row(7299u, "Diamond Scarabs", 0)]); // Diamond Scarab
        ContributionSummary summary = ContributionAdvisor.Build(
            report, ownedItems: [], inventoryReadable: true, LowStock);

        ContributionNeed need = Assert.Single(summary.LowReagents);
        Assert.Null(need.PeaAlternative);
    }

    [Fact]
    public void AScarabAtOrAboveThresholdIsNotReported()
    {
        var report = new ComponentReport(true, [Row(690u, "Pyreal Scarabs", 25)]);
        ContributionSummary summary = ContributionAdvisor.Build(
            report, ownedItems: [], inventoryReadable: true, LowStock);

        Assert.Empty(summary.LowReagents);
    }

    /// <summary>A scarab colour no learned spell needs never appears, even at zero stock — "what
    /// she actually needs", not every colour that exists.</summary>
    [Fact]
    public void AScarabColourNotAmongTheReportsRowsIsNeverNamed()
    {
        var report = new ComponentReport(true, []); // nothing needed at all
        ContributionSummary summary = ContributionAdvisor.Build(
            report, ownedItems: [], inventoryReadable: true, LowStock);

        Assert.Empty(summary.LowReagents);
    }

    [Fact]
    public void AHeldButUnneededLowRowNeverAppearsInTheReply()
    {
        var report = new ComponentReport(true, [new ComponentUsage(690u, "Pyreal Scarabs", 4, UsedBy: 0)]);
        ContributionSummary summary = ContributionAdvisor.Build(
            report, ownedItems: [], inventoryReadable: true, LowStock);

        Assert.Empty(summary.LowReagents);
    }

    [Fact]
    public void ASplittingToolIsAskedForOnlyWhenNoneIsHeld()
    {
        var report = new ComponentReport(true, []);

        ContributionSummary none = ContributionAdvisor.Build(
            report, ownedItems: [], inventoryReadable: true, LowStock);
        ContributionSummary held = ContributionAdvisor.Build(
            report, ownedItems: [Stack(8283u, 1)], inventoryReadable: true, LowStock); // Splitting Tool

        Assert.True(none.NeedsSplittingTool);
        Assert.False(held.NeedsSplittingTool);
    }

    [Fact]
    public void UnavailableWhenTheItemSurfaceCannotBeRead()
    {
        var report = new ComponentReport(true, [Row(690u, "Pyreal Scarabs", 0)]);

        ContributionSummary summary = ContributionAdvisor.Build(
            report, ownedItems: [], inventoryReadable: false, LowStock);

        Assert.Equal(ContributionSummary.Unavailable, summary);
    }

    /// <summary>Headless never binds the magic catalog, but inventory still reads
    /// fine, so the Splitting Tool half must still answer.</summary>
    [Fact]
    public void CatalogUnavailableStillAnswersTheSplittingToolFromRawInventory()
    {
        ContributionSummary summary = ContributionAdvisor.Build(
            ComponentReport.CatalogUnavailable, ownedItems: [Stack(8283u, 1)], inventoryReadable: true, LowStock);

        Assert.False(summary.ReagentCatalogAvailable);
        Assert.True(summary.InventoryReadable);
        Assert.False(summary.NeedsSplittingTool);
        Assert.Empty(summary.LowReagents);
    }
}
