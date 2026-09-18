using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Guard;

namespace SolrLabs.BuffBot.Tests;

public sealed class ShieldFinderTests
{
    private static PluginWorldObject Object(
        uint objectId, string name, PluginObjectClass objectClass, uint wielderObjectId) =>
        new(objectId, WeenieClassId: objectId, name, objectClass, ItemType: 0, ContainerObjectId: 0, wielderObjectId);

    [Fact]
    public void FindsNothingWhenTheRequesterHasNothingWielded()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer", PluginObjectClass.Player, wielderObjectId: 0)];

        bool found = ShieldFinder.TryFindWieldedShield(objects, wielderObjectId: 1, out _);

        Assert.False(found);
    }

    [Fact]
    public void FindsNothingWhenOnlyAWeaponIsWielded()
    {
        // A melee or missile weapon is wielded too (WielderObjectId set the same way a shield's
        // would be), but neither classifies as Armor, so neither is mistaken for one.
        IReadOnlyList<PluginWorldObject> objects =
        [
            Object(1, "Archer", PluginObjectClass.Player, wielderObjectId: 0),
            Object(2, "Main-hand Sword", PluginObjectClass.MeleeWeapon, wielderObjectId: 1),
        ];

        bool found = ShieldFinder.TryFindWieldedShield(objects, wielderObjectId: 1, out _);

        Assert.False(found);
    }

    [Fact]
    public void FindsTheRequestersOwnWieldedShield()
    {
        IReadOnlyList<PluginWorldObject> objects =
        [
            Object(1, "Archer", PluginObjectClass.Player, wielderObjectId: 0),
            Object(2, "Main-hand Sword", PluginObjectClass.MeleeWeapon, wielderObjectId: 1),
            Object(3, "Round Shield", PluginObjectClass.Armor, wielderObjectId: 1),
        ];

        bool found = ShieldFinder.TryFindWieldedShield(objects, wielderObjectId: 1, out PluginWorldObject shield);

        Assert.True(found);
        Assert.Equal(3u, shield.ObjectId);
        Assert.Equal("Round Shield", shield.Name);
    }

    [Fact]
    public void NeverReturnsAnotherPlayersShield()
    {
        IReadOnlyList<PluginWorldObject> objects =
        [
            Object(1, "Archer", PluginObjectClass.Player, wielderObjectId: 0),
            Object(2, "Someone Else", PluginObjectClass.Player, wielderObjectId: 0),
            Object(3, "Someone Else's Shield", PluginObjectClass.Armor, wielderObjectId: 2),
        ];

        bool found = ShieldFinder.TryFindWieldedShield(objects, wielderObjectId: 1, out _);

        Assert.False(found);
    }
}
