using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Portals;

namespace SolrLabs.BuffBot.Vocabulary;

/// <summary>No reply text appears as a literal anywhere else in this codebase.</summary>
internal static class DefaultReplies
{
    internal const string Unresolved =
        "I didn't understand that. Send help to see what I know.";

    internal const string PaidNotAvailable = "Paid access isn't available yet.";

    internal const string FellowshipNotAvailable =
        "Fellowship access isn't available yet.";

    internal const string AlreadyQueued = "You're already in line, hang tight.";

    /// <summary>Their place in line doesn't change, only which profile runs.</summary>
    internal static string ReplacedQueuedRequest(string newSetName, string oldSetName) =>
        $"Sure, I'll buff {newSetName} instead of {oldSetName}.";

    internal const string AlreadyBeingServed =
        "Too late, I'm already casting on you. Say cancel if you'd like me to stop.";

    // The run stops once the cast already in flight resolves, not immediately.
    internal const string StoppingAfterThisCast = "Okay, I'll stop after this cast.";

    internal const string QueueFull = "Too many people are waiting right now. Try again shortly.";

    // Never sent to the one being cast on; a drain doesn't touch the run in flight.
    internal const string QueueDrained = "The queue was just cleared. Ask again if you still want a buff.";

    internal const string OutOfRange = "You're too far away. Come closer and ask again.";

    // Nothing is enqueued; status, help, position and cancel still work.
    internal const string IntakePaused = "I'm not taking new requests right now. Try again shortly.";

    internal const string Starting = "On it.";

    internal const string StartingWithSelfBuffs = "On it. Buffing up first.";

    internal const string StartingWithManaTopUp = "On it. Topping up mana first.";

    internal const string Pausing =
        "I'm pausing replies to you for a bit: too many, too fast. I'll listen again in 10 minutes.";

    /// <summary>Sent as a system message, never a tell, by <c>/buffbot on</c>.</summary>
    internal const string Enabled = "BuffBot is on for this character.";

    /// <summary>Sent as a system message, never a tell, by <c>/buffbot off</c>.</summary>
    internal const string Disabled = "BuffBot is off for this character.";

    // Posted as a system message, never a tell.
    internal static string Inspect(
        bool enabled, int waitingCount, IReadOnlyList<string> mutedSenders, int tellsAnswered)
    {
        string muted = mutedSenders.Count == 0 ? "none" : string.Join(", ", mutedSenders);
        return $"BuffBot: {(enabled ? "enabled" : "disabled")} for this character, "
            + $"{waitingCount} waiting, muted: {muted}, {tellsAnswered} tell(s) answered.";
    }

    // Posted as a system message, never a tell.
    // Unwedge lifts both automatic and manual mutes.
    internal static string Unwedged(int clearedFromQueue, MuteClearResult unmuted) =>
        $"Unwedged: cleared {clearedFromQueue} from the queue, unmuted {unmuted.Total} "
            + $"({unmuted.Automatic} automatic, {unmuted.Manual} manual).";

    internal static string Help(VocabularyTable vocabulary, bool portalsOffered = false)
    {
        var segments = new List<string> { "Happy to help!" };

        List<string> buffPhrases = CanonicalPhrasesFor(vocabulary, IsBuffRequest);
        if (buffPhrases.Count > 0)
            segments.Add($"Ask me for a buff: {string.Join(", ", buffPhrases)}.");

        List<string> queuePhrases = CanonicalPhrasesFor(vocabulary, IsQueueCommand);
        if (queuePhrases.Count > 0)
            segments.Add($"While you're waiting: {string.Join(", ", queuePhrases)}.");

        string? statusPhrase = CanonicalPhraseFor(vocabulary, Intent.Status);
        if (statusPhrase is not null)
            segments.Add($"Send {statusPhrase} any time to see how busy I am.");

        string? contributePhrase = CanonicalPhraseFor(vocabulary, Intent.Contribute);
        if (contributePhrase is not null)
            segments.Add($"Send {contributePhrase} to hear what I'm low on.");

        if (portalsOffered)
        {
            List<string> portalPhrases = CanonicalPhrasesFor(vocabulary, IsPortalCommand);
            if (portalPhrases.Count > 0)
                segments.Add($"Portals: {string.Join(", ", portalPhrases)}.");
        }

        return string.Join(" ", segments);
    }

    private static bool IsBuffRequest(Intent intent) => intent switch
    {
        Intent.Buffs or Intent.Prots or Intent.Heavy or Intent.Light or Intent.Finesse
            or Intent.Missile or Intent.Void or Intent.Mage or Intent.TwoHanded or Intent.Dual
            or Intent.Tink or Intent.Trades => true,
        _ => false,
    };

    private static bool IsQueueCommand(Intent intent) => intent switch
    {
        Intent.Position or Intent.Cancel => true,
        _ => false,
    };

    private static bool IsPortalCommand(Intent intent) => intent switch
    {
        Intent.Where or Intent.PortalPrimary or Intent.PortalSecondary => true,
        _ => false,
    };

    private static List<string> CanonicalPhrasesFor(VocabularyTable vocabulary, Func<Intent, bool> include)
    {
        var seen = new HashSet<Intent>();
        var phrases = new List<string>();
        foreach (string phrase in vocabulary.Phrases)
        {
            if (vocabulary.TryResolve(phrase, out Intent intent) && include(intent) && seen.Add(intent))
                phrases.Add(phrase);
        }
        return phrases;
    }

    private static string? CanonicalPhraseFor(VocabularyTable vocabulary, Intent intent)
    {
        foreach (string phrase in vocabulary.Phrases)
        {
            if (vocabulary.TryResolve(phrase, out Intent resolved) && resolved == intent)
                return phrase;
        }
        return null;
    }

    // -- Portals --------------------------------------------------------------------------------

    internal static string Where(string? primary, string? secondary)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(primary)) parts.Add($"Primary: {primary.Trim().TrimEnd('.')}.");
        if (!string.IsNullOrWhiteSpace(secondary)) parts.Add($"Secondary: {secondary.Trim().TrimEnd('.')}.");
        return parts.Count == 0 ? PortalNotOffered : string.Join(" ", parts);
    }

    internal static string SummoningPortal(string description) =>
        $"Summoning a portal to {description.Trim().TrimEnd('.')}.";

    internal static string NotTied(PortalTieSlot slot) =>
        $"I'm not tied to a {slot.ToString().ToLowerInvariant()} portal right now.";

    internal const string CouldNotSummon = "I couldn't summon that portal.";

    internal const string CantSummonYet = "I can't summon portals yet.";

    internal const string PortalNotOffered = "I'm not offering portals right now.";

    internal const string PortalTimedOut = "Something went wrong summoning the portal.";

    internal static string Status(string version, int tellsAnswered) =>
        $"Still here: {tellsAnswered} tell(s) answered so far, running BuffBot {version}.";

    // Posted as a system message, never a tell — by ConsoleLinkAnnouncer and /buffbot console.
    internal static string WebConsoleAnnouncement(string link) => $"BuffBot web console: {link}";

    // Posted as a system message by /buffbot console when the mesh has no link yet.
    internal const string WebConsoleNotUpYet = "The web console isn't up yet.";

    // Posted as a system message, never a tell.
    internal static string Status(string version, int tellsAnswered, bool enabled) =>
        $"BuffBot {version}: {(enabled ? "enabled" : "disabled")} for this character, "
            + $"{tellsAnswered} tell(s) answered.";

    internal static string Queued(int aheadCount) =>
        aheadCount == 1
            ? "Queued, 1 person ahead of you."
            : $"Queued, {aheadCount} people ahead of you.";

    // 0 reads as "you're next", not a bare count.
    internal static string Position(int aheadCount) =>
        aheadCount switch
        {
            0 => "In line: you're next.",
            1 => "In line: 1 ahead of you.",
            _ => $"In line: {aheadCount} ahead of you.",
        };

    internal const string BeingServedNow = "I'm buffing you right now.";

    // Shared by Position and Cancel; same fact either way.
    internal const string NotInLine = "You're not in line.";

    internal const string RemovedFromLine = "You're out of the line.";

    internal static string UnknownSpellLine(string line) =>
        $"Something's misconfigured on my end for \"{line}\". Sorry about that.";

    internal static string NothingLearned(string line) =>
        $"I haven't learned {line}.";

    // Names the whole profile; naming one line would read as though the rest were fine.
    internal static string NothingLearnedInProfile(string setName) =>
        $"I haven't learned anything for {setName} yet.";

    // AlreadyFullyBuffed is unreachable here; it only fires for the idle self-buff catch-up.
    // componentCeilingRung is non-null only when a step accepted a lower tier for lack of components.
    internal static string ClosingReply(IReadOnlyList<CastStep> steps, int? componentCeilingRung = null)
    {
        if (steps.Count == 0)
            return NothingToCast;

        int cast = 0;
        int alreadyUp = 0;
        foreach (CastStep step in steps)
        {
            if (step.Outcome == CastOutcome.Cast)
                cast++;
            else
                alreadyUp++;
        }

        if (cast == 0)
            return AlreadyFullyBuffed;

        string castPart = cast == 1 ? "1 buff" : $"{cast} buffs";
        string reply = alreadyUp == 0
            ? $"All set: cast {castPart}."
            : $"All set: cast {castPart}, {alreadyUp} already up.";
        return componentCeilingRung is { } rung ? $"{reply} {ComponentTierReducedNote(rung)}" : reply;
    }

    private const string AlreadyFullyBuffed = "You're already fully buffed, nothing new to cast.";

    private const string NothingToCast = "There was nothing for me to cast.";

    private const int MaxNamedMisses = 3;

    internal static string ClosingPartial(IReadOnlyList<CastStep> steps, int? componentCeilingRung = null)
    {
        int cast = 0;
        int alreadyUp = 0;
        int noShield = 0;
        var missed = new List<string>();
        var unlearned = new List<string>();
        foreach (CastStep step in steps)
        {
            switch (step.Outcome)
            {
                case CastOutcome.Cast:
                    cast++;
                    break;
                case CastOutcome.Skipped:
                    alreadyUp++;
                    break;
                case CastOutcome.Failed when step.Failure!.Value.Kind == CastFailureKind.NotLearned:
                    unlearned.Add(step.Line);
                    break;
                // Baned steps are counted, not named.
                case CastOutcome.Failed when step.Failure!.Value.Kind == CastFailureKind.NoShield:
                    noShield++;
                    break;
                case CastOutcome.Failed:
                    missed.Add($"{step.Line} ({ShortFailureReason(step.Failure!.Value)})");
                    break;
            }
        }

        string reply = $"I cast what I could: buffed {cast}, {alreadyUp} already up";
        if (missed.Count > 0)
        {
            reply += $", but missed {missed.Count}: {string.Join(", ", missed.Take(MaxNamedMisses))}";
            if (missed.Count > MaxNamedMisses)
                reply += $" and {missed.Count - MaxNamedMisses} more";
        }
        reply += ".";
        if (unlearned.Count > MaxNamedMisses)
            reply += $" I haven't learned {unlearned.Count} of those yet.";
        else if (unlearned.Count > 0)
            reply += $" I haven't learned {string.Join(", ", unlearned)} yet.";
        if (NoShieldNote(noShield) is { } noShieldNote)
            reply += $" {noShieldNote}";
        if (cast > 0 && componentCeilingRung is { } rung)
            reply += $" {ComponentTierReducedNote(rung)}";
        return reply;
    }

    // Null when nothing was skipped.
    private static string? NoShieldNote(int noShieldCount) =>
        noShieldCount == 0
            ? null
            : $"No shield equipped, so I skipped {noShieldCount} bane{(noShieldCount == 1 ? string.Empty : "s")}.";

    private static int CountNoShield(IReadOnlyList<CastStep> steps)
    {
        int count = 0;
        foreach (CastStep step in steps)
            if (step.Outcome == CastOutcome.Failed && step.Failure!.Value.Kind == CastFailureKind.NoShield)
                count++;
        return count;
    }

    // Rung count, not absolute tier: lines don't share a top tier.
    private static string ComponentTierReducedNote(int rungsDown) =>
        rungsDown == 1
            ? "I'm short on higher-level components, so I cast one tier down."
            : $"I'm short on higher-level components, so I cast {rungsDown} tiers down.";

    // steps is read only for MissingComponents; every other fatal kind reports nothing else.
    internal static string ClosingFailure(CastFailure failure, IReadOnlyList<CastStep>? steps = null)
    {
        if (failure.Kind == CastFailureKind.MissingComponents)
        {
            if (steps is not { Count: > 0 })
                return OutOfComponents;

            int cast = 0;
            foreach (CastStep step in steps)
                if (step.Outcome == CastOutcome.Cast)
                    cast++;

            // Nothing landed, so the "buffed 0" sentence is dropped; only the shortage note remains.
            if (cast == 0)
            {
                string? noShieldNote = NoShieldNote(CountNoShield(steps));
                return noShieldNote is null ? OutOfComponents : $"{noShieldNote} {OutOfComponents}";
            }

            return $"{ClosingPartial(steps)} {OutOfComponents}";
        }

        string reason = failure.Kind switch
        {
            CastFailureKind.GateRefused => $"couldn't cast {failure.Line} ({failure.Detail})",
            CastFailureKind.RequestRefused => $"couldn't send {failure.Line} ({failure.Detail})",
            CastFailureKind.CastFailed => $"{failure.Line} failed ({failure.Detail})",
            CastFailureKind.TimedOut => $"gave up on {failure.Line}, you were busy too long",
            CastFailureKind.NoWand => "I don't have a wand to cast with",
            CastFailureKind.WieldFailed => $"couldn't wield a wand ({failure.Detail})",
            CastFailureKind.ModeFailed => $"couldn't get into casting stance ({failure.Detail})",
            CastFailureKind.DidNotLand => $"{failure.Line} doesn't seem to have landed",
            // Never fatal; unreachable by a real run, kept so the switch is exhaustive.
            CastFailureKind.Fizzled => $"{failure.Line} {failure.Detail}",
            CastFailureKind.OutOfMana => failure.Detail,
            CastFailureKind.TooManyFailures => failure.Detail,
            CastFailureKind.NotLearned => $"haven't learned {failure.Line}",
            CastFailureKind.NoShield => "no shield equipped",
            _ => $"couldn't finish {failure.Line}",
        };
        return $"I had to stop: {reason}.";
    }

    private const string OutOfComponents = "I'm out of spell components, so I had to stop.";

    private const string ThanksForAsking = "Thanks for asking!";

    internal const string ContributeInventoryUnavailable = "I can't check my supplies right now, sorry.";

    internal const string ContributeCatalogUnavailableToolOk =
        "I can't check my scarabs or tapers right now, but my Splitting Tool is fine.";

    internal const string ContributeCatalogUnavailableNeedTool =
        "I can't check my scarabs or tapers right now, but I could use a Splitting Tool.";

    internal const string ContributeWellStocked =
        ThanksForAsking + " I'm well stocked on scarabs, tapers and my splitting tool right now.";

    internal const string ContributeSplittingToolOnly =
        ThanksForAsking + " I could use a Splitting Tool.";

    private const string AlsoNeedSplittingTool = " I could also use a Splitting Tool.";

    private const int MaxNamedShortages = 3;

    internal static string Contribute(ContributionSummary summary) =>
        $"{ContributeBody(summary)} {OpenTradeInvite(summary)}";

    private static string ContributeBody(ContributionSummary summary)
    {
        if (!summary.InventoryReadable)
            return ContributeInventoryUnavailable;

        if (!summary.ReagentCatalogAvailable)
            return summary.NeedsSplittingTool
                ? ContributeCatalogUnavailableNeedTool
                : ContributeCatalogUnavailableToolOk;

        if (summary.LowReagents.Count == 0)
            return summary.NeedsSplittingTool ? ContributeSplittingToolOnly : ContributeWellStocked;

        var named = new List<string>();
        var peas = new List<string>();
        int shown = Math.Min(summary.LowReagents.Count, MaxNamedShortages);
        for (int i = 0; i < shown; i++)
        {
            ContributionNeed need = summary.LowReagents[i];
            named.Add($"{need.Name} ({need.Stock})");
            if (need.PeaAlternative is { } pea && !peas.Contains(pea))
                peas.Add(pea);
        }

        int remaining = summary.LowReagents.Count - shown;
        string namedList = remaining > 0
            ? string.Join(", ", named) + (remaining == 1 ? " and 1 more" : $" and {remaining} more")
            : JoinNaturally(named, "and");

        string reply = $"{ThanksForAsking} I'm low on {namedList}";
        reply += peas.Count > 0 ? $"; {JoinPeaAlternatives(peas)} work too." : ".";
        if (summary.NeedsSplittingTool)
            reply += AlsoNeedSplittingTool;
        return reply;
    }

    // Kept short: every reply here must stay under the 255-character tell limit.
    private static string OpenTradeInvite(ContributionSummary summary) =>
        "Open a trade with me while I'm not casting.";

    // No Oxford comma before the final two.
    private static string JoinNaturally(IReadOnlyList<string> items, string conjunction) =>
        items.Count switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} {conjunction} {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))} {conjunction} {items[^1]}",
        };

    private const string PeaSuffix = " Peas";

    // "Pyreal or Prismatic Peas", not "Pyreal Peas or Prismatic Peas": drop the shared trailing
    // "Peas" from every name but the last.
    private static string JoinPeaAlternatives(IReadOnlyList<string> peaNames)
    {
        if (peaNames.Count <= 1)
            return JoinNaturally(peaNames, "or");

        var shortened = new List<string>(peaNames.Count);
        for (int i = 0; i < peaNames.Count - 1; i++)
        {
            string name = peaNames[i];
            shortened.Add(name.EndsWith(PeaSuffix, StringComparison.Ordinal)
                ? name[..^PeaSuffix.Length]
                : name);
        }
        shortened.Add(peaNames[^1]);
        return JoinNaturally(shortened, "or");
    }

    internal const string GiftKeepThanks = "Thank you! I will put this to good use.";

    // ACE already refuses the direct give; this just explains why to open a trade instead.
    internal static string DirectGiveRefused(string wantedItemsDescription) =>
        $"Please open a trade with me instead; I'll take {wantedItemsDescription}.";

    // Sent on the trade's own "Trade Complete!" message.
    internal const string DonationThanks = GiftKeepThanks;

    // wantedItemsDescription excludes Splitting Tools once the bot already holds one.
    internal static string DonationUnwanted(IReadOnlyList<string> unwantedNames, string wantedItemsDescription) =>
        $"I can't use {JoinNaturally(unwantedNames, "and")}. Please add only {wantedItemsDescription}.";

    // Sent after 90s with nothing newly staged and no accept.
    internal const string DonationStallClosing = "Closing the trade - open it again when you're ready.";

    // Sent after the third inventory-space refusal in one trade.
    internal const string DonationNoSpace = "I can't carry that right now.";

    internal const string TakingABreak = "Sorry folks, need a short break.";

    private const string StoppedAsRequested = "Stopped, like you asked.";

    internal static string ClosingStopped(RunStopReason reason) =>
        reason == RunStopReason.BotDisabling ? TakingABreak : StoppedAsRequested;

    // Short form: ClosingPartial already names the spell, so this must not repeat it.
    private static string ShortFailureReason(CastFailure failure) => failure.Kind switch
    {
        CastFailureKind.GateRefused => failure.Detail,
        CastFailureKind.RequestRefused => failure.Detail,
        CastFailureKind.CastFailed => failure.Detail,
        CastFailureKind.TimedOut => "busy too long",
        CastFailureKind.DidNotLand => "didn't land",
        // Detail here is already short ("fizzled N times"); other kinds' Detail is a raw server reason.
        CastFailureKind.Fizzled => failure.Detail,
        CastFailureKind.NotLearned => "not learned",
        CastFailureKind.NoShield => "no shield equipped",
        _ => failure.Detail,
    };

    // Glob patterns (* = the variable part), matched to drop a two-bot exchange rather than answer it.
    // Enabled/Disabled are excluded: they only go out as a system message, never a tell.
    internal static readonly IReadOnlyList<string> BotShapePatterns =
    [
        Unresolved,
        PaidNotAvailable,
        FellowshipNotAvailable,
        AlreadyQueued,
        AlreadyBeingServed,
        StoppingAfterThisCast,
        QueueFull,
        QueueDrained,
        OutOfRange,
        IntakePaused,
        Starting,
        StartingWithSelfBuffs,
        StartingWithManaTopUp,
        Pausing,
        BeingServedNow,
        NotInLine,
        RemovedFromLine,
        "Sure, I'll buff * instead of *.",
        // Trailing * covers the "Portals: *." sentence Help appends when a tie is offered.
        "Happy to help! Ask me for a buff: *. While you're waiting: *. Send * any time to see how busy I am. "
            + "Send * to hear what I'm low on.*",
        "Still here: *.",
        "Queued, * ahead of you.",
        "In line: *",
        "Something's misconfigured on my end for *. Sorry about that.",
        "I haven't learned *.",
        NothingToCast,
        AlreadyFullyBuffed,
        "All set: *.",
        "I cast what I could: *.",
        "I had to stop: *.",
        OutOfComponents,
        // The nothing-landed shape of ClosingFailure, when a no-shield note precedes it.
        "No shield equipped, so I skipped * bane*. I'm out of spell components, so I had to stop.",
        TakingABreak,
        StoppedAsRequested,
        GiftKeepThanks,
        // Each ends with OpenTradeInvite's own sentence; the wildcard covers that suffix.
        ContributeInventoryUnavailable + " *",
        ContributeCatalogUnavailableToolOk + " *",
        ContributeCatalogUnavailableNeedTool + " *",
        ContributeWellStocked + " *",
        ContributeSplittingToolOnly + " *",
        ThanksForAsking + " I'm low on *.",
        "I can't use *. Please add only *.",
        DonationStallClosing,
        DonationNoSpace,
        "Please open a trade with me instead; I'll take *.",
        "Primary: *.",
        "Secondary: *.",
        PortalNotOffered,
        "Summoning a portal to *.",
        "I'm not tied to a * portal right now.",
        CouldNotSummon,
        CantSummonYet,
        PortalTimedOut,
    ];
}
