using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

/// <summary>Host-free, fake-clock-driven coverage of the loop guard's four rules (a)-(d).</summary>
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

    // a donation reply never trips the breaker -----------------------------------------------

    [Fact]
    public void RepeatedDonationReplyNeverTripsTheBreakerOrMutesTheSender()
    {
        var guard = NewGuard(out _, out List<string> warns);
        const string repeated = "Thank you! I will put this to good use.";

        for (int i = 0; i < LoopGuard.RepeatThreshold * 5; i++)
        {
            string? admitted = guard.Admit(
                Sender, SenderName, repeated, isUnresolvedReply: false, countsAsRequest: false,
                countsTowardMuteTrigger: false);
            Assert.Equal(repeated, admitted); // never swapped for the "pausing" tell
        }

        Assert.Empty(guard.MutedSenderNames());
        Assert.Empty(warns);
        Assert.NotNull(guard.Admit(Sender, SenderName, "anything else", isUnresolvedReply: false));
    }

    [Fact]
    public void ADonationReplyToAnAlreadyMutedSenderIsStillSilenced()
    {
        var guard = NewGuard(out _, out _);
        const string unrelated = "Stopped: no wand.";
        guard.Admit(Sender, SenderName, unrelated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, unrelated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, unrelated, isUnresolvedReply: false); // trips the breaker

        string? donationReply = guard.Admit(
            Sender, SenderName, "Thank you! I will put this to good use.", isUnresolvedReply: false,
            countsAsRequest: false, countsTowardMuteTrigger: false);

        Assert.Null(donationReply); // still muted: the exemption only means a donation reply cannot mute, not that it bypasses one
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
    public void SameReplyThreeTimesWithinTheRepeatWindowMutesTheSenderAndSendsOnePausingTell()
    {
        var guard = NewGuard(out _, out List<string> warns);
        const string repeated = "Stopped: no wand.";

        Assert.Equal(repeated, guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false));
        Assert.Equal(repeated, guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false));
        string? third = guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);

        Assert.Equal(DefaultReplies.Pausing, third);
        Assert.Single(warns);
    }

    [Fact]
    public void MutedSenderGetsNothingUntilTheMuteExpires()
    {
        var clock = new FakeClock();
        var guard = new LoopGuard(clock, _ => { }, _ => { });
        const string repeated = "Stopped: no wand.";
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false); // trips the breaker

        Assert.Null(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));

        clock.Advance(LoopGuard.MuteDuration + TimeSpan.FromSeconds(1));

        Assert.NotNull(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
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
        const string repeated = "Stopped: no wand.";
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false); // trips the breaker

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
        const string repeated = "Stopped: no wand.";
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false); // trips the breaker
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
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false); // trips the breaker
        guard.ClearMutes();

        // Two more of the same reply, right after clearing, must not re-trip on their own — the
        // repeat tally that got them muted the first time was reset, not carried forward.
        Assert.Equal(repeated, guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false));
        Assert.Equal(repeated, guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false));

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

        // Well past LoopGuard.MuteDuration, the automatic mute's own expiry -- a manual mute has
        // no expiry to reach, by design: a judgement, not a rate-limit cooldown.
        clock.Advance(LoopGuard.MuteDuration + LoopGuard.MuteDuration);

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
        const string repeated = "Stopped: no wand.";
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false);
        guard.Admit(Sender, SenderName, repeated, isUnresolvedReply: false); // trips the breaker

        MutedEntry entry = Assert.Single(guard.MutedEntries());

        Assert.False(entry.IsManual);
        Assert.Equal(clock.UtcNow + LoopGuard.MuteDuration, entry.Until);
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
        guard.Admit(2, "Auto", repeated, isUnresolvedReply: false);
        guard.Admit(2, "Auto", repeated, isUnresolvedReply: false);
        guard.Admit(2, "Auto", repeated, isUnresolvedReply: false);

        MuteClearResult result = guard.ClearMutes();

        Assert.Equal(1, result.Automatic);
        Assert.Equal(1, result.Manual);
        Assert.Equal(2, result.Total);
        Assert.Empty(guard.MutedEntries());
        Assert.NotNull(guard.Admit(Sender, SenderName, "anything", isUnresolvedReply: false));
        Assert.NotNull(guard.Admit(2, "Auto", "anything", isUnresolvedReply: false));
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
