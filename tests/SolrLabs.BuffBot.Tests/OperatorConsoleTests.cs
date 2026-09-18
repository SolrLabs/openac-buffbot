using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Tests.Timing;

namespace SolrLabs.BuffBot.Tests;

public sealed class OperatorConsoleTests
{
    private const string SenderName = "Archer";
    private const uint SenderId = 1;

    [Fact]
    public void InspectReportsEnabledQueueDepthAndTellsAnswered()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(SenderId, SenderName, DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(2, "Other", DefaultSpellSets.Buff), out _);
        var console = new OperatorConsole(queue, NewGuard());

        string report = console.Inspect(enabled: true, tellsAnswered: 7);

        Assert.Equal(
            $"BuffBot: enabled for this character, 2 waiting, muted: none, 7 tell(s) answered.",
            report);
    }

    [Fact]
    public void InspectReportsDisabled()
    {
        var console = new OperatorConsole(new RequestQueue(5), NewGuard());

        string report = console.Inspect(enabled: false, tellsAnswered: 0);

        Assert.StartsWith("BuffBot: disabled for this character, ", report);
    }

    [Fact]
    public void InspectNamesEveryCurrentlyMutedSender()
    {
        var guard = NewGuard();
        Mute(guard, SenderId, SenderName);
        var console = new OperatorConsole(new RequestQueue(5), guard);

        string report = console.Inspect(enabled: true, tellsAnswered: 0);

        Assert.Contains($"muted: {SenderName}", report);
    }

    [Fact]
    public void UnwedgeDrainsEveryStillWaitingRequest()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(1, "First", DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(2, "Second", DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(3, "Third", DefaultSpellSets.Buff), out _);
        var console = new OperatorConsole(queue, NewGuard());

        string result = console.Unwedge();

        Assert.Equal(0, queue.WaitingCount);
        Assert.Equal(QueueStanding.NotQueued, queue.TryGetStanding(1, out _));
        Assert.Equal(QueueStanding.NotQueued, queue.TryGetStanding(2, out _));
        Assert.Equal(QueueStanding.NotQueued, queue.TryGetStanding(3, out _));
        Assert.Equal("Unwedged: cleared 3 from the queue, unmuted 0 (0 automatic, 0 manual).", result);
    }

    [Fact]
    public void UnwedgeNeverTouchesTheRequestAlreadyBeingCastOn()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(1, "Active", DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(2, "Waiting", DefaultSpellSets.Buff), out _);
        queue.TryDequeue(out _); // "Active" is now the one BuffCoordinator is casting on
        var console = new OperatorConsole(queue, NewGuard());

        string result = console.Unwedge();

        Assert.Equal("Unwedged: cleared 1 from the queue, unmuted 0 (0 automatic, 0 manual).", result);
        Assert.Equal(QueueStanding.Active, queue.TryGetStanding(1, out _));
        Assert.Equal(QueueStanding.NotQueued, queue.TryGetStanding(2, out _));
    }

    [Fact]
    public void UnwedgeLiftsEveryActiveMute()
    {
        var guard = NewGuard();
        Mute(guard, SenderId, SenderName);
        Mute(guard, 2, "Other");
        var console = new OperatorConsole(new RequestQueue(5), guard);

        string result = console.Unwedge();

        Assert.Equal("Unwedged: cleared 0 from the queue, unmuted 2 (2 automatic, 0 manual).", result);
        Assert.Empty(guard.MutedSenderNames());
    }

    [Fact]
    public void AfterUnwedgeAMutedSenderIsAnsweredAgain()
    {
        var guard = NewGuard();
        Mute(guard, SenderId, SenderName);
        Assert.Null(guard.Admit(SenderId, SenderName, "anything", isUnresolvedReply: false));
        var console = new OperatorConsole(new RequestQueue(5), guard);

        console.Unwedge();

        Assert.NotNull(guard.Admit(SenderId, SenderName, "anything else", isUnresolvedReply: false));
    }

    /// <summary>Trips the circuit breaker the same way three real replies would.</summary>
    private static void Mute(LoopGuard guard, uint senderId, string senderName)
    {
        const string repeated = "Stopped: no wand.";
        guard.Admit(senderId, senderName, repeated, isUnresolvedReply: false);
        guard.Admit(senderId, senderName, repeated, isUnresolvedReply: false);
        guard.Admit(senderId, senderName, repeated, isUnresolvedReply: false);
    }

    private static LoopGuard NewGuard() => new(new FakeClock(), _ => { }, _ => { });
}
