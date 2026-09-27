using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests.Spells;

public sealed class SpellReachTests
{
    private const uint CreatureEnchantment = 31u;

    private static PluginSpellInfo Other(float constant, float modifier, uint school = CreatureEnchantment) =>
        new(1u, "Strength Other", 1u, 1, 1, 10, 1800f, school, string.Empty, IsSelfTargeted: false, IsBeneficial: true)
        {
            BaseRangeConstant = constant,
            BaseRangeModifier = modifier,
        };

    // The buff "Other" lines' modifiers by tier, from the End-of-Retail spell table.
    [Theory]
    [InlineData(1.0f, 100u, 104d)]
    [InlineData(0.85f, 20u, 21d)]
    [InlineData(0.7f, 20u, 18d)]
    [InlineData(0.55f, 20u, 15d)]
    [InlineData(0.4f, 20u, 12d)]
    [InlineData(0.25f, 20u, 9d)]
    public void ReachIsConstantPlusModifierTimesRanksLessOneMetre(float modifier, uint ranks, double uncapped)
    {
        double expected = Math.Min(uncapped, SpellReach.CapMetres - SpellReach.SafetyMarginMetres);

        Assert.Equal(expected, SpellReach.Metres(Other(5f, modifier), ranks), precision: 3);
    }

    [Fact]
    public void ReachNeverExceedsTheCapLessTheMargin() =>
        Assert.Equal(74d, SpellReach.Metres(Other(5f, 1f), 500u), precision: 3);

    [Fact]
    public void ZeroRanksReachesTheConstantLessTheMargin() =>
        Assert.Equal(4d, SpellReach.Metres(Other(5f, 0.85f), 0u), precision: 3);

    [Fact]
    public void AnUnreadableSkillFallsBackToTheLargestReach() =>
        Assert.Equal(SpellReach.UnknownReachMetres, SpellReach.Metres(Other(5f, 0.85f), null));

    [Fact]
    public void MissingRangeMetadataFallsBackToTheLargestReach() =>
        Assert.Equal(SpellReach.UnknownReachMetres, SpellReach.Metres(Other(0f, 0f), 50u));

    [Fact]
    public void AnUnmappedSchoolNeverAsksForRanks()
    {
        bool asked = false;

        double reach = SpellReach.ForSpell(Other(5f, 0.85f, school: 0u), _ => { asked = true; return 2u; });

        Assert.False(asked);
        Assert.Equal(SpellReach.UnknownReachMetres, reach);
    }

    [Fact]
    public void RanksAreReadForTheSpellsOwnSchool()
    {
        double reach = SpellReach.ForSpell(Other(5f, 0.85f), skill => skill == CreatureEnchantment ? 2u : null);

        Assert.InRange(reach, 5.69d, 5.71d);
    }
}
