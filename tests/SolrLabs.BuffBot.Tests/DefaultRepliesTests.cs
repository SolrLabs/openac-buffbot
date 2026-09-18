using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

/// <summary>A run ending with per-spell failures is a <see cref="DefaultReplies.ClosingPartial"/>,
/// neither a clean <see cref="DefaultReplies.ClosingReply"/> nor an aborted <see cref="DefaultReplies.ClosingFailure"/>.</summary>
public sealed class DefaultRepliesTests
{
    /// <summary>A player asked for "heavy" and knows roughly what that means, so a full success
    /// reports counts rather than echoing back all thirty-odd spell names.</summary>
    [Fact]
    public void ClosingReplyWithNothingToCastSaysSo()
    {
        Assert.Equal("There was nothing for me to cast.", DefaultReplies.ClosingReply([]));
    }

    [Fact]
    public void ClosingReplyWithOneCastUsesTheSingular()
    {
        CastStep[] steps = [new("Strength Other", 1, CastOutcome.Cast)];

        Assert.Equal("All set: cast 1 buff.", DefaultReplies.ClosingReply(steps));
    }

    [Fact]
    public void ClosingReplyWithSeveralCastsAndNoneAlreadyUpNamesOnlyTheCount()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new("Focus Other", 2, CastOutcome.Cast),
            new("Endurance Other", 3, CastOutcome.Cast),
        ];

        Assert.Equal("All set: cast 3 buffs.", DefaultReplies.ClosingReply(steps));
    }

    [Fact]
    public void ClosingReplyMentionsAlreadyUpCountWhenSomeSpellsWereSkipped()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new("Focus Other", 2, CastOutcome.Skipped),
        ];

        Assert.Equal("All set: cast 1 buff, 1 already up.", DefaultReplies.ClosingReply(steps));
    }

    /// <summary>Every line asked for was already up: nothing was cast, so this reads as its own
    /// fact rather than "All set: cast 0 buffs, 2 already up."</summary>
    [Fact]
    public void ClosingReplyWhenEverythingWasAlreadyUpSaysSoWithoutACastCount()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Skipped),
            new("Focus Other", 2, CastOutcome.Skipped),
        ];

        Assert.Equal(
            "You're already fully buffed, nothing new to cast.", DefaultReplies.ClosingReply(steps));
    }

    /// <summary>Says so once, briefly, using a rung count rather than an absolute
    /// numeral, since lines do not share a top tier.</summary>
    [Fact]
    public void ClosingReplyNamesAReducedTierOnceWhenTheComponentCeilingApplied()
    {
        CastStep[] steps = [new("Focus Self", 1, CastOutcome.Cast)];

        string closing = DefaultReplies.ClosingReply(steps, componentCeilingRung: 1);

        Assert.Equal(
            "All set: cast 1 buff. I'm short on higher-level components, so I cast one tier down.",
            closing);
    }

    [Fact]
    public void ClosingReplyNamesTheRungCountWhenMoreThanOneTierWasStepped()
    {
        CastStep[] steps = [new("Focus Self", 1, CastOutcome.Cast)];

        string closing = DefaultReplies.ClosingReply(steps, componentCeilingRung: 2);

        Assert.Equal(
            "All set: cast 1 buff. I'm short on higher-level components, so I cast 2 tiers down.",
            closing);
    }

    [Fact]
    public void ClosingPartialReportsCountsAndNamesEachMissedSpellWithItsReason()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new("Focus Other", 2, CastOutcome.Skipped), // already up
            new(
                "Endurance Other", 3, CastOutcome.Failed,
                new CastFailure(CastFailureKind.Fizzled, "Endurance Other", "fizzled twice")),
        ];

        string closing = DefaultReplies.ClosingPartial(steps);

        Assert.Equal(
            "I cast what I could: buffed 1, 1 already up, but missed 1: Endurance Other (fizzled twice).",
            closing);
    }

    [Fact]
    public void ClosingPartialCountsUnlearnedLinesAndNamesAtMostThreeMisses()
    {
        var steps = new List<CastStep> { new("Strength Other", 1, CastOutcome.Cast) };
        foreach (string line in new[] { "Focus Other", "Willpower Other", "Quickness Other", "Coordination Other" })
            steps.Add(new(line, 2, CastOutcome.Failed, new CastFailure(CastFailureKind.DidNotLand, line, "no confirmation received")));
        for (int i = 0; i < 20; i++)
            steps.Add(new($"Line {i}", 3, CastOutcome.Failed, new CastFailure(CastFailureKind.NotLearned, $"Line {i}", "")));

        string closing = DefaultReplies.ClosingPartial(steps);

        Assert.StartsWith("I cast what I could: buffed 1, 0 already up, but missed 4: Focus Other (", closing);
        Assert.Contains(" and 1 more.", closing);
        Assert.DoesNotContain("Coordination Other", closing);
        Assert.EndsWith(" I haven't learned 20 of those yet.", closing);
    }

    [Fact]
    public void ClosingPartialNamesAFewUnlearnedLinesRatherThanCountingThem()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new("Focus Other", 2, CastOutcome.Failed, new CastFailure(CastFailureKind.NotLearned, "Focus Other", "")),
        ];

        Assert.Equal(
            "I cast what I could: buffed 1, 0 already up. I haven't learned Focus Other yet.",
            DefaultReplies.ClosingPartial(steps));
    }

    [Fact]
    public void ClosingPartialWithNoAlreadyUpSpellsStillReportsZero()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new(
                "Health Other", 2, CastOutcome.Failed,
                new CastFailure(CastFailureKind.DidNotLand, "Health Other", "no confirmation received")),
        ];

        string closing = DefaultReplies.ClosingPartial(steps);

        Assert.Equal(
            "I cast what I could: buffed 1, 0 already up, but missed 1: Health Other (didn't land).",
            closing);
    }

    [Fact]
    public void ClosingPartialListsMultipleMissedSpellsInOrder()
    {
        CastStep[] steps =
        [
            new(
                "Endurance Other", 1, CastOutcome.Failed,
                new CastFailure(CastFailureKind.Fizzled, "Endurance Other", "fizzled twice")),
            new("Strength Other", 2, CastOutcome.Cast),
            new(
                "Health Other", 3, CastOutcome.Failed,
                new CastFailure(CastFailureKind.TimedOut, "Health Other", "busy too long")),
        ];

        string closing = DefaultReplies.ClosingPartial(steps);

        Assert.Equal(
            "I cast what I could: buffed 1, 0 already up, but missed 2: "
                + "Endurance Other (fizzled twice), Health Other (busy too long).",
            closing);
    }

    [Fact]
    public void ClosingPartialNamesAnUnlearnedLineFromTheYoungBotMilestone()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new(
                "Endurance Other", 0, CastOutcome.Failed,
                new CastFailure(CastFailureKind.NotLearned, "Endurance Other", "not learned")),
        ];

        string closing = DefaultReplies.ClosingPartial(steps);

        Assert.Equal(
            "I cast what I could: buffed 1, 0 already up. I haven't learned Endurance Other yet.",
            closing);
    }

    [Fact]
    public void ClosingPartialNamesNoShieldOnceRegardlessOfHowManyBanesItCost()
    {
        var steps = new List<CastStep> { new("Strength Other", 1, CastOutcome.Cast) };
        foreach (string line in new[] { "Acid Bane", "Blade Bane", "Flame Bane" })
            steps.Add(new(line, 0, CastOutcome.Failed, new CastFailure(CastFailureKind.NoShield, line, "no shield equipped")));

        string closing = DefaultReplies.ClosingPartial(steps);

        Assert.Equal(
            "I cast what I could: buffed 1, 0 already up. No shield equipped, so I skipped 3 banes.",
            closing);
    }

    [Fact]
    public void ClosingPartialNamesNoShieldSingularForExactlyOneBane()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new("Acid Bane", 0, CastOutcome.Failed, new CastFailure(CastFailureKind.NoShield, "Acid Bane", "no shield equipped")),
        ];

        Assert.Equal(
            "I cast what I could: buffed 1, 0 already up. No shield equipped, so I skipped 1 bane.",
            DefaultReplies.ClosingPartial(steps));
    }

    /// <summary>The same reduced-tier note proved for a clean run, appended once
    /// after everything else <see cref="DefaultReplies.ClosingPartial"/> already says.</summary>
    [Fact]
    public void ClosingPartialNamesAReducedTierOnceAfterEverythingElse()
    {
        CastStep[] steps =
        [
            new("Focus Self", 1, CastOutcome.Cast),
            new(
                "Endurance Other", 3, CastOutcome.Failed,
                new CastFailure(CastFailureKind.Fizzled, "Endurance Other", "fizzled twice")),
        ];

        string closing = DefaultReplies.ClosingPartial(steps, componentCeilingRung: 1);

        Assert.Equal(
            "I cast what I could: buffed 1, 0 already up, but missed 1: Endurance Other "
                + "(fizzled twice). I'm short on higher-level components, so I cast one tier down.",
            closing);
    }

    [Fact]
    public void ClosingPartialOmitsTheReducedTierNoteWhenNothingLanded()
    {
        CastStep[] steps =
        [
            new("Focus Self", 1, CastOutcome.Skipped),
            new(
                "Endurance Other", 3, CastOutcome.Failed,
                new CastFailure(CastFailureKind.Fizzled, "Endurance Other", "fizzled twice")),
        ];

        string closing = DefaultReplies.ClosingPartial(steps, componentCeilingRung: 1);

        Assert.Equal(
            "I cast what I could: buffed 0, 1 already up, but missed 1: Endurance Other (fizzled twice).",
            closing);
    }

    [Fact]
    public void NothingLearnedInProfileNamesTheProfileNotASingleLine()
    {
        Assert.Equal(
            "I haven't learned anything for heavy yet.",
            DefaultReplies.NothingLearnedInProfile("heavy"));
    }

    /// <summary>Not swept by <c>ReplyEncodingTests</c>'s reflection, being methods rather
    /// than static fields, so this is their ASCII coverage instead.</summary>
    [Fact]
    public void InspectAndUnwedgedAreAscii()
    {
        string inspect = DefaultReplies.Inspect(
            enabled: true, waitingCount: 3, mutedSenders: ["Archer", "Other"], tellsAnswered: 9);
        string unwedged = DefaultReplies.Unwedged(
            clearedFromQueue: 2, unmuted: new Guard.MuteClearResult(Automatic: 1, Manual: 0));

        foreach (char c in inspect + unwedged)
            Assert.True(c <= 0x7F, $"non-ASCII character U+{(int)c:X4} in an operator reply");
    }

    [Fact]
    public void InspectReportsNoneWhenNobodyIsMuted()
    {
        string inspect = DefaultReplies.Inspect(
            enabled: true, waitingCount: 0, mutedSenders: [], tellsAnswered: 0);

        Assert.Contains("muted: none", inspect);
    }

    [Fact]
    public void OperatorRepliesAreNotBotShaped()
    {
        string inspect = DefaultReplies.Inspect(
            enabled: true, waitingCount: 1, mutedSenders: ["Archer"], tellsAnswered: 1);
        string unwedged = DefaultReplies.Unwedged(
            clearedFromQueue: 1, unmuted: new Guard.MuteClearResult(Automatic: 1, Manual: 0));

        Assert.False(Guard.BotShapeMatcher.IsBotShaped(inspect));
        Assert.False(Guard.BotShapeMatcher.IsBotShaped(unwedged));
    }

    [Fact]
    public void ClosingFailureForTooManyFailuresReportsTheBound()
    {
        var failure = new CastFailure(
            CastFailureKind.TooManyFailures,
            "Health Other",
            "3 spells in a row didn't land, something's wrong");

        string closing = DefaultReplies.ClosingFailure(failure);

        Assert.Equal("I had to stop: 3 spells in a row didn't land, something's wrong.", closing);
    }

    // -- MissingComponents --------------------------------------------------------------------

    /// <summary>Names the real cause rather than blaming "something's wrong."</summary>
    [Fact]
    public void ClosingFailureForMissingComponentsNamesTheRealCauseWithNoStepsYet()
    {
        var failure = new CastFailure(CastFailureKind.MissingComponents, "Armor Other", "MissingComponents");

        string closing = DefaultReplies.ClosingFailure(failure);

        Assert.Equal("I'm out of spell components, so I had to stop.", closing);
    }

    /// <summary>Unlike every other fatal kind, MissingComponents still owes the requester
    /// what the run managed before it ran out.</summary>
    [Fact]
    public void ClosingFailureForMissingComponentsReportsWhatAlreadyLanded()
    {
        CastStep[] steps =
        [
            new("Strength Other", 1, CastOutcome.Cast),
            new("Focus Other", 2, CastOutcome.Skipped),
        ];
        var failure = new CastFailure(CastFailureKind.MissingComponents, "Armor Other", "MissingComponents");

        string closing = DefaultReplies.ClosingFailure(failure, steps);

        Assert.Equal(
            "I cast what I could: buffed 1, 1 already up. I'm out of spell components, so I had to stop.",
            closing);
    }

    /// <summary>A "buffed 0, 0 already up." prefix would say nothing true happened,
    /// so it is dropped and only the honest sentence remains.</summary>
    [Fact]
    public void ClosingFailureForMissingComponentsWithNothingLandedDropsTheBuffedZeroPrefix()
    {
        CastStep[] steps = [new("Focus Other", 2, CastOutcome.Skipped)];
        var failure = new CastFailure(CastFailureKind.MissingComponents, "Focus Self", "MissingComponents");

        string closing = DefaultReplies.ClosingFailure(failure, steps);

        Assert.Equal("I'm out of spell components, so I had to stop.", closing);
    }

    /// <summary>The no-shield note is still owed once the false "buffed 0" prefix
    /// is gone.</summary>
    [Fact]
    public void ClosingFailureForMissingComponentsWithNothingLandedKeepsTheNoShieldNote()
    {
        CastStep[] steps =
        [
            new("Acid Bane", 0, CastOutcome.Failed,
                new CastFailure(CastFailureKind.NoShield, "Acid Bane", "no shield equipped")),
        ];
        var failure = new CastFailure(CastFailureKind.MissingComponents, "Focus Self", "MissingComponents");

        string closing = DefaultReplies.ClosingFailure(failure, steps);

        Assert.Equal(
            "No shield equipped, so I skipped 1 bane. I'm out of spell components, so I had to stop.",
            closing);
    }

    // -- Stopped, cancel and disable -----------------------------------------------------------

    [Fact]
    public void ClosingStoppedForARequesterCancelSaysTheRunStopped()
    {
        Assert.Equal(
            "Stopped, like you asked.", DefaultReplies.ClosingStopped(RunStopReason.RequesterCancelled));
    }

    [Fact]
    public void ClosingStoppedForDisablingSaysTheBotNeedsABreak()
    {
        Assert.Equal(
            "Sorry folks, need a short break.", DefaultReplies.ClosingStopped(RunStopReason.BotDisabling));
    }

    // -- The `contribute` keyword ---------------------------------------------------------------

    /// <summary>Computed through <see cref="DonationPolicy.WantedItemsDescription"/>,
    /// the same source the production code reads.</summary>
    private static string TradeInvite(bool botHoldsSplittingTool) =>
        "Open a trade with me while I'm not casting.";

    [Fact]
    public void ContributeNamesALowScarabAndTaperWithTheirMatchingPeas()
    {
        IReadOnlyList<ContributionNeed> low =
        [
            new ContributionNeed("Pyreal Scarabs", 4, "Pyreal Peas"),
            new ContributionNeed("Prismatic Tapers", 12, "Prismatic Peas"),
        ];
        var summary = new ContributionSummary(true, true, low, NeedsSplittingTool: false);

        Assert.Equal(
            "Thanks for asking! I'm low on Pyreal Scarabs (4) and Prismatic Tapers (12); "
                + $"Pyreal or Prismatic Peas work too. {TradeInvite(botHoldsSplittingTool: true)}",
            DefaultReplies.Contribute(summary));
    }

    /// <summary>Platinum, Diamond and Mana scarabs have no matching pea — named on their own,
    /// never followed by a peas clause that would otherwise read as an empty "or work too".</summary>
    [Fact]
    public void ContributeNamesAScarabWithNoPeaWithoutAPeasClause()
    {
        IReadOnlyList<ContributionNeed> low = [new ContributionNeed("Diamond Scarabs", 0, PeaAlternative: null)];
        var summary = new ContributionSummary(true, true, low, NeedsSplittingTool: false);

        Assert.Equal(
            $"Thanks for asking! I'm low on Diamond Scarabs (0). {TradeInvite(botHoldsSplittingTool: true)}",
            DefaultReplies.Contribute(summary));
    }

    [Fact]
    public void ContributeAsksForASplittingToolOnlyWhenNoneIsHeldAlongsideLowReagents()
    {
        IReadOnlyList<ContributionNeed> low = [new ContributionNeed("Pyreal Scarabs", 4, "Pyreal Peas")];
        var summary = new ContributionSummary(true, true, low, NeedsSplittingTool: true);

        Assert.Equal(
            "Thanks for asking! I'm low on Pyreal Scarabs (4); Pyreal Peas work too. "
                + $"I could also use a Splitting Tool. {TradeInvite(botHoldsSplittingTool: false)}",
            DefaultReplies.Contribute(summary));
    }

    [Fact]
    public void ContributeDoesNotMentionASplittingToolWhenOneIsAlreadyHeld()
    {
        IReadOnlyList<ContributionNeed> low = [new ContributionNeed("Pyreal Scarabs", 4, "Pyreal Peas")];
        var summary = new ContributionSummary(true, true, low, NeedsSplittingTool: false);

        Assert.DoesNotContain("Splitting Tool", DefaultReplies.Contribute(summary));
    }

    [Fact]
    public void ContributeWithNothingLowAndAToolInHandIsAShortThankYou()
    {
        var summary = new ContributionSummary(true, true, Array.Empty<ContributionNeed>(), NeedsSplittingTool: false);

        Assert.Equal(
            $"{DefaultReplies.ContributeWellStocked} {TradeInvite(botHoldsSplittingTool: true)}",
            DefaultReplies.Contribute(summary));
    }

    [Fact]
    public void ContributeWithOnlyASplittingToolMissingSaysSoWithoutAWellStockedClaim()
    {
        var summary = new ContributionSummary(true, true, Array.Empty<ContributionNeed>(), NeedsSplittingTool: true);

        Assert.Equal(
            $"{DefaultReplies.ContributeSplittingToolOnly} {TradeInvite(botHoldsSplittingTool: false)}",
            DefaultReplies.Contribute(summary));
    }

    /// <summary>Carries the same reasoning as <see cref="ComponentReport.CatalogUnavailable"/>
    /// to this keyword: never claim "well stocked" when the catalog cannot answer.</summary>
    [Fact]
    public void ContributeIsHonestWhenTheCatalogCannotAnswerButStillReportsTheSplittingTool()
    {
        var toolMissing = new ContributionSummary(
            InventoryReadable: true, ReagentCatalogAvailable: false, Array.Empty<ContributionNeed>(),
            NeedsSplittingTool: true);
        var toolHeld = toolMissing with { NeedsSplittingTool = false };

        Assert.Equal(
            $"{DefaultReplies.ContributeCatalogUnavailableNeedTool} {TradeInvite(botHoldsSplittingTool: false)}",
            DefaultReplies.Contribute(toolMissing));
        Assert.Equal(
            $"{DefaultReplies.ContributeCatalogUnavailableToolOk} {TradeInvite(botHoldsSplittingTool: true)}",
            DefaultReplies.Contribute(toolHeld));
        Assert.DoesNotContain("well stocked", DefaultReplies.Contribute(toolMissing));
    }

    [Fact]
    public void ContributeIsHonestWhenTheItemSurfaceItselfCannotAnswerAtAll()
    {
        Assert.Equal(
            $"{DefaultReplies.ContributeInventoryUnavailable} {TradeInvite(botHoldsSplittingTool: true)}",
            DefaultReplies.Contribute(ContributionSummary.Unavailable));
    }

    /// <summary>Every scarab colour and the taper low at once, worst case for length: still fits a
    /// single tell rather than growing without bound.</summary>
    [Fact]
    public void ContributeStaysWithinTheTellLengthLimitEvenWhenEverythingIsLow()
    {
        IReadOnlyList<ContributionNeed> low =
        [
            new ContributionNeed("Lead Scarabs", 1, "Lead Peas"),
            new ContributionNeed("Iron Scarabs", 2, "Iron Peas"),
            new ContributionNeed("Copper Scarabs", 3, "Copper Peas"),
            new ContributionNeed("Silver Scarabs", 4, "Silver Peas"),
            new ContributionNeed("Gold Scarabs", 5, "Gold Peas"),
            new ContributionNeed("Pyreal Scarabs", 6, "Pyreal Peas"),
            new ContributionNeed("Platinum Scarabs", 0, null),
            new ContributionNeed("Diamond Scarabs", 0, null),
            new ContributionNeed("Mana Scarabs", 0, null),
            new ContributionNeed("Prismatic Tapers", 7, "Prismatic Peas"),
        ];
        var summary = new ContributionSummary(true, true, low, NeedsSplittingTool: true);

        string reply = DefaultReplies.Contribute(summary);

        // Every shape now also carries OpenTradeInvite's own sentence, a fixed cost on top of the
        // already-bounded reagent list — still worth a ceiling so this cannot grow without bound.
        Assert.True(reply.Length <= 255, $"contribute reply is {reply.Length} chars: {reply}");
    }

    // -- Direct gives -----------------------------------------------------------------------------

    [Fact]
    public void DirectGiveRefusedNamesTheLiveWantedItemsDescription()
    {
        Assert.Equal(
            "Please open a trade with me instead; I'll take casting reagents or trade notes of 10,000+.",
            DefaultReplies.DirectGiveRefused(
                DonationPolicy.WantedItemsDescription(botHoldsSplittingTool: true)));
        Assert.Equal(
            "Please open a trade with me instead; I'll take casting reagents, Splitting Tools, "
                + "or trade notes of 10,000+.",
            DefaultReplies.DirectGiveRefused(
                DonationPolicy.WantedItemsDescription(botHoldsSplittingTool: false)));
    }
}
