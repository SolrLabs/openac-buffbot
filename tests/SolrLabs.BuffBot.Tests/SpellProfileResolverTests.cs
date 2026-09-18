using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests;

public sealed class SpellProfileResolverTests
{
    [Fact]
    public void ResolvesALayerWithNoIncludesToItsOwnLinesInOrder()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["base"] = new SpellLayer("base",
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Cast("Endurance Other"),
            ]),
        };

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "base");

        Assert.Equal(["Strength Other", "Endurance Other"], resolved);
    }

    [Fact]
    public void ANestedLayerExpandsWhereItIsPlaced()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["prots"] = new SpellLayer("prots",
            [
                SpellLayerElement.Cast("Armor Other"),
                SpellLayerElement.Cast("Acid Protection Other"),
            ]),
            ["generic"] = new SpellLayer("generic",
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Cast("Endurance Other"),
                SpellLayerElement.Layer("prots"),
            ]),
        };

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "generic");

        // The nested layer lands exactly where its element sits, not at the front or back
        // regardless of how the layer that contains it phrases things.
        Assert.Equal(
            ["Strength Other", "Endurance Other", "Armor Other", "Acid Protection Other"],
            resolved);
    }

    [Fact]
    public void AStyleLayerBuildsOnTheGenericLayerThenAddsItsOwnLineLast()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["generic"] = new SpellLayer("generic",
            [
                SpellLayerElement.Cast("Strength Other"),
            ]),
            ["heavy"] = new SpellLayer("heavy",
            [
                SpellLayerElement.Layer("generic"),
                SpellLayerElement.Cast("Heavy Weapon Mastery Other"),
            ]),
        };

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "heavy");

        Assert.Equal(["Strength Other", "Heavy Weapon Mastery Other"], resolved);
    }

    [Fact]
    public void ALineAlreadyContributedByAnEarlierLayerIsNotDuplicated()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["generic"] = new SpellLayer("generic",
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Cast("Armor Other"),
            ]),
            // A style layer that (mistakenly, or through a shared sub-include) re-lists a line
            // generic already contributed. The convention is explicit: skip it, keep the first.
            ["heavy"] = new SpellLayer("heavy",
            [
                SpellLayerElement.Layer("generic"),
                SpellLayerElement.Cast("Armor Other"),
                SpellLayerElement.Cast("Heavy Weapon Mastery Other"),
            ]),
        };

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "heavy");

        Assert.Equal(["Strength Other", "Armor Other", "Heavy Weapon Mastery Other"], resolved);
    }

    [Fact]
    public void ADiamondIncludeContributesItsSharedLineOnlyOnce()
    {
        // Two different included layers both nest the same base layer. The base's line must
        // appear exactly once, at the position of whichever include reaches it first.
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["prots"] = new SpellLayer("prots",
            [
                SpellLayerElement.Cast("Armor Other"),
            ]),
            ["left"] = new SpellLayer("left",
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Layer("prots"),
            ]),
            ["right"] = new SpellLayer("right",
            [
                SpellLayerElement.Layer("prots"),
                SpellLayerElement.Cast("Endurance Other"),
            ]),
            ["diamond"] = new SpellLayer("diamond",
            [
                SpellLayerElement.Layer("left"),
                SpellLayerElement.Layer("right"),
            ]),
        };

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "diamond");

        Assert.Equal(["Strength Other", "Armor Other", "Endurance Other"], resolved);
    }

    [Fact]
    public void ADirectSelfIncludeTerminatesInsteadOfRecursingForever()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["loopy"] = new SpellLayer("loopy",
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Layer("loopy"),
                SpellLayerElement.Cast("Endurance Other"),
            ]),
        };

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "loopy");

        // The re-entrant include contributes nothing on its second visit; both of loopy's own
        // lines still land, in order, because they sit either side of the self-include.
        Assert.Equal(["Strength Other", "Endurance Other"], resolved);
    }

    [Fact]
    public void AMutualCycleBetweenTwoLayersTerminates()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = new SpellLayer("a",
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Layer("b"),
            ]),
            ["b"] = new SpellLayer("b",
            [
                SpellLayerElement.Cast("Endurance Other"),
                SpellLayerElement.Layer("a"),
                SpellLayerElement.Cast("Coordination Other"),
            ]),
        };

        // The point of this test is that it returns at all rather than stack-overflowing.
        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "a");

        Assert.Equal(["Strength Other", "Endurance Other", "Coordination Other"], resolved);
    }

    [Fact]
    public void AnIncludeNamingAnUndefinedLayerContributesNothingRatherThanThrowing()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            ["heavy"] = new SpellLayer("heavy",
            [
                SpellLayerElement.Layer("does-not-exist"),
                SpellLayerElement.Cast("Heavy Weapon Mastery Other"),
            ]),
        };

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "heavy");

        Assert.Equal(["Heavy Weapon Mastery Other"], resolved);
    }

    [Fact]
    public void ResolvingAnUndefinedTopLevelLayerReturnsAnEmptyList()
    {
        var layers = new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase);

        IReadOnlyList<string> resolved = SpellProfileResolver.Resolve(layers, "nothing-here");

        Assert.Empty(resolved);
    }
}
