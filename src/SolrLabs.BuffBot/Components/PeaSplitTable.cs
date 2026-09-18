namespace SolrLabs.BuffBot.Components;

/// <summary>One pea the Splitting Tool breaks into a stack of the reagent it stands in for. <see
/// cref="PeaWeenieId"/>/<see cref="ComponentWeenieId"/> are the identity; names are display only.</summary>
internal readonly record struct PeaSplitRecipe(
    uint PeaWeenieId, string PeaName, uint ComponentWeenieId, string ComponentName, int Yield);

/// <summary>Scoped to the six scarab colours and the Prismatic Taper — the reagents <see
/// cref="CastingReagents"/> recognises.</summary>
internal static class PeaSplitTable
{
    internal static readonly IReadOnlyList<PeaSplitRecipe> Recipes =
    [
        new(20963u, "Prismatic Pea", ContributionAdvisor.PrismaticTaperWeenieId, "Prismatic Taper", 50),
        new(8329u, "Lead Pea", 691u, "Lead Scarab", 20),
        new(8328u, "Iron Pea", 689u, "Iron Scarab", 20),
        new(8326u, "Copper Pea", 686u, "Copper Scarab", 20),
        new(8331u, "Silver Pea", 688u, "Silver Scarab", 20),
        new(8327u, "Gold Pea", 687u, "Gold Scarab", 20),
        new(8330u, "Pyreal Pea", 690u, "Pyreal Scarab", 20),
    ];

    private static readonly IReadOnlyDictionary<uint, PeaSplitRecipe> ByComponent =
        BuildByComponent();

    /// <summary>The recipe that produces <paramref name="componentWeenieId"/>, if any.</summary>
    internal static bool TryGetRecipeForComponent(uint componentWeenieId, out PeaSplitRecipe recipe) =>
        ByComponent.TryGetValue(componentWeenieId, out recipe);

    private static IReadOnlyDictionary<uint, PeaSplitRecipe> BuildByComponent()
    {
        var byComponent = new Dictionary<uint, PeaSplitRecipe>(Recipes.Count);
        foreach (PeaSplitRecipe recipe in Recipes)
            byComponent[recipe.ComponentWeenieId] = recipe;
        return byComponent;
    }
}
