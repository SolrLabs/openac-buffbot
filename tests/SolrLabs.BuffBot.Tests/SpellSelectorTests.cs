using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests;

public sealed class SpellSelectorTests
{
    [Fact]
    public void PicksTheHighestTierMatchingTheLine()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1),
            Spell(2, "Strength Other II", family: 10, tier: 2),
            Spell(3, "Strength Other III", family: 10, tier: 3),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(3u, resolved.Spell.SpellId);
        Assert.Equal("Strength Other", resolved.Line);
    }

    [Fact]
    public void IncantationOutranksVI()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other IV", family: 10, tier: 4),
            Spell(2, "Strength Other V", family: 10, tier: 5),
            Spell(3, "Strength Other VI", family: 10, tier: 6),
            Spell(4, "Strength Other Incantation", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(4u, resolved.Spell.SpellId);
    }

    [Fact]
    public void ALineWithOnlyIThroughVIStillPicksVI()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1),
            Spell(2, "Strength Other II", family: 10, tier: 2),
            Spell(3, "Strength Other III", family: 10, tier: 3),
            Spell(4, "Strength Other IV", family: 10, tier: 4),
            Spell(5, "Strength Other V", family: 10, tier: 5),
            Spell(6, "Strength Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(6u, resolved.Spell.SpellId);
    }

    [Fact]
    public void ALineWithNoIncantationAtAllStillTopsOutAtItsHighestNumeral()
    {
        // Some mastery lines' numerals run to VII with no Incantation above them at all —
        // the prefix shape must not invent a rung that was never in the catalog.
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Cooking Mastery Other I", family: 10, tier: 1),
            Spell(2, "Cooking Mastery Other VI", family: 10, tier: 6),
            Spell(3, "Cooking Mastery Other VII", family: 10, tier: 7),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Cooking Mastery Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(3u, resolved.Spell.SpellId);
        Assert.Equal([3u, 2u, 1u], resolved.LearnedTiersDescending.Select(s => s.SpellId));
    }

    [Fact]
    public void APrefixOnlyLineResolvesToItsIncantationRatherThanVI()
    {
        // The leading "Incantation of ..." shape is how 353 of the catalog's 364
        // Incantation-bearing lines carry their top tier.
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Armor Other IV", family: 10, tier: 4),
            Spell(2, "Armor Other V", family: 10, tier: 5),
            Spell(3, "Armor Other VI", family: 10, tier: 6),
            Spell(4, "Incantation of Armor Other", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Armor Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(4u, resolved.Spell.SpellId);
    }

    [Fact]
    public void IncantationPrefixFormOutranksVI()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Incantation of Armor Other", family: 10, tier: 6),
            Spell(2, "Armor Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Armor Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(1u, resolved.Spell.SpellId);
        Assert.Equal([1u, 2u], resolved.LearnedTiersDescending.Select(s => s.SpellId));
    }

    [Fact]
    public void ABothFormsLineCollapsesTheTwoIncantationIdsToOneRung()
    {
        // The same conceptual top tier under two spell ids must carry once, not twice,
        // or a fizzle at the top rung would step "down" into the same tier under the other id.
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other V", family: 10, tier: 5),
            Spell(2, "Strength Other VI", family: 10, tier: 6),
            Spell(3, "Strength Other Incantation", family: 10, tier: 6),
            Spell(4, "Incantation of Strength Other", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        // Exactly three rungs — Incantation, VI, V — never four: the two Incantation ids
        // collapse to whichever appeared first in the catalog (spell 3).
        Assert.Equal(3, resolved.LearnedTiersDescending.Count);
        Assert.Equal(3u, resolved.Spell.SpellId);
        Assert.Equal([3u, 2u, 1u], resolved.LearnedTiersDescending.Select(s => s.SpellId));
    }

    [Fact]
    public void IgnoresSelfTargetedSpellsEvenWhenTheNameMatches()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1, isSelfTargeted: true),
            Spell(2, "Strength Other II", family: 10, tier: 2, isSelfTargeted: false),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(2u, resolved.Spell.SpellId);
    }

    [Fact]
    public void ReportsNothingLearnedWhenOnlyASelfTargetedIncantationIsKnown()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other Incantation", family: 10, tier: 6, isSelfTargeted: true),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(SpellPlanFailureKind.NothingLearned, result.Failure!.Value.Kind);
    }

    [Fact]
    public void IgnoresASelfTargetedIncantationEvenThoughItWouldOutrankACastableVI()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other VI", family: 10, tier: 6, isSelfTargeted: false),
            Spell(2, "Strength Other Incantation", family: 10, tier: 6, isSelfTargeted: true),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(1u, resolved.Spell.SpellId);
    }

    [Fact]
    public void ReportsNothingLearnedWhenNoTierIsKnown()
    {
        PluginSpellInfo[] catalog = [Spell(1, "Weakness Other I", family: 20, tier: 1)];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(SpellPlanFailureKind.NothingLearned, result.Failure!.Value.Kind);
        Assert.Equal("Strength Other", result.Failure.Value.Line);
    }

    [Fact]
    public void ReportsNothingLearnedWhenOnlyTheSelfVariantIsKnown()
    {
        PluginSpellInfo[] catalog = [Spell(1, "Strength Other I", family: 10, tier: 1, isSelfTargeted: true)];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(SpellPlanFailureKind.NothingLearned, result.Failure!.Value.Kind);
    }

    [Fact]
    public void ATierOffsetPicksThatManyRungsDownFromTheTopOfTheLine()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1),
            Spell(2, "Strength Other II", family: 10, tier: 2),
            Spell(3, "Strength Other III", family: 10, tier: 3),
            Spell(4, "Strength Other IV", family: 10, tier: 4),
            Spell(5, "Strength Other V", family: 10, tier: 5),
            Spell(6, "Strength Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(
            catalog, ["Strength Other"], SpellTargetKind.Other, tierOffset: 2);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(4u, resolved.Spell.SpellId); // VI (0), V (1), IV (2) — two rungs down from VI
    }

    [Fact]
    public void ATierOffsetPastTheBottomOfTheLadderBottomsOutRatherThanFailing()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1),
            Spell(2, "Strength Other II", family: 10, tier: 2),
            Spell(3, "Strength Other III", family: 10, tier: 3),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(
            catalog, ["Strength Other"], SpellTargetKind.Other, tierOffset: 10);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(1u, resolved.Spell.SpellId); // the lowest learned tier, not a failure
    }

    [Fact]
    public void TheDefaultHighestTierResolutionStillCarriesTheFullLadderForLaterFallback()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1),
            Spell(2, "Strength Other II", family: 10, tier: 2),
            Spell(3, "Strength Other III", family: 10, tier: 3),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(3u, resolved.Spell.SpellId); // highest learned, unchanged default
        Assert.Equal([3u, 2u, 1u], resolved.LearnedTiersDescending.Select(s => s.SpellId));
    }

    [Fact]
    public void ReportsUnknownLineForABlankEntry()
    {
        PluginSpellInfo[] catalog = [];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["   "]);

        Assert.False(result.IsSuccess);
        Assert.Equal(SpellPlanFailureKind.UnknownLine, result.Failure!.Value.Kind);
    }

    [Fact]
    public void SelfKindPicksTheHighestTierAmongSelfTargetedSpellsOnly()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Focus Self I", family: 30, tier: 1, isSelfTargeted: true),
            Spell(2, "Focus Self VI", family: 30, tier: 6, isSelfTargeted: true),
            Spell(3, "Focus Other I", family: 31, tier: 1, isSelfTargeted: false),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Focus Self"], SpellTargetKind.Self);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(2u, resolved.Spell.SpellId);
    }

    [Fact]
    public void SelfKindIgnoresATargetedSpellEvenWhenTheLineMatches()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Focus Self I", family: 30, tier: 1, isSelfTargeted: false),
        ];

        SpellSelectionResult result = SpellSelector.Resolve(catalog, ["Focus Self"], SpellTargetKind.Self);

        Assert.False(result.IsSuccess);
        Assert.Equal(SpellPlanFailureKind.NothingLearned, result.Failure!.Value.Kind);
    }

    [Fact]
    public void ResolveLenientSkipsALineThatIsNotLearnedInsteadOfFailingTheBatch()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Focus Self VI", family: 30, tier: 6, isSelfTargeted: true),
        ];

        IReadOnlyList<ResolvedSpell> plan = SpellSelector.ResolveLenient(
            catalog, ["Focus Self", "Willpower Self"], SpellTargetKind.Self);

        ResolvedSpell resolved = Assert.Single(plan);
        Assert.Equal("Focus Self", resolved.Line);
    }

    [Fact]
    public void ResolveLenientSkipsABlankLine()
    {
        PluginSpellInfo[] catalog = [Spell(1, "Focus Self VI", family: 30, tier: 6, isSelfTargeted: true)];

        IReadOnlyList<ResolvedSpell> plan = SpellSelector.ResolveLenient(
            catalog, ["   ", "Focus Self"], SpellTargetKind.Self);

        Assert.Single(plan);
    }

    [Fact]
    public void ResolveForPlayerRequestSkipsAnUnlearnedLineAndReportsIt()
    {
        // Young-bot milestone (unit 1b): one unlearned line out of a request is skipped, not a
        // reason to fail the whole thing.
        PluginSpellInfo[] catalog = [Spell(1, "Strength Other VI", family: 10, tier: 6)];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other", "Willpower Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal("Strength Other", resolved.Line);
        Assert.Equal(["Willpower Other"], result.UnlearnedLines);
    }

    [Fact]
    public void ResolveForPlayerRequestReturnsAnEmptyPlanWhenEveryLineIsUnlearned()
    {
        // Refusing the whole request is left to the caller (BuffCoordinator), which alone
        // knows the profile's own name; this method just reports an empty Plan.
        PluginSpellInfo[] catalog = [Spell(1, "Weakness Other I", family: 20, tier: 1)];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other", "Willpower Other"]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Plan);
        Assert.Equal(["Strength Other", "Willpower Other"], result.UnlearnedLines);
    }

    [Fact]
    public void ResolveForPlayerRequestCastsALineLearnedOnlyAtTierIEvenAtTheTopLearnedDefault()
    {
        PluginSpellInfo[] catalog = [Spell(1, "Strength Other I", family: 10, tier: 1)];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(catalog, ["Strength Other"]);

        Assert.True(result.IsSuccess);
        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(1u, resolved.Spell.SpellId);
        Assert.Empty(result.UnlearnedLines);
    }

    [Fact]
    public void ResolveForPlayerRequestStillFailsTheWholeCallForABlankLine()
    {
        // A blank entry is malformed set data, not a young character's own spellbook -- leniency
        // is not owed to a data bug.
        PluginSpellInfo[] catalog = [];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(catalog, ["   "]);

        Assert.False(result.IsSuccess);
        Assert.Equal(SpellPlanFailureKind.UnknownLine, result.Failure!.Value.Kind);
    }

    // ── target tier (unit 1b) ────────────────────────────────────────────────

    [Fact]
    public void TargetTierPicksTheHighestLearnedTierAtOrBelowTheTarget()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1),
            Spell(2, "Strength Other IV", family: 10, tier: 4),
            Spell(3, "Strength Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other"], targetTier: 5);

        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(2u, resolved.Spell.SpellId); // IV is the highest learned at or below 5
    }

    [Fact]
    public void TargetTierFallsBackToTheLowestLearnedTierAboveItRatherThanSkippingTheLine()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other IV", family: 10, tier: 4),
            Spell(2, "Strength Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other"], targetTier: 1);

        ResolvedSpell resolved = Assert.Single(result.Plan);
        // IV, the weakest learned -- never skipped just for being stronger than asked.
        Assert.Equal(1u, resolved.Spell.SpellId);
    }

    [Fact]
    public void TargetTierExactlyMatchingALearnedNumeralPicksIt()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other IV", family: 10, tier: 4),
            Spell(2, "Strength Other V", family: 10, tier: 5),
            Spell(3, "Strength Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other"], targetTier: 5);

        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(2u, resolved.Spell.SpellId);
    }

    [Fact]
    public void TargetTierEightMatchesIncantation()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other VI", family: 10, tier: 6),
            Spell(2, "Strength Other Incantation", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other"], targetTier: 8);

        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(2u, resolved.Spell.SpellId); // Incantation counts as tier 8
    }

    [Fact]
    public void TargetTierBelowEightStillPrefersANumeralOverIncantation()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other VI", family: 10, tier: 6),
            Spell(2, "Strength Other Incantation", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other"], targetTier: 6);

        ResolvedSpell resolved = Assert.Single(result.Plan);
        // VI (rank 6) is at or below target 6; Incantation (rank 8) is not.
        Assert.Equal(1u, resolved.Spell.SpellId);
    }

    [Fact]
    public void NoTargetTierStillPicksTheTopLearnedTier()
    {
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other IV", family: 10, tier: 4),
            Spell(2, "Strength Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(catalog, ["Strength Other"]);

        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal(2u, resolved.Spell.SpellId);
    }

    [Fact]
    public void TargetTierStillCarriesTheFullLadderForStepDownsToWalk()
    {
        // LearnedTiersDescending is unaffected by the target, only which entry
        // ResolvedSpell.Spell starts on.
        PluginSpellInfo[] catalog =
        [
            Spell(1, "Strength Other I", family: 10, tier: 1),
            Spell(2, "Strength Other IV", family: 10, tier: 4),
            Spell(3, "Strength Other VI", family: 10, tier: 6),
        ];

        SpellSelectionResult result = SpellSelector.ResolveForPlayerRequest(
            catalog, ["Strength Other"], targetTier: 5);

        ResolvedSpell resolved = Assert.Single(result.Plan);
        Assert.Equal([3u, 2u, 1u], resolved.LearnedTiersDescending.Select(s => s.SpellId));
    }

    [Fact]
    public void TheBuffSetResolvesEveryLineInOrderAgainstACatalogThatKnowsThemAll()
    {
        IReadOnlyList<string> lines = DefaultSpellSets.Table[DefaultSpellSets.Buff];
        var catalog = new List<PluginSpellInfo>();
        for (int i = 0; i < lines.Count; i++)
            catalog.Add(Spell((uint)(i + 1), $"{lines[i]} I", family: (uint)(100 + i), tier: 1));

        SpellSelectionResult result = SpellSelector.Resolve(catalog, lines);

        Assert.True(result.IsSuccess);
        Assert.Equal(lines, result.Plan.Select(resolved => resolved.Line));
    }

    private static PluginSpellInfo Spell(
        uint spellId, string name, uint family, int tier, bool isSelfTargeted = false) =>
        new(
            SpellId: spellId,
            Name: name,
            Family: family,
            Tier: tier,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 120f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: isSelfTargeted,
            IsBeneficial: true);
}
