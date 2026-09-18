using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Guard;

namespace SolrLabs.BuffBot.Tests;

public sealed class PlayerItemsLookupTests
{
    private static PluginWorldObject Object(
        uint objectId, string name, PluginObjectClass objectClass = PluginObjectClass.Misc,
        uint wielderObjectId = 0) =>
        new(objectId, WeenieClassId: objectId, name, objectClass, ItemType: 0, ContainerObjectId: 0, wielderObjectId);

    [Fact]
    public void FindsAPlayerByNameCaseInsensitively()
    {
        IReadOnlyList<PluginWorldObject> objects =
        [
            Object(1, "Archer", PluginObjectClass.Player),
            Object(2, "Dereth Dweller", PluginObjectClass.Player),
        ];

        bool found = PlayerItemsLookup.TryFindByName(objects, "dereth dweller", out PluginWorldObject player);

        Assert.True(found);
        Assert.Equal(2u, player.ObjectId);
    }

    [Fact]
    public void ReportsNoMatchForAnUnknownName()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer", PluginObjectClass.Player)];

        bool found = PlayerItemsLookup.TryFindByName(objects, "Nobody", out _);

        Assert.False(found);
    }

    [Fact]
    public void WieldedByFiltersOnTheWielderObjectIdOnly()
    {
        IReadOnlyList<PluginWorldObject> objects =
        [
            Object(1, "Archer", PluginObjectClass.Player),
            Object(2, "Main-hand Sword", PluginObjectClass.MeleeWeapon, wielderObjectId: 1),
            Object(3, "Shield", PluginObjectClass.Armor, wielderObjectId: 1),
            Object(4, "Someone Else's Bow", PluginObjectClass.MissileWeapon, wielderObjectId: 99),
        ];

        IReadOnlyList<PluginWorldObject> wielded = PlayerItemsLookup.WieldedBy(objects, wielderObjectId: 1);

        Assert.Equal(2, wielded.Count);
        Assert.Contains(wielded, item => item.ObjectId == 2u);
        Assert.Contains(wielded, item => item.ObjectId == 3u);
    }

    [Fact]
    public void WieldedByIsEmptyWhenNothingIsWielded()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer", PluginObjectClass.Player)];

        IReadOnlyList<PluginWorldObject> wielded = PlayerItemsLookup.WieldedBy(objects, wielderObjectId: 1);

        Assert.Empty(wielded);
    }
}
