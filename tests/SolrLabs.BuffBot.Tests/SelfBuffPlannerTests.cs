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

    private static PluginSpellInfo SelfSpell(string name, uint family, uint spellId, int tier = 6) =>
        new(
            SpellId: spellId,
            Name: name,
            Family: family,
            Tier: tier,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 1200f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: true,
            IsBeneficial: true);

    private static PluginActiveEnchantment Active(uint family, double secondsRemaining, int tier = 6) =>
        new(SpellId: 1, Family: family, Tier: tier, SecondsRemaining: secondsRemaining);

    [Fact]
    public void ALearnedTierAboveTheActiveOneIsDue()
    {
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self III", family: 1, spellId: 10, tier: 3),
            SelfSpell("Focus Self IV", family: 1, spellId: 11, tier: 4),
        ];
        PluginActiveEnchantment[] active = [Active(family: 1, secondsRemaining: 600, tier: 3)];

        IReadOnlyList<ResolvedSpell> due = SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], active);

        ResolvedSpell resolved = Assert.Single(due);
        Assert.Equal(11u, resolved.Spell.SpellId);
    }

    [Fact]
    public void OneLineUnderFiveMinutesPlansTheWholeSet()
    {
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self VI", family: 1, spellId: 10),
            SelfSpell("Willpower Self VI", family: 2, spellId: 11),
        ];
        PluginActiveEnchantment[] active =
        [
            Active(family: 1, secondsRemaining: 100), // due
            Active(family: 2, secondsRemaining: 600), // not due on its own
        ];

        IReadOnlyList<ResolvedSpell> full =
            SelfBuffPlanner.PlanFullRebuff(catalog, ["Focus Self", "Willpower Self"], active);

        Assert.Equal(2, full.Count);
    }

    [Fact]
    public void NothingDuePlansNothing()
    {
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self VI", family: 1, spellId: 10),
            SelfSpell("Willpower Self VI", family: 2, spellId: 11),
        ];
        PluginActiveEnchantment[] active =
        [
            Active(family: 1, secondsRemaining: 600),
            Active(family: 2, secondsRemaining: 600),
        ];

        IReadOnlyList<ResolvedSpell> full =
            SelfBuffPlanner.PlanFullRebuff(catalog, ["Focus Self", "Willpower Self"], active);

        Assert.Empty(full);
    }

    [Fact]
    public void ADuplicateFamilyPicksTheStrongestThenLongestRemaining()
    {
        PluginSpellInfo[] catalog = [SelfSpell("Focus Self VI", family: 1, spellId: 10)];
        PluginActiveEnchantment[] active =
        [
            new(SpellId: 1, Family: 1, Tier: 6, SecondsRemaining: 50),
            new(SpellId: 2, Family: 1, Tier: 6, SecondsRemaining: 400), // same tier, longer remaining wins
        ];

        IReadOnlyList<ResolvedSpell> due = SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], active);

        Assert.Empty(due);
    }

    [Fact]
    public void AComponentCeilingSuppressesTheOutOfTierTriggerAcrossRepeatedPasses()
    {
        // Bot learned VII but reagents only ever supported VI; that is what it actually casts now.
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self VI", family: 1, spellId: 10, tier: 6),
            SelfSpell("Focus Self VII", family: 1, spellId: 11, tier: 7),
        ];
        PluginActiveEnchantment[] active = [Active(family: 1, secondsRemaining: 600, tier: 6)];

        for (int pass = 0; pass < 3; pass++)
        {
            IReadOnlyList<ResolvedSpell> due =
                SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], active, componentCeilingRung: 1);
            Assert.Empty(due);
        }
    }

    [Fact]
    public void AStepDownRememberedAtTheActiveTierIsNotOutTiered()
    {
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self VI", family: 1, spellId: 10, tier: 6),
            SelfSpell("Focus Self VII", family: 1, spellId: 11, tier: 7),
        ];
        PluginActiveEnchantment[] active = [Active(family: 1, secondsRemaining: 600, tier: 6)];

        IReadOnlyList<ResolvedSpell> withoutMemory =
            SelfBuffPlanner.PlanDue(catalog, ["Focus Self"], active);
        Assert.Single(withoutMemory); // VI is below the top learned VII: out-tiered.

        var rememberedStepDown = new Dictionary<uint, int> { [1] = 6 };
        IReadOnlyList<ResolvedSpell> withMemory = SelfBuffPlanner.PlanDue(
            catalog, ["Focus Self"], active, componentCeilingRung: null, rememberedStepDown);
        Assert.Empty(withMemory); // VI is exactly what a fizzle step-down landed last time.
    }

    [Fact]
    public void ALineLandedSteppedDownDoesNotTriggerAFullRebuffWhileNothingElseIsDue()
    {
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self VI", family: 1, spellId: 10, tier: 6),
            SelfSpell("Focus Self VII", family: 1, spellId: 11, tier: 7),
            SelfSpell("Willpower Self VI", family: 2, spellId: 20, tier: 6),
        ];
        PluginActiveEnchantment[] active =
        [
            Active(family: 1, secondsRemaining: 600, tier: 6), // stepped down from VII last time
            Active(family: 2, secondsRemaining: 600, tier: 6), // fully up, nothing else is due
        ];
        var rememberedStepDown = new Dictionary<uint, int> { [1] = 6 };

        IReadOnlyList<ResolvedSpell> full = SelfBuffPlanner.PlanFullRebuff(
            catalog, ["Focus Self", "Willpower Self"], active, componentCeilingRung: null, rememberedStepDown);

        Assert.Empty(full);
    }

    [Fact]
    public void ARefusedLineDoesNotTriggerAFullRebuffButRidesAlongWhenAnotherLineIsDue()
    {
        PluginSpellInfo[] catalog =
        [
            SelfSpell("Focus Self VI", family: 1, spellId: 10),
            SelfSpell("Willpower Self VI", family: 2, spellId: 20),
        ];
        var refused = new HashSet<uint> { 1 }; // Focus Self always fails, never lands

        // Nothing else is due: the refused line alone must not trigger a full pass.
        IReadOnlyList<ResolvedSpell> full = SelfBuffPlanner.PlanFullRebuff(
            catalog, ["Focus Self", "Willpower Self"],
            activeEnchantments: [Active(family: 2, secondsRemaining: 600)],
            componentCeilingRung: null, lastLandedTierByFamily: null, refusedFamilies: refused);
        Assert.Empty(full);

        // Willpower comes due: the refused Focus Self still rides along in the same pass.
        IReadOnlyList<ResolvedSpell> triggered = SelfBuffPlanner.PlanFullRebuff(
            catalog, ["Focus Self", "Willpower Self"],
            activeEnchantments: [Active(family: 2, secondsRemaining: 100)],
            componentCeilingRung: null, lastLandedTierByFamily: null, refusedFamilies: refused);
        Assert.Equal(2, triggered.Count);
    }
}
