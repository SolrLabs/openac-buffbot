using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

/// <summary>Host-free, fake-clock-driven coverage of the loop guard's rules (a)-(e).</summary>
public sealed class LoopGuardTests
{
    private const uint Sender = 42;
    private const string SenderName = "Archer";

    // (a) ---------------------------------------------------------------------------------

    [Fact]
    public void BotShapedTextIsDropped()
    {
        var guard = NewGuard(out _, out _);

        bool dropped = guard.IsBotShapedAndShouldDrop(Sender, SenderName, DefaultReplies.Starting);

        Assert.True(dropped);
    }

    [Fact]
    public void OrdinaryTextIsNotDropped()
    {
        var guard = NewGuard(out _, out _);

        bool dropped = guard.IsBotShapedAndShouldDrop(Sender, SenderName, "buff");

        Assert.False(dropped);
    }

    [Fact]
    public void BotShapedTemplateIsRecognisedWithItsVariablePartWildcarded()
    {
        var guard = NewGuard(out _, out _);

        bool dropped = guard.IsBotShapedAndShouldDrop(Sender, SenderName, "All set: cast Strength Other.");

        Assert.True(dropped);
    }

    [Fact]
    public void BotShapedDropLogsInfoOncePerMinutePerSender()
    {
        var guard = NewGuard(out List<string> infos, out _);

        guard.IsBotShapedAndShouldDrop(Sender, SenderName, DefaultReplies.Starting);
        guard.IsBotShapedAndShouldDrop(Sender, SenderName, DefaultReplies.Starting);
        guard.IsBotShapedAndShouldDrop(Sender, SenderName, DefaultReplies.Starting);

        Assert.Single(infos);
        Assert.Equal($"ignored bot-shaped tell from {SenderName}", infos[0]);
    }

    [Fact]
    public void BotShapedDropLogsAgainAfterTheSuppressWindowPasses()
    {
        var clock = new FakeClock();
        var infos = new List<string>();
        var guard = new LoopGuard(clock, infos.Add, _ => { });

        guard.IsBotShapedAndShouldDrop(Sender, SenderName, DefaultReplies.Starting);
        clock.Advance(LoopGuard.LogSuppressWindow + TimeSpan.FromSeconds(1));
        guard.IsBotShapedAndShouldDrop(Sender, SenderName, DefaultReplies.Starting);

        Assert.Equal(2, infos.Count);
    }

    // (b) ---------------------------------------------------------------------------------

    [Fact]
    public void ThirteenthRequestWithinTheWindowIsDropped()
    {
        var guard = NewGuard(out _, out List<string> warns);

        for (int i = 0; i < LoopGuard.MaxRequestsPerWindow; i++)
            Assert.NotNull(guard.Admit(Sender, SenderName, $"reply-{i}", isUnresolvedReply: false));

        string? thirteenth = guard.Admit(Sender, SenderName, "reply-overflow", isUnresolvedReply: false);

        Assert.Null(thirteenth);
        Assert.Single(warns);
        Assert.Equal($"rate limit exceeded for {SenderName}", warns[0]);
    }

    [Fact]
    public void RateLimitedSenderCanReplyAgainOnceTheWindowRollsOff()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });

        for (int i = 0; i < LoopGuard.MaxRequestsPerWindow; i++)
            guard.Admit(Sender, SenderName, $"reply-{i}", isUnresolvedReply: false);
        Assert.Null(guard.Admit(Sender, SenderName, "still-limited", isUnresolvedReply: false));

        clock.Advance(LoopGuard.RateLimitWindow + TimeSpan.FromSeconds(1));

        Assert.NotNull(guard.Admit(Sender, SenderName, "reply-after-window", isUnresolvedReply: false));
    }

    [Fact]
    public void ClosingReplyOfAnAlreadyAdmittedRequestIsExemptFromTheCount()
    {
        var guard = NewGuard(out _, out List<string> warns);

        for (int i = 0; i < LoopGuard.MaxRequestsPerWindow; i++)
            Assert.NotNull(guard.Admit(Sender, SenderName, $"opening-{i}", isUnresolvedReply: false));

        // The tally is already at the cap, but a closing reply tied to one of those already-
        // admitted requests does not draw on it, so it still goes out.
        string? closing = guard.Admit(
            Sender, SenderName, "closing", isUnresolvedReply: false, countsAsRequest: false);
        Assert.NotNull(closing);

        // A genuinely new request is still refused.
        Assert.Null(guard.Admit(Sender, SenderName, "one-more-request", isUnresolvedReply: false));
        Assert.Single(warns);
    }

    // a reply with no request behind it never trips the breaker -------------------------------

    [Fact]
    public void AReplyWithNoRequestBehindItNeverTripsTheBreakerOrMutesTheSender()
    {
        var guard = NewGuard(out _, out List<string> warns);
        const string repeated = "Thank you! I will put this to good use.";

        for (int i = 0; i < LoopGuard.RepeatThreshold * 5; i++)
        {
            string? admitted = guard.Admit(
                Sender, SenderName, repeated, isUnresolvedReply: false, countsAsRequest: false);
            Assert.Equal(repeated, admitted); // never swapped for the "pausing" tell
        }

        Assert.Empty(guard.MutedSenderNames());
        Assert.Empty(warns);
        Assert.NotNull(guard.Admit(Sender, SenderName, "anything else", isUnresolvedReply: false));
    }

    [Fact]
    public void ARequestlessReplyToAnAlreadyMutedSenderIsStillSilenced()
    {
        var guard = NewGuard(out _, out _);
        const string repeated = "Stopped: no wand.";
        Trip(guard, repeated);

        string? closingReply = guard.Admit(
            Sender, SenderName, "Thank you! I will put this to good use.", isUnresolvedReply: false,
            countsAsRequest: false);

        Assert.Null(closingReply); // a requestless reply still doesn't bypass an existing mute
    }

    // the exact live scenario: different asks that happen to answer the same way --------------

    [Fact]
    public void ThreeDifferentRequestsThatAllProduceTheSameReplyDoNotMuteTheSender()
    {
        var guard = NewGuard(out _, out List<string> warns);
        const string sameReply = "I'm not offering portals right now.";

        Assert.Equal(sameReply, guard.Admit(
            Sender, SenderName, sameReply, isUnresolvedReply: false, requestText: "whereto"));
        Assert.Equal(sameReply, guard.Admit(
            Sender, SenderName, sameReply, isUnresolvedReply: false, requestText: "where"));
        Assert.Equal(sameReply, guard.Admit(
            Sender, SenderName, sameReply, isUnresolvedReply: false, requestText: "primary"));

        Assert.Empty(warns);
        Assert.Empty(guard.MutedSenderNames());
    }

    // a settings-driven rate limit ------------------------------------------------------------

    [Fact]
    public void SetMaxRequestsPerWindowLowersTheLimitForSubsequentRequests()
    {
        var guard = NewGuard(out _, out List<string> warns);
        guard.SetMaxRequestsPerWindow(2);

        Assert.NotNull(guard.Admit(Sender, SenderName, "reply-0", isUnresolvedReply: false));
        Assert.NotNull(guard.Admit(Sender, SenderName, "reply-1", isUnresolvedReply: false));
        string? third = guard.Admit(Sender, SenderName, "reply-2", isUnresolvedReply: false);

        Assert.Null(third);
        Assert.Single(warns);
    }

    [Fact]
    public void SetMaxRequestsPerWindowRaisingTheLimitAllowsMoreThanTheOldDefault()
    {
        var guard = NewGuard(out _, out _);
        guard.SetMaxRequestsPerWindow(20);

        for (int i = 0; i < 20; i++)
            Assert.NotNull(guard.Admit(Sender, SenderName, $"reply-{i}", isUnresolvedReply: false));

        Assert.Null(guard.Admit(Sender, SenderName, "reply-overflow", isUnresolvedReply: false));
    }

    // (c) ---------------------------------------------------------------------------------

    [Fact]
    public void FiveIdenticalRequestsWithinTheRepeatWindowMutesTheSenderAndSendsThePausingTell()
    {
        var guard = NewGuard(out _, out List<string> warns);
        const string repeated = "Stopped: no wand.";

        for (int i = 0; i < LoopGuard.RepeatThreshold - 1; i++)
        {
            Assert.Equal(repeated, guard.Admit(
                Sender, SenderName, repeated, isUnresolvedReply: false, requestText: repeated));
        }
        string? last = guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false, requestText: repeated);

        Assert.Equal(DefaultReplies.Pausing(LoopGuard.FirstMuteDuration), last);
        Assert.Single(warns);
    }

    [Fact]
    public void RequestTextComparisonIsTrimmedCaseInsensitiveAndCollapsesInnerWhitespace()
    {
        var guard = NewGuard(out _, out List<string> warns);
        const string reply = "Okay.";

        for (int i = 0; i < LoopGuard.RepeatThreshold - 1; i++)
            guard.Admit(Sender, SenderName, reply, isUnresolvedReply: false, requestText: "  Where   to  ");
        string? last = guard.Admit(Sender, SenderName, reply, isUnresolvedReply: false, requestText: "WHERE TO");

        Assert.Equal(DefaultReplies.Pausing(LoopGuard.FirstMuteDuration), last);
        Assert.Single(warns);
    }

    [Fact]
    public void MutedSenderGetsNothingUntilTheMuteExpires()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        const string repeated = "Stopped: no wand.";
        Trip(guard, repeated);

        Assert.Null(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));

        clock.Advance(LoopGuard.FirstMuteDuration + TimeSpan.FromSeconds(1));

        Assert.NotNull(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
    }

    // (e) escalating mute -------------------------------------------------------------------

    [Fact]
    public void SuccessiveStrikesEscalateOneTwoFourThenHoldAtFour()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        const string repeated = "Stopped: no wand.";

        (string? firstReply, TimeSpan first) = TripBreaker(guard, clock, repeated);
        Assert.Equal(LoopGuard.FirstMuteDuration, first);
        Assert.Equal(DefaultReplies.Pausing(first), firstReply);
        clock.Advance(first + TimeSpan.FromSeconds(1));

        (string? secondReply, TimeSpan second) = TripBreaker(guard, clock, repeated);
        Assert.Equal(LoopGuard.SecondMuteDuration, second);
        Assert.Equal(DefaultReplies.Pausing(second), secondReply);
        clock.Advance(second + TimeSpan.FromSeconds(1));

        (string? thirdReply, TimeSpan third) = TripBreaker(guard, clock, repeated);
        Assert.Equal(LoopGuard.MaxMuteDuration, third);
        Assert.Equal(DefaultReplies.Pausing(third), thirdReply);
        clock.Advance(third + TimeSpan.FromSeconds(1));

        (string? fourthReply, TimeSpan fourth) = TripBreaker(guard, clock, repeated);
        Assert.Equal(LoopGuard.MaxMuteDuration, fourth); // caps here, never doubles again
        Assert.Equal(DefaultReplies.Pausing(fourth), fourthReply);
    }

    [Fact]
    public void EscalationResetsToTheFirstStrikeTwentyFourHoursAfterTheLastOne()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        const string repeated = "Stopped: no wand.";

        (_, TimeSpan first) = TripBreaker(guard, clock, repeated);
        Assert.Equal(LoopGuard.FirstMuteDuration, first);
        clock.Advance(LoopGuard.EscalationResetWindow + TimeSpan.FromSeconds(1));

        (_, TimeSpan afterReset) = TripBreaker(guard, clock, repeated);

        Assert.Equal(LoopGuard.FirstMuteDuration, afterReset);
    }

    [Fact]
    public void UnmuteResetsTheEscalationLadderSoTheNextStrikeStartsOver()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        const string repeated = "Stopped: no wand.";
        TripBreaker(guard, clock, repeated);

        guard.Unmute(Sender);
        (_, TimeSpan afterUnmute) = TripBreaker(guard, clock, repeated);

        Assert.Equal(LoopGuard.FirstMuteDuration, afterUnmute);
    }

    [Fact]
    public void ClearMutesResetsTheEscalationLadderSoTheNextStrikeStartsOver()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        const string repeated = "Stopped: no wand.";
        TripBreaker(guard, clock, repeated);

        guard.ClearMutes();
        (_, TimeSpan afterClear) = TripBreaker(guard, clock, repeated);

        Assert.Equal(LoopGuard.FirstMuteDuration, afterClear);
    }

    // (d) ---------------------------------------------------------------------------------

    [Fact]
    public void SecondUnresolvedReplyWithinTheWindowIsDropped()
    {
        var guard = NewGuard(out _, out _);

        Assert.NotNull(guard.Admit(Sender, SenderName, DefaultReplies.Unresolved, isUnresolvedReply: true));
        string? second = guard.Admit(Sender, SenderName, DefaultReplies.Unresolved, isUnresolvedReply: true);

        Assert.Null(second);
    }

    [Fact]
    public void UnresolvedReplyIsAllowedAgainAfterTheWindowPasses()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        guard.Admit(Sender, SenderName, DefaultReplies.Unresolved, isUnresolvedReply: true);

        clock.Advance(LoopGuard.UnresolvedReplyWindow + TimeSpan.FromSeconds(1));

        Assert.NotNull(guard.Admit(Sender, SenderName, DefaultReplies.Unresolved, isUnresolvedReply: true));
    }

    [Fact]
    public void UnresolvedDedupeDoesNotBlockADifferentSender()
    {
        var guard = NewGuard(out _, out _);
        guard.Admit(Sender, SenderName, DefaultReplies.Unresolved, isUnresolvedReply: true);

        string? other = guard.Admit(99, "Other", DefaultReplies.Unresolved, isUnresolvedReply: true);

        Assert.NotNull(other);
    }

    // Operator surface (Guard.OperatorConsole) -----------------------------------------------

    [Fact]
    public void MutedSenderNamesIsEmptyWhenNobodyIsMuted()
    {
        var guard = NewGuard(out _, out _);

        Assert.Empty(guard.MutedSenderNames());
    }

    [Fact]
    public void MutedSenderNamesNamesAMutedSenderByTheNameLastSeenOnThem()
    {
        var guard = NewGuard(out _, out _);
        Trip(guard, "Stopped: no wand.");

        Assert.Equal([SenderName], guard.MutedSenderNames());
    }

    [Fact]
    public void ClearMutesReturnsZeroWhenNobodyIsMuted()
    {
        var guard = NewGuard(out _, out _);

        Assert.Equal(0, guard.ClearMutes().Total);
    }

    [Fact]
    public void ClearMutesLiftsTheMuteAndAnUnmutedSenderIsAnsweredAgain()
    {
        var guard = NewGuard(out _, out _);
        Trip(guard, "Stopped: no wand.");
        Assert.Null(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));

        MuteClearResult cleared = guard.ClearMutes();

        Assert.Equal(1, cleared.Total);
        Assert.Equal(1, cleared.Automatic);
        Assert.Equal(0, cleared.Manual);
        Assert.Empty(guard.MutedSenderNames());
        Assert.NotNull(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
    }

    [Fact]
    public void ClearMutesDoesNotImmediatelyRetripTheBreakerFromAStaleRepeatTally()
    {
        var guard = NewGuard(out _, out List<string> warns);
        const string repeated = "Stopped: no wand.";
        Trip(guard, repeated);
        guard.ClearMutes();

        // More of the same request, right after clearing, must not re-trip on their own -- the
        // repeat tally that got them muted the first time was reset, not carried forward.
        for (int i = 0; i < LoopGuard.RepeatThreshold - 1; i++)
        {
            Assert.Equal(repeated, guard.Admit(
                Sender, SenderName, repeated, isUnresolvedReply: false, requestText: repeated));
        }

        Assert.Single(warns); // only the original mute, not a second one
    }

    // -- Manual mute (operator panel's Mute/Release, operator-panel-unit-1) -----------------

    [Fact]
    public void ManualMuteSilencesTheSenderImmediately()
    {
        var guard = NewGuard(out _, out _);

        guard.Mute(Sender, SenderName);

        Assert.Null(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
    }

    [Fact]
    public void ManualMuteNeverLapsesWhereAnAutomaticOneDoes()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        guard.Mute(Sender, SenderName);

        // Well past any automatic mute's own expiry -- a manual mute has no expiry to reach,
        // by design: a judgement, not a rate-limit cooldown.
        clock.Advance(LoopGuard.EscalationResetWindow + LoopGuard.EscalationResetWindow);

        Assert.Null(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
    }

    [Fact]
    public void ManualMuteAppearsInMutedEntriesAsIndefinite()
    {
        var guard = NewGuard(out _, out _);
        guard.Mute(Sender, SenderName);

        MutedEntry entry = Assert.Single(guard.MutedEntries());

        Assert.Equal(Sender, entry.ObjectId);
        Assert.Equal(SenderName, entry.Name);
        Assert.True(entry.IsManual);
        Assert.Null(entry.Until); // indefinite -- the panel renders something other than a date
    }

    [Fact]
    public void MutedEntriesDistinguishesAnAutomaticMuteWithItsExpiry()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        DateTimeOffset before = clock.UtcNow;
        TripBreaker(guard, clock, "Stopped: no wand.");

        MutedEntry entry = Assert.Single(guard.MutedEntries());

        Assert.False(entry.IsManual);
        Assert.Equal(before + LoopGuard.FirstMuteDuration, entry.Until);
    }

    [Fact]
    public void UnmuteReleasesOnlyTheNamedSender()
    {
        var guard = NewGuard(out _, out _);
        guard.Mute(Sender, SenderName);
        guard.Mute(2, "Other");

        guard.Unmute(Sender);

        Assert.NotNull(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
        Assert.Null(guard.Admit(2, "Other", "anything", isUnresolvedReply: false));
    }

    [Fact]
    public void UnmuteOnAStrangerIsANoOp()
    {
        var guard = NewGuard(out _, out _);

        guard.Unmute(999); // never muted -- must not throw

        Assert.Empty(guard.MutedEntries());
    }

    [Fact]
    public void ClearMutesLiftsBothKindsAndReportsTheCountOfEach()
    {
        var guard = NewGuard(out _, out _);
        guard.Mute(Sender, SenderName); // manual
        const string repeated = "Stopped: no wand."; // trips the circuit breaker -- automatic
        for (int i = 0; i < LoopGuard.RepeatThreshold; i++)
            guard.Admit(2, "Auto", repeated, isUnresolvedReply: false, requestText: repeated);

        MuteClearResult result = guard.ClearMutes();

        Assert.Equal(1, result.Automatic);
        Assert.Equal(1, result.Manual);
        Assert.Equal(2, result.Total);
        Assert.Empty(guard.MutedEntries());
        Assert.NotNull(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
        Assert.NotNull(guard.Admit(2, "Auto", "anything", isUnresolvedReply: false));
    }

    /// <summary>Trips the circuit breaker with <see cref="LoopGuard.RepeatThreshold"/> repeats
    /// of the same request text.</summary>
    private static void Trip(LoopGuard guard, string repeated)
    {
        for (int i = 0; i < LoopGuard.RepeatThreshold; i++)
            guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false, requestText: repeated);
    }

    /// <summary>Like <see cref="Trip"/>, but also reports the last reply and how long the
    /// resulting mute lasts, so a test can check the ladder's step.</summary>
    private static (string? Reply, TimeSpan Duration) TripBreaker(LoopGuard guard, FakeClock clock, string repeated)
    {
        DateTimeOffset before = clock.UtcNow;
        string? last = null;
        for (int i = 0; i < LoopGuard.RepeatThreshold; i++)
            last = guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false, requestText: repeated);
        MutedEntry entry = Assert.Single(guard.MutedEntries());
        return (last, entry.Until!.Value - before);
    }

    private static LoopGuard NewGuard(out List<string> infos, out List<string> warns)
    {
        var capturedInfos = new List<string>();
        var capturedWarns = new List<string>();
        var guard = new LoopGuard(new FakeClock(), capturedInfos.Add, capturedWarns.Add);
        infos = capturedInfos;
        warns = capturedWarns;
        return guard;
    }
}
