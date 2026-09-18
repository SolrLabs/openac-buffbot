using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests;

public sealed class SelfBuffPlannerTests
{
    [Fact]
    public void EveryLearnedLineIsDueWhenNothingIsActive()
    {
        PluginSpellInfo[] catalog = [SelfSpell("Focus Self VI", family: 1, spellId: 10)];

        IReadOnlyList<ResolvedSpell> due = SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], []);

        ResolvedSpell resolved = Assert.Single(due);
        Assert.Equal(10u, resolved.Spell.SpellId);
    }

    [Fact]
    public void AFamilyUnderFiveMinutesRemainingIsStillDue()
    {
        PluginSpellInfo[] catalog = [SelfSpell("Focus Self VI", family: 1, spellId: 10)];
        PluginActiveEnchantment[] active = [Active(family: 1, secondsRemaining: 100)];

        IReadOnlyList<ResolvedSpell> due = SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], active);

        Assert.Single(due);
    }

    [Fact]
    public void AFamilyAtExactlyFiveMinutesIsNotDue()
    {
        PluginSpellInfo[] catalog = [SelfSpell("Focus Self VI", family: 1, spellId: 10)];
        PluginActiveEnchantment[] active = [Active(family: 1, secondsRemaining: SelfBuffPlanner.DueThresholdSeconds)];

        IReadOnlyList<ResolvedSpell> due = SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], active);

        Assert.Empty(due);
    }

    [Fact]
    public void AFamilyWithPlentyRemainingIsNotDue()
    {
        PluginSpellInfo[] catalog = [SelfSpell("Focus Self VI", family: 1, spellId: 10)];
        PluginActiveEnchantment[] active = [Active(family: 1, secondsRemaining: 600)];

        IReadOnlyList<ResolvedSpell> due = SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], active);

        Assert.Empty(due);
    }

    [Fact]
    public void ALineTheCasterHasNotLearnedIsSkippedRatherThanFailingTheWholeSet()
    {
        PluginSpellInfo[] catalog = [SelfSpell("Focus Self VI", family: 1, spellId: 10)];

        IReadOnlyList<ResolvedSpell> due =
            SelfBuffPlanner.PlanDue(catalog, ["Focus Self", "Willpower Self"], []);

        ResolvedSpell resolved = Assert.Single(due);
        Assert.Equal("Focus Self", resolved.Line);
    }

    [Fact]
    public void TwoDueFamiliesBothComeBack()
    {
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self VI", family: 1, spellId: 10),
            SelfSpell("Willpower Self VI", family: 2, spellId: 11),
        ];
        PluginActiveEnchantment[] active = [Active(family: 1, secondsRemaining: 600)]; // only Focus is up

        IReadOnlyList<ResolvedSpell> due =
            SelfBuffPlanner.PlanDue(catalog, ["Focus Self", "Willpower Self"], active);

        ResolvedSpell resolved = Assert.Single(due);
        Assert.Equal(11u, resolved.Spell.SpellId);
    }

    private static PluginSpellInfo SelfSpell(string name, uint family, uint spellId) =>
        new(
            SpellId: spellId,
            Name: name,
            Family: family,
            Tier: 6,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 1200f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: true,
            IsBeneficial: true);

    private static PluginActiveEnchantment Active(uint family, double secondsRemaining) =>
        new(SpellId: 1, Family: family, Tier: 6, SecondsRemaining: secondsRemaining);
}
