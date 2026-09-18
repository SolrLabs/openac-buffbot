using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Policy;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

public sealed class ResponderTests
{
    private const string Version = "9.9.9";

    [Fact]
    public void HelpIntentListsThePhrasesTheTableKnows()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "help"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.Help(DefaultVocabulary.Table)}", reply);
    }

    [Fact]
    public void StatusIntentReportsVersionAndTellsAnswered()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "status"), tellsAnswered: 42);

        Assert.Equal($"/tell Archer, {DefaultReplies.Status(Version, 42)}", reply);
    }

    [Fact]
    public void FirstBuffsIntentStartsImmediately()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "buffs"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.Starting}", reply);
    }

    [Fact]
    public void SecondRequesterIsQueuedBehindTheFirst()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);

        string? reply = responder.Reply(Tell("Other", "buff", senderObjectId: 2), tellsAnswered: 2);

        Assert.Equal($"/tell Other, {DefaultReplies.Queued(1)}", reply);
    }

    [Fact]
    public void SecondTellFromTheSameRequesterWhileQueuedIsRefused()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);

        string? reply = responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 2);

        Assert.Equal($"/tell Archer, {DefaultReplies.AlreadyQueued}", reply);
    }

    /// <summary>A second, different request from someone already waiting replaces their queued
    /// request rather than being refused, and keeps their place in line.</summary>
    [Fact]
    public void SecondDifferentRequestWhileWaitingReplacesTheQueuedOneAndKeepsPlaceInLine()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(DefaultVocabulary.Table, Free(), queue, Version, NewGuard());
        responder.Reply(Tell("First", "buff", senderObjectId: 1), tellsAnswered: 1);
        responder.Reply(Tell("Archer", "light", senderObjectId: 2), tellsAnswered: 2);

        string? reply = responder.Reply(Tell("Archer", "heavy", senderObjectId: 2), tellsAnswered: 3);

        Assert.Equal($"/tell Archer, {DefaultReplies.ReplacedQueuedRequest(DefaultSpellSets.Heavy, DefaultSpellSets.Light)}", reply);
        Assert.True(queue.TryDequeue(out BuffRequest first));
        Assert.True(queue.TryDequeue(out BuffRequest second));
        Assert.Equal("First", first.RequesterName); // place in line unchanged
        Assert.Equal("Archer", second.RequesterName);
        Assert.Equal(DefaultSpellSets.Heavy, second.SetName); // but the chain itself updated
    }

    /// <summary>While being served, a second request — not a cancel — still gets a "too late"
    /// reply, pointing at cancel rather than doing nothing.</summary>
    [Fact]
    public void SecondRequestWhileActiveIsRefusedAndPointsAtCancel()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(DefaultVocabulary.Table, Free(), queue, Version, NewGuard());
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);
        queue.TryDequeue(out _); // now active

        string? reply = responder.Reply(Tell("Archer", "heavy", senderObjectId: 1), tellsAnswered: 2);

        Assert.Equal($"/tell Archer, {DefaultReplies.AlreadyBeingServed}", reply);
        Assert.Equal(QueueStanding.Active, queue.TryGetStanding(1, out _));
    }

    // -- refuse before acking ----------------------------------------------------------------

    /// <summary>A request known up front to resolve to nothing learned is refused without an
    /// "On it." ack first, and never reaches the queue.</summary>
    [Fact]
    public void ARequestKnownToHaveNothingLearnedIsRefusedWithoutAnAckFirst()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), queue, Version, NewGuard(),
            nothingLearnedFor: _ => true);

        string? reply = responder.Reply(Tell("Archer", "buff"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.NothingLearnedInProfile(DefaultSpellSets.Buff)}", reply);
        Assert.Equal(0, queue.WaitingCount);
    }

    [Fact]
    public void QueueFullRefusesANewRequester()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(1), Version, NewGuard());
        responder.Reply(Tell("First", "buff", senderObjectId: 1), tellsAnswered: 1);
        responder.Reply(Tell("Second", "buff", senderObjectId: 2), tellsAnswered: 2);

        string? reply = responder.Reply(Tell("Third", "buff", senderObjectId: 3), tellsAnswered: 3);

        Assert.Equal($"/tell Third, {DefaultReplies.QueueFull}", reply);
    }

    [Fact]
    public void UnknownTextGetsTheDidNotUnderstandReply()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "do a backflip"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.Unresolved}", reply);
    }

    /// <summary>Console verbs like these are never entries in <see cref="DefaultVocabulary"/>,
    /// so guessing one over a tell reads as any other unknown phrase.</summary>
    [Theory]
    [InlineData("inspect")]
    [InlineData("unwedge")]
    [InlineData("buffbot inspect")]
    [InlineData("buffbot unwedge")]
    [InlineData("/buffbot inspect")]
    [InlineData("inv")]
    [InlineData("inv list")]
    [InlineData("inv use 0x1234")]
    [InlineData("buffbot inv list")]
    [InlineData("logout")]
    [InlineData("buffbot logout")]
    [InlineData("chatdump")]
    [InlineData("chatdump 10")]
    public void GuessingAnOperatorVerbOverATellGetsTheOrdinaryUnresolvedReply(string guess)
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", guess), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.Unresolved}", reply);
    }

    [Fact]
    public void AdminPrefixedSenderIsAddressedVerbatim()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("+Archer", "help"), tellsAnswered: 1);

        Assert.NotNull(reply);
        Assert.StartsWith("/tell +Archer, ", reply);
    }

    [Fact]
    public void PaidModeRefusesEvenAKnownPhrase() => AssertRefuses(AccessMode.Paid);

    [Fact]
    public void FellowshipModeRefusesEvenAKnownPhrase() => AssertRefuses(AccessMode.Fellowship);

    private static void AssertRefuses(AccessMode mode)
    {
        var responder = new Responder(DefaultVocabulary.Table, new AccessPolicy(mode), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "buffs"), tellsAnswered: 1);

        Assert.NotNull(reply);
        Assert.NotEqual($"/tell Archer, {DefaultReplies.Starting}", reply);
        Assert.StartsWith("/tell Archer, ", reply);
    }

    [Fact]
    public void StartingAckMentionsBuffingUpFirstWhenSelfCastsAreDue()
    {
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard(),
            selfCastsDue: () => true);

        string? reply = responder.Reply(Tell("Archer", "buff"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.StartingWithSelfBuffs}", reply);
    }

    [Fact]
    public void StartingAckIsPlainWhenNoSelfCastsAreDue()
    {
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard(),
            selfCastsDue: () => false);

        string? reply = responder.Reply(Tell("Archer", "buff"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.Starting}", reply);
    }

    [Fact]
    public void StartingAckMentionsToppingUpManaWhenTheCoordinatorWouldTopUpBeforeThisRequest()
    {
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard(),
            selfCastsDue: () => false, manaTopUpDue: () => true);

        string? reply = responder.Reply(Tell("Archer", "buff"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.StartingWithManaTopUp}", reply);
    }

    [Fact]
    public void SelfCastsDueTakesPriorityOverManaTopUpInTheStartingAck()
    {
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard(),
            selfCastsDue: () => true, manaTopUpDue: () => true);

        string? reply = responder.Reply(Tell("Archer", "buff"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.StartingWithSelfBuffs}", reply);
    }

    [Fact]
    public void QueuedRepliesNeverMentionSelfBuffsEvenWhenTheyAreDue()
    {
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard(),
            selfCastsDue: () => true);
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);

        string? reply = responder.Reply(Tell("Other", "buff", senderObjectId: 2), tellsAnswered: 2);

        Assert.Equal($"/tell Other, {DefaultReplies.Queued(1)}", reply);
    }

    [Fact]
    public void FreePolicyNeverRefuses()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "buffs"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.Starting}", reply);
    }

    /// <summary>Help is grouped by <see cref="Intent"/>, not typed out a second time,
    /// so a table gaining a phrase changes the text with no code change here.</summary>
    [Fact]
    public void HelpTextChangesWhenThePhraseTableGainsAPhraseForANewIntent()
    {
        var smallTable = new VocabularyTable([("buffs", Intent.Buffs)]);
        var largerTable = new VocabularyTable([("buffs", Intent.Buffs), ("prots", Intent.Prots)]);

        string smallHelp = DefaultReplies.Help(smallTable);
        string largerHelp = DefaultReplies.Help(largerTable);

        Assert.NotEqual(smallHelp, largerHelp);
        Assert.Contains("prots", largerHelp);
    }

    /// <summary>An alias — a second phrase for an intent already shown — does not
    /// repeat the same fact under a second name.</summary>
    [Fact]
    public void HelpTextShowsOnlyTheFirstDeclaredPhraseForAnAliasedIntent()
    {
        var table = new VocabularyTable([("zap", Intent.Buffs), ("zzz", Intent.Buffs)]);

        string help = DefaultReplies.Help(table);

        Assert.Contains("zap", help);
        Assert.DoesNotContain("zzz", help);
    }

    [Theory]
    [InlineData("prots", DefaultSpellSets.Prots)]
    [InlineData("heavy", DefaultSpellSets.Heavy)]
    [InlineData("light", DefaultSpellSets.Light)]
    [InlineData("finesse", DefaultSpellSets.Finesse)]
    [InlineData("missile", DefaultSpellSets.Missile)]
    [InlineData("void", DefaultSpellSets.Void)]
    [InlineData("mage", DefaultSpellSets.Mage)]
    [InlineData("2h", DefaultSpellSets.TwoHanded)]
    [InlineData("dual", DefaultSpellSets.Dual)]
    [InlineData("buff", DefaultSpellSets.Buff)]
    public void EachArchetypeKeywordEnqueuesItsOwnProfile(string phrase, string expectedSetName)
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(DefaultVocabulary.Table, Free(), queue, Version, NewGuard());

        responder.Reply(Tell("Archer", phrase), tellsAnswered: 1);

        Assert.True(queue.TryDequeue(out BuffRequest request));
        Assert.Equal(expectedSetName, request.SetName);
    }

    [Fact]
    public void AnEnqueuedRequestIsRecordedOnStatsByRequesterAndArchetypeKeywordNeverRawText()
    {
        var stats = new SolrLabs.BuffBot.Stats.BotStats();
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard(), stats: stats);

        responder.Reply(Tell("Archer", "heavy"), tellsAnswered: 1);

        SolrLabs.BuffBot.Stats.RecentEvent entry = Assert.Single(stats.Snapshot().Recent);
        Assert.Equal(SolrLabs.BuffBot.Stats.RecentEventKind.RequestAccepted, entry.Kind);
        Assert.Equal("Archer", entry.Who);
        Assert.Equal(DefaultSpellSets.Heavy, entry.Archetype);
    }

    [Fact]
    public void PositionForAStrangerToTheQueueSaysSo()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "position"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.NotInLine}", reply);
    }

    [Fact]
    public void PositionForTheHeadOfTheLineSaysNext()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(DefaultVocabulary.Table, Free(), queue, Version, NewGuard());
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);

        string? reply = responder.Reply(Tell("Archer", "line", senderObjectId: 1), tellsAnswered: 2);

        Assert.Equal($"/tell Archer, {DefaultReplies.Position(0)}", reply);
    }

    [Fact]
    public void PositionCountsOnlyThoseAhead()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(DefaultVocabulary.Table, Free(), queue, Version, NewGuard());
        responder.Reply(Tell("First", "buff", senderObjectId: 1), tellsAnswered: 1);
        responder.Reply(Tell("Second", "buff", senderObjectId: 2), tellsAnswered: 2);

        string? reply = responder.Reply(Tell("Second", "position", senderObjectId: 2), tellsAnswered: 3);

        Assert.Equal($"/tell Second, {DefaultReplies.Position(1)}", reply);
    }

    [Fact]
    public void PositionForTheActiveRecipientSaysBeingServedNow()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(DefaultVocabulary.Table, Free(), queue, Version, NewGuard());
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);
        queue.TryDequeue(out _); // now active, as BuffCoordinator would leave it mid-cast

        string? reply = responder.Reply(Tell("Archer", "position", senderObjectId: 1), tellsAnswered: 2);

        Assert.Equal($"/tell Archer, {DefaultReplies.BeingServedNow}", reply);
    }

    [Fact]
    public void CancelForAStrangerToTheQueueSaysSo()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", "cancel"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.NotInLine}", reply);
    }

    [Fact]
    public void CancelRemovesAWaitingRequesterAndFreesThemToAskAgain()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(DefaultVocabulary.Table, Free(), queue, Version, NewGuard());
        responder.Reply(Tell("First", "buff", senderObjectId: 1), tellsAnswered: 1);
        responder.Reply(Tell("Second", "buff", senderObjectId: 2), tellsAnswered: 2);

        string? reply = responder.Reply(Tell("Second", "remove", senderObjectId: 2), tellsAnswered: 3);

        Assert.Equal($"/tell Second, {DefaultReplies.RemovedFromLine}", reply);
        // Cancelled, so a fresh request from the same sender is a new enqueue, not AlreadyQueued.
        string? again = responder.Reply(Tell("Second", "buff", senderObjectId: 2), tellsAnswered: 4);
        Assert.Equal($"/tell Second, {DefaultReplies.Queued(1)}", again);
    }

    /// <summary>Cancel from the active recipient asks the coordinator to stop the run
    /// after the cast already in the air resolves, via <c>stopActiveRun</c>.</summary>
    [Fact]
    public void CancelAsksTheActiveRunToStopRatherThanRefusingOutright()
    {
        var queue = new RequestQueue(5);
        uint? stoppedFor = null;
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), queue, Version, NewGuard(),
            stopActiveRun: id =>
            {
                stoppedFor = id;
                return true;
            });
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);
        queue.TryDequeue(out _); // now active

        string? reply = responder.Reply(Tell("Archer", "cancel", senderObjectId: 1), tellsAnswered: 2);

        Assert.Equal($"/tell Archer, {DefaultReplies.StoppingAfterThisCast}", reply);
        Assert.Equal(1u, stoppedFor);
    }

    /// <summary>A race where the coordinator no longer finds this requester active falls
    /// back to the same reply rather than a false promise that the run will stop.</summary>
    [Fact]
    public void CancelFallsBackToAlreadyBeingServedWhenTheCoordinatorCannotFindTheRunAnymore()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), queue, Version, NewGuard(), stopActiveRun: _ => false);
        responder.Reply(Tell("Archer", "buff", senderObjectId: 1), tellsAnswered: 1);
        queue.TryDequeue(out _); // now active

        string? reply = responder.Reply(Tell("Archer", "cancel", senderObjectId: 1), tellsAnswered: 2);

        Assert.Equal($"/tell Archer, {DefaultReplies.AlreadyBeingServed}", reply);
    }

    [Fact]
    public void BotShapedIncomingTellIsDroppedRatherThanAnswered()
    {
        var responder = new Responder(DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard());

        string? reply = responder.Reply(Tell("Archer", DefaultReplies.Starting), tellsAnswered: 1);

        Assert.Null(reply);
    }

    // -- Pause intake ---------------------------------------------------------------------------

    [Fact]
    public void PausedIntakeAnswersANewBuffRequestWithoutQueuingIt()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), queue, Version, NewGuard(), intakePaused: () => true);

        string? reply = responder.Reply(Tell("Archer", "buff"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.IntakePaused}", reply);
        Assert.Equal(0, queue.WaitingCount);
    }

    [Fact]
    public void PausedIntakeStillAnswersStatusHelpPositionAndCancel()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), queue, Version, NewGuard(), intakePaused: () => true);

        Assert.Equal(
            $"/tell Archer, {DefaultReplies.Help(DefaultVocabulary.Table)}",
            responder.Reply(Tell("Archer", "help"), tellsAnswered: 1));
        Assert.Equal(
            $"/tell Archer, {DefaultReplies.Status(Version, 2)}",
            responder.Reply(Tell("Archer", "status"), tellsAnswered: 2));
        Assert.Equal(
            $"/tell Archer, {DefaultReplies.NotInLine}",
            responder.Reply(Tell("Archer", "position"), tellsAnswered: 3));
        Assert.Equal(
            $"/tell Archer, {DefaultReplies.NotInLine}",
            responder.Reply(Tell("Archer", "cancel"), tellsAnswered: 4));
    }

    [Fact]
    public void UnpausedIntakeEnqueuesNormally()
    {
        var queue = new RequestQueue(5);
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), queue, Version, NewGuard(), intakePaused: () => false);

        string? reply = responder.Reply(Tell("Archer", "buff"), tellsAnswered: 1);

        Assert.Equal($"/tell Archer, {DefaultReplies.Starting}", reply);
        Assert.Equal(1, queue.WaitingCount);
    }

    // The `contribute` keyword ----------------------------------------------------------------

    [Fact]
    public void ContributeIsRecognisedByTheVocabularyAndAnsweredAtOnce()
    {
        var summary = new ContributionSummary(true, true, Array.Empty<ContributionNeed>(), false);
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, NewGuard(),
            contributionSummary: () => summary);

        string? reply = responder.Reply(Tell("Archer", "contribute"), tellsAnswered: 1);

        Assert.Equal(
            $"/tell Archer, {DefaultReplies.ContributeWellStocked} "
                + "Open a trade with me while I'm not casting.",
            reply);
    }

    [Fact]
    public void ContributeGoesThroughTheSameLoopGuardAsEveryOtherReply()
    {
        var summary = new ContributionSummary(true, true, Array.Empty<ContributionNeed>(), false);
        var guard = NewGuard();
        var responder = new Responder(
            DefaultVocabulary.Table, Free(), new RequestQueue(5), Version, guard,
            contributionSummary: () => summary);

        responder.Reply(Tell("Archer", "contribute"), tellsAnswered: 1);
        responder.Reply(Tell("Archer", "contribute"), tellsAnswered: 2);
        string? third = responder.Reply(Tell("Archer", "contribute"), tellsAnswered: 3);

        Assert.Equal($"/tell Archer, {DefaultReplies.Pausing}", third);
    }

    private static AccessPolicy Free() => new(AccessMode.Free);

    private static LoopGuard NewGuard() => new(new FakeClock(), _ => { }, _ => { });

    private static PluginChatMessage Tell(string sender, string text, uint senderObjectId = 1234) =>
        new(
            Sequence: 1,
            SenderObjectId: senderObjectId,
            Kind: 3,
            Sender: sender,
            Text: text,
            ChannelName: string.Empty);
}
