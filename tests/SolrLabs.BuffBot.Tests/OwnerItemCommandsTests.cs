using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Guard;

namespace SolrLabs.BuffBot.Tests;

public sealed class OwnerItemCommandsTests
{
    private static PluginWorldObject Object(
        uint objectId, string name, uint wielderObjectId = 0) =>
        new(objectId, WeenieClassId: objectId, name, PluginObjectClass.Misc, ItemType: 0, ContainerObjectId: 0, wielderObjectId);

    private static PluginInventoryItem Item(uint objectId, string name, int stackSize = 1, uint containerObjectId = 0) =>
        new(
            objectId, WeenieClassId: objectId, name, ItemType: 0, containerObjectId, WielderObjectId: 0,
            ValidLocations: 0, EquippedLocation: 0, Useability: 0, TargetType: 0, PublicFlags: 0,
            StackSize: stackSize, Structure: 0, MaximumStructure: 0, SpellId: 0, PetClass: 0,
            SummoningMastery: 0, ProcSpellId: 0, ProcSpellSelfTargeted: false, ProcSpellRate: 0,
            WeaponSkill: 0, DamageType: 0, Damage: 0, DamageVariance: 0, UseRequiresSkill: 0,
            UseRequiresSkillLevel: 0, UseRequiresSkillSpecialized: 0);

    [Theory]
    [InlineData("0x1A", 0x1Au)]
    [InlineData("0x1a", 0x1au)]
    [InlineData("0X1A", 0x1Au)]
    [InlineData("305419896", 305419896u)]
    [InlineData(" 0x1A ", 0x1Au)]
    public void TryParseObjectIdAcceptsHexAndDecimal(string token, uint expected)
    {
        bool parsed = OwnerItemCommands.TryParseObjectId(token, out uint objectId);

        Assert.True(parsed);
        Assert.Equal(expected, objectId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("0xZZZZ")]
    public void TryParseObjectIdRejectsGarbage(string token)
    {
        Assert.False(OwnerItemCommands.TryParseObjectId(token, out _));
    }

    [Fact]
    public void ResolveTargetTakesAnIdTokenDirectlyWithoutSearching()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer")];

        OwnerItemCommands.TargetResolution resolved = OwnerItemCommands.ResolveTarget(objects, "0x2A");

        Assert.Equal(OwnerItemCommands.TargetLookup.Ok, resolved.Result);
        Assert.Equal(0x2Au, resolved.ObjectId);
    }

    [Fact]
    public void ResolveTargetFindsAUniqueNameCaseInsensitively()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer"), Object(2, "Dereth Dweller")];

        OwnerItemCommands.TargetResolution resolved = OwnerItemCommands.ResolveTarget(objects, "dereth dweller");

        Assert.Equal(OwnerItemCommands.TargetLookup.Ok, resolved.Result);
        Assert.Equal(2u, resolved.ObjectId);
    }

    [Fact]
    public void ResolveTargetReportsNotFoundForAnUnknownName()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer")];

        OwnerItemCommands.TargetResolution resolved = OwnerItemCommands.ResolveTarget(objects, "Nobody");

        Assert.Equal(OwnerItemCommands.TargetLookup.NotFound, resolved.Result);
    }

    [Fact]
    public void ResolveTargetReportsAmbiguityInsteadOfPickingTheFirstMatch()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer"), Object(2, "Archer")];

        OwnerItemCommands.TargetResolution resolved = OwnerItemCommands.ResolveTarget(objects, "Archer");

        Assert.Equal(OwnerItemCommands.TargetLookup.Ambiguous, resolved.Result);
        Assert.Equal(2, resolved.MatchCount);
    }

    [Fact]
    public void FilterOwnedItemsWithAnEmptyFilterReturnsEverything()
    {
        IReadOnlyList<PluginInventoryItem> items = [Item(1, "Potion"), Item(2, "Wand")];

        IReadOnlyList<PluginInventoryItem> filtered = OwnerItemCommands.FilterOwnedItems(items, string.Empty);

        Assert.Same(items, filtered);
    }

    [Fact]
    public void FilterOwnedItemsMatchesByNameSubstringCaseInsensitively()
    {
        IReadOnlyList<PluginInventoryItem> items = [Item(1, "Healing Potion"), Item(2, "Wand of Nem")];

        IReadOnlyList<PluginInventoryItem> filtered = OwnerItemCommands.FilterOwnedItems(items, "potion");

        PluginInventoryItem only = Assert.Single(filtered);
        Assert.Equal(1u, only.ObjectId);
    }

    private static PluginChatMessage Message(ulong sequence) =>
        new(sequence, SenderObjectId: 1, Kind: 3, Sender: "Archer", Text: $"message {sequence}", ChannelName: "Tell");

    [Fact]
    public void TakeLastMessagesReturnsEverythingWhenCountExceedsTheBatch()
    {
        IReadOnlyList<PluginChatMessage> messages = [Message(1), Message(2)];

        IReadOnlyList<PluginChatMessage> tail = OwnerItemCommands.TakeLastMessages(messages, 10);

        Assert.Same(messages, tail);
    }

    [Fact]
    public void TakeLastMessagesReturnsTheTrailingNInOrder()
    {
        IReadOnlyList<PluginChatMessage> messages = [Message(1), Message(2), Message(3)];

        IReadOnlyList<PluginChatMessage> tail = OwnerItemCommands.TakeLastMessages(messages, 2);

        Assert.Equal(2, tail.Count);
        Assert.Equal(2ul, tail[0].Sequence);
        Assert.Equal(3ul, tail[1].Sequence);
    }

    [Fact]
    public void TakeLastMessagesWithZeroOrNegativeCountReturnsNothing()
    {
        IReadOnlyList<PluginChatMessage> messages = [Message(1)];

        Assert.Empty(OwnerItemCommands.TakeLastMessages(messages, 0));
        Assert.Empty(OwnerItemCommands.TakeLastMessages(messages, -5));
    }
}
