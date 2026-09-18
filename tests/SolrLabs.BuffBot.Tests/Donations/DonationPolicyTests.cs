using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Tests.Donations;

public sealed class DonationPolicyTests
{
    private static PluginInventoryItem Item(uint weenieClassId, string name, PluginObjectClass objectClass, int value = 0) =>
        new(
            ObjectId: weenieClassId, weenieClassId, name, ItemType: 0, ContainerObjectId: 0, WielderObjectId: 0,
            ValidLocations: 0, EquippedLocation: 0, Useability: 0, TargetType: 0, PublicFlags: 0,
            StackSize: 1, Structure: 0, MaximumStructure: 0, SpellId: 0, PetClass: 0,
            SummoningMastery: 0, ProcSpellId: 0, ProcSpellSelfTargeted: false, ProcSpellRate: 0,
            WeaponSkill: 0, DamageType: 0, Damage: 0, DamageVariance: 0, UseRequiresSkill: 0,
            UseRequiresSkillLevel: 0, UseRequiresSkillSpecialized: 0)
        { ObjectClass = objectClass, Value = value };

    [Fact]
    public void EveryCastingReagentIsWanted()
    {
        PluginInventoryItem scarab = Item(691u, "Lead Scarab", PluginObjectClass.SpellComponent);
        PluginInventoryItem pea = Item(90210u, "Prismatic Pea", PluginObjectClass.Misc);

        Assert.True(DonationPolicy.IsWanted(scarab, botHoldsSplittingTool: false));
        Assert.True(DonationPolicy.IsWanted(pea, botHoldsSplittingTool: false));
        Assert.True(DonationPolicy.IsWanted(scarab.WeenieClassId, scarab.Name, botHoldsSplittingTool: false));
        Assert.True(DonationPolicy.IsWanted(pea.WeenieClassId, pea.Name, botHoldsSplittingTool: false));
    }

    [Fact]
    public void ASplittingToolIsWantedOnlyWhileTheBotHoldsNone()
    {
        PluginInventoryItem tool = Item(8283u, "Splitting Tool", PluginObjectClass.Misc);

        Assert.True(DonationPolicy.IsWanted(tool, botHoldsSplittingTool: false));
        Assert.False(DonationPolicy.IsWanted(tool, botHoldsSplittingTool: true));
        Assert.True(DonationPolicy.IsWanted(8283u, "Splitting Tool", botHoldsSplittingTool: false));
        Assert.False(DonationPolicy.IsWanted(8283u, "Splitting Tool", botHoldsSplittingTool: true));
    }

    [Fact]
    public void ATradeNoteWorthAtLeastTenThousandIsWantedByKnownId()
    {
        PluginInventoryItem note = Item(2625u, "Trade Note (10,000)", PluginObjectClass.TradeNote);

        Assert.True(DonationPolicy.IsWanted(note, botHoldsSplittingTool: false));
        Assert.True(DonationPolicy.IsWanted(note.WeenieClassId, note.Name, botHoldsSplittingTool: false));
    }

    [Fact]
    public void ATradeNoteUnderTenThousandIsUnwanted()
    {
        PluginInventoryItem note = Item(2624u, "Trade Note (5,000)", PluginObjectClass.TradeNote);

        Assert.False(DonationPolicy.IsWanted(note, botHoldsSplittingTool: false));
        Assert.False(DonationPolicy.IsWanted(note.WeenieClassId, note.Name, botHoldsSplittingTool: false));
    }

    /// <summary>An unknown weenie id falls back to the parenthesised figure in the name.
    /// </summary>
    [Fact]
    public void AnUnknownTradeNoteIdParsesTheValueFromItsName()
    {
        PluginInventoryItem note = Item(99999u, "Trade Note (20,000)", PluginObjectClass.TradeNote);

        Assert.True(DonationPolicy.IsWanted(note, botHoldsSplittingTool: false));
        Assert.True(DonationPolicy.IsWanted(note.WeenieClassId, note.Name, botHoldsSplittingTool: false));
    }

    [Fact]
    public void TheWeenieNameOverloadNeverMistakesAnUnrelatedItemForATradeNote()
    {
        // No ObjectClass here (the overload has none to check), so the "Trade Note" name prefix is
        // the only gate against a coincidental "(...)" in an unrelated item's own name.
        Assert.False(DonationPolicy.IsWanted(12345u, "Bundle of Arrowheads (24)", botHoldsSplittingTool: false));
    }

    [Fact]
    public void AWandOrCoinIsNeverWanted()
    {
        PluginInventoryItem wand = Item(4001u, "Yellow Wand", PluginObjectClass.WandStaffOrb);
        PluginInventoryItem coin = Item(273u, "Pyreal", PluginObjectClass.Money, value: 50000);

        Assert.False(DonationPolicy.IsWanted(wand, botHoldsSplittingTool: false));
        Assert.False(DonationPolicy.IsWanted(coin, botHoldsSplittingTool: false));
    }

    [Fact]
    public void WantedItemsDescriptionListsTheSplittingToolOnlyWhileNoneIsHeld()
    {
        Assert.Equal(
            "casting reagents, Splitting Tools, or trade notes of 10,000+",
            DonationPolicy.WantedItemsDescription(botHoldsSplittingTool: false));
        Assert.Equal(
            "casting reagents or trade notes of 10,000+",
            DonationPolicy.WantedItemsDescription(botHoldsSplittingTool: true));
    }
}
