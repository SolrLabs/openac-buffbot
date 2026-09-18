using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Tests.Donations;

/// <summary>Host-free coverage of resolving a direct-give line's parsed giver name against
/// captured objects — exact, case-insensitive, and <c>+</c>-prefix aware.</summary>
public sealed class GiverResolverTests
{
    private static PluginWorldObject Object(uint objectId, string name) =>
        new(objectId, WeenieClassId: objectId, name, PluginObjectClass.Player, ItemType: 0, ContainerObjectId: 0, WielderObjectId: 0);

    [Fact]
    public void ResolvesAnExactNameMatch()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "probe")];

        GiverResolver.GiverResolution resolved = GiverResolver.Resolve(objects, "probe");

        Assert.Equal(GiverResolver.GiverLookup.Found, resolved.Result);
        Assert.Equal(1u, resolved.ObjectId);
    }

    [Fact]
    public void ResolvesWhenOnlyTheCapturedObjectCarriesThePlusPrefix()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "+buffbot")];

        GiverResolver.GiverResolution resolved = GiverResolver.Resolve(objects, "buffbot");

        Assert.Equal(GiverResolver.GiverLookup.Found, resolved.Result);
        Assert.Equal(1u, resolved.ObjectId);
    }

    [Fact]
    public void ResolvesWhenOnlyTheChatLineCarriesThePlusPrefix()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer")];

        GiverResolver.GiverResolution resolved = GiverResolver.Resolve(objects, "+Archer");

        Assert.Equal(GiverResolver.GiverLookup.Found, resolved.Result);
        Assert.Equal(1u, resolved.ObjectId);
    }

    [Fact]
    public void ReportsNotFoundForAnUnknownName()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer")];

        GiverResolver.GiverResolution resolved = GiverResolver.Resolve(objects, "Nobody");

        Assert.Equal(GiverResolver.GiverLookup.NotFound, resolved.Result);
    }

    [Fact]
    public void ReportsAmbiguityInsteadOfPickingAName()
    {
        IReadOnlyList<PluginWorldObject> objects = [Object(1, "Archer"), Object(2, "Archer")];

        GiverResolver.GiverResolution resolved = GiverResolver.Resolve(objects, "Archer");

        Assert.Equal(GiverResolver.GiverLookup.Ambiguous, resolved.Result);
    }
}
