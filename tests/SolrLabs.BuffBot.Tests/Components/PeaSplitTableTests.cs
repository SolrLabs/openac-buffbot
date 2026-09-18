using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary>Host-free coverage of the pea -> component yield table — the seven recipes
/// selection and the mid-chain retry both look up by component id.</summary>
public sealed class PeaSplitTableTests
{
    [Theory]
    [InlineData(20631u, 20963u, 50)] // Prismatic Taper <- Prismatic Pea
    [InlineData(691u, 8329u, 20)] // Lead Scarab <- Lead Pea
    [InlineData(689u, 8328u, 20)] // Iron Scarab <- Iron Pea
    [InlineData(686u, 8326u, 20)] // Copper Scarab <- Copper Pea
    [InlineData(688u, 8331u, 20)] // Silver Scarab <- Silver Pea
    [InlineData(687u, 8327u, 20)] // Gold Scarab <- Gold Pea
    [InlineData(690u, 8330u, 20)] // Pyreal Scarab <- Pyreal Pea
    public void EveryRecipeYieldsTheVerifiedAmount(uint componentWeenieId, uint peaWeenieId, int yield)
    {
        Assert.True(PeaSplitTable.TryGetRecipeForComponent(componentWeenieId, out PeaSplitRecipe recipe));
        Assert.Equal(peaWeenieId, recipe.PeaWeenieId);
        Assert.Equal(yield, recipe.Yield);
        Assert.Equal(componentWeenieId, recipe.ComponentWeenieId);
    }

    [Fact]
    public void AComponentWithNoRecipeIsNotFound() =>
        Assert.False(PeaSplitTable.TryGetRecipeForComponent(componentWeenieId: 12345u, out _));

    [Fact]
    public void ScarabsWithoutAPeaHaveNoRecipe()
    {
        // Platinum, Diamond and Mana scarabs (ContributionAdvisor.ScarabsWithoutPeas) have no pea
        // at all — never a splitting candidate.
        Assert.False(PeaSplitTable.TryGetRecipeForComponent(8897u, out _)); // Platinum
        Assert.False(PeaSplitTable.TryGetRecipeForComponent(7299u, out _)); // Diamond
        Assert.False(PeaSplitTable.TryGetRecipeForComponent(37155u, out _)); // Mana
    }

    [Fact]
    public void ExactlySevenRecipesExist() => Assert.Equal(7, PeaSplitTable.Recipes.Count);
}
