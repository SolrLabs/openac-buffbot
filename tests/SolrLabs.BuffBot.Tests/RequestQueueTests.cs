using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Tests.Timing;

namespace SolrLabs.BuffBot.Tests;

public sealed class RequestQueueTests
{
    [Fact]
    public void DequeuesInTheOrderTheyWereEnqueued()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "First"), out _);
        queue.TryEnqueue(Request(2, "Second"), out _);

        Assert.True(queue.TryDequeue(out BuffRequest first));
        Assert.True(queue.TryDequeue(out BuffRequest second));
        Assert.Equal("First", first.RequesterName);
        Assert.Equal("Second", second.RequesterName);
    }

    [Fact]
    public void ReportsHowManyAreAheadWhenEnqueuing()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "First"), out int firstAhead);
        queue.TryEnqueue(Request(2, "Second"), out int secondAhead);

        Assert.Equal(0, firstAhead);
        Assert.Equal(1, secondAhead);
    }

    [Fact]
    public void RefusesASecondRequestFromTheSameRequesterWhileWaiting()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);

        EnqueueResult result = queue.TryEnqueue(Request(1, "Archer"), out _);

        Assert.Equal(EnqueueResult.AlreadyQueued, result);
    }

    [Fact]
    public void RefusesASecondRequestFromTheSameRequesterWhileBeingProcessed()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);
        queue.TryDequeue(out _); // now "being processed", not waiting

        EnqueueResult result = queue.TryEnqueue(Request(1, "Archer"), out _);

        Assert.Equal(EnqueueResult.AlreadyQueued, result);
    }

    [Fact]
    public void AllowsARequesterBackInOnceTheirRequestCompletes()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);
        queue.TryDequeue(out _);
        queue.Complete(1);

        EnqueueResult result = queue.TryEnqueue(Request(1, "Archer"), out _);

        Assert.Equal(EnqueueResult.Enqueued, result);
    }

    [Fact]
    public void RefusesBeyondCapacity()
    {
        var queue = new RequestQueue(2);
        queue.TryEnqueue(Request(1, "A"), out _);
        queue.TryEnqueue(Request(2, "B"), out _);

        EnqueueResult result = queue.TryEnqueue(Request(3, "C"), out _);

        Assert.Equal(EnqueueResult.QueueFull, result);
    }

    // -- TryGetStanding (position query) -------------------------------------------------------

    [Fact]
    public void StandingIsNotQueuedForAStrangerToTheQueue()
    {
        var queue = new RequestQueue(5);

        QueueStanding standing = queue.TryGetStanding(999, out int aheadCount);

        Assert.Equal(QueueStanding.NotQueued, standing);
        Assert.Equal(0, aheadCount);
    }

    [Fact]
    public void StandingIsWaitingWithZeroAheadForTheHeadOfTheLine()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);

        QueueStanding standing = queue.TryGetStanding(1, out int aheadCount);

        Assert.Equal(QueueStanding.Waiting, standing);
        Assert.Equal(0, aheadCount);
    }

    [Fact]
    public void StandingCountsOnlyThoseAheadNotTheWholeLine()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "First"), out _);
        queue.TryEnqueue(Request(2, "Second"), out _);
        queue.TryEnqueue(Request(3, "Third"), out _);

        QueueStanding standing = queue.TryGetStanding(3, out int aheadCount);

        Assert.Equal(QueueStanding.Waiting, standing);
        Assert.Equal(2, aheadCount);
    }

    [Fact]
    public void StandingIsActiveOnceDequeuedButNotYetComplete()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);
        queue.TryDequeue(out _);

        QueueStanding standing = queue.TryGetStanding(1, out _);

        Assert.Equal(QueueStanding.Active, standing);
    }

    [Fact]
    public void StandingReturnsToNotQueuedOnceComplete()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);
        queue.TryDequeue(out _);
        queue.Complete(1);

        QueueStanding standing = queue.TryGetStanding(1, out _);

        Assert.Equal(QueueStanding.NotQueued, standing);
    }

    // -- TryCancel --------------------------------------------------------------------------

    [Fact]
    public void CancelRemovesAWaitingRequesterAndFreesTheirSlot()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);

        bool cancelled = queue.TryCancel(1);

        Assert.True(cancelled);
        Assert.Equal(QueueStanding.NotQueued, queue.TryGetStanding(1, out _));
        Assert.Equal(EnqueueResult.Enqueued, queue.TryEnqueue(Request(1, "Archer"), out _));
    }

    [Fact]
    public void CancelPreservesTheOrderOfThoseStillWaiting()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "First"), out _);
        queue.TryEnqueue(Request(2, "Second"), out _);
        queue.TryEnqueue(Request(3, "Third"), out _);

        queue.TryCancel(2);

        Assert.True(queue.TryDequeue(out BuffRequest first));
        Assert.True(queue.TryDequeue(out BuffRequest second));
        Assert.Equal("First", first.RequesterName);
        Assert.Equal("Third", second.RequesterName);
    }

    [Fact]
    public void CancelFailsForAStrangerToTheQueue()
    {
        var queue = new RequestQueue(5);

        bool cancelled = queue.TryCancel(999);

        Assert.False(cancelled);
    }

    [Fact]
    public void CancelDoesNotRemoveTheActiveRecipient()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);
        queue.TryDequeue(out _); // now active, not waiting

        bool cancelled = queue.TryCancel(1);

        Assert.False(cancelled);
        Assert.Equal(QueueStanding.Active, queue.TryGetStanding(1, out _));
    }

    // -- TryGetWaiting / TryReplaceWaiting -----------------------------------------------------

    [Fact]
    public void TryGetWaitingFindsAStillWaitingRequestersOwnRequest()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(1, "Archer", "heavy"), out _);

        bool found = queue.TryGetWaiting(1, out BuffRequest request);

        Assert.True(found);
        Assert.Equal("heavy", request.SetName);
    }

    [Fact]
    public void TryGetWaitingFailsForAStrangerOrTheActiveRecipient()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(1, "Archer", "heavy"), out _);
        queue.TryDequeue(out _); // now active, not waiting

        Assert.False(queue.TryGetWaiting(1, out _));
        Assert.False(queue.TryGetWaiting(999, out _));
    }

    [Fact]
    public void TryReplaceWaitingKeepsPlaceInLineWithTheNewRequest()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "First"), out _);
        queue.TryEnqueue(new BuffRequest(2, "Archer", "bow"), out _);
        queue.TryEnqueue(Request(3, "Third"), out _);

        string? previous = queue.TryReplaceWaiting(new BuffRequest(2, "Archer", "heavy"));

        Assert.Equal("bow", previous);
        Assert.True(queue.TryDequeue(out BuffRequest first));
        Assert.True(queue.TryDequeue(out BuffRequest second));
        Assert.True(queue.TryDequeue(out BuffRequest third));
        Assert.Equal("First", first.RequesterName);
        Assert.Equal("Archer", second.RequesterName); // place in line unchanged
        Assert.Equal("heavy", second.SetName); // the chain itself updated
        Assert.Equal("Third", third.RequesterName);
    }

    [Fact]
    public void TryReplaceWaitingReturnsNullForARequesterNotCurrentlyWaiting() =>
        Assert.Null(new RequestQueue(5).TryReplaceWaiting(new BuffRequest(1, "Archer", "heavy")));

    [Fact]
    public void TryReplaceWaitingLeavesTheWaitTimestampUntouched()
    {
        var clock = new FakeClock();
        var queue = new RequestQueue(5, clock);
        queue.TryEnqueue(new BuffRequest(1, "Archer", "bow"), out _);
        clock.Advance(TimeSpan.FromSeconds(30));

        queue.TryReplaceWaiting(new BuffRequest(1, "Archer", "heavy"));

        QueueEntry entry = Assert.Single(queue.Waiting());
        Assert.Equal(30d, entry.WaitingSeconds, precision: 3); // never left the line, so the wait keeps counting
    }

    // -- Waiting (operator-panel-unit-1's QueueEntry read surface) ---------------------------

    [Fact]
    public void WaitingReportsEachEntrysNameArchetypeAndObjectId()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(1, "Archer", "heavy"), out _);

        QueueEntry entry = Assert.Single(queue.Waiting());

        Assert.Equal(1u, entry.ObjectId);
        Assert.Equal("Archer", entry.Name);
        Assert.Equal("heavy", entry.Archetype);
    }

    [Fact]
    public void WaitingSecondsGrowsWithTheClockSinceEnqueuing()
    {
        var clock = new FakeClock();
        var queue = new RequestQueue(5, clock);
        queue.TryEnqueue(Request(1, "Archer"), out _);

        clock.Advance(TimeSpan.FromSeconds(42));

        QueueEntry entry = Assert.Single(queue.Waiting());
        Assert.Equal(42d, entry.WaitingSeconds, precision: 3);
    }

    [Fact]
    public void WaitingNeverIncludesTheOneAlreadyDequeuedAndBeingCastOn()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Active"), out _);
        queue.TryEnqueue(Request(2, "Waiting"), out _);
        queue.TryDequeue(out _); // "Active" is now being cast on, not waiting

        QueueEntry entry = Assert.Single(queue.Waiting());
        Assert.Equal("Waiting", entry.Name);
    }

    // Drain queue -----------------------------------------------------------------------------

    [Fact]
    public void DrainWaitingRemovesEveryoneWaitingInLineOrder()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "First"), out _);
        queue.TryEnqueue(Request(2, "Second"), out _);

        IReadOnlyList<BuffRequest> drained = queue.DrainWaiting();

        Assert.Equal(["First", "Second"], drained.Select(r => r.RequesterName));
        Assert.Empty(queue.Waiting());
    }

    [Fact]
    public void DrainWaitingNeverTouchesTheOneAlreadyBeingCastOn()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Active"), out _);
        queue.TryEnqueue(Request(2, "Waiting"), out _);
        queue.TryDequeue(out _); // "Active" is now being cast on, not waiting

        IReadOnlyList<BuffRequest> drained = queue.DrainWaiting();

        Assert.Equal(["Waiting"], drained.Select(r => r.RequesterName));
        Assert.Equal(QueueStanding.Active, queue.TryGetStanding(1, out _));
    }

    [Fact]
    public void DrainWaitingUntracksEveryoneRemovedSoTheyCanAskAgainImmediately()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(Request(1, "Archer"), out _);
        queue.DrainWaiting();

        EnqueueResult result = queue.TryEnqueue(Request(1, "Archer"), out int aheadCount);

        Assert.Equal(EnqueueResult.Enqueued, result);
        Assert.Equal(0, aheadCount);
    }

    [Fact]
    public void DrainWaitingOnAnEmptyQueueReturnsNothing() =>
        Assert.Empty(new RequestQueue(5).DrainWaiting());

    private static BuffRequest Request(uint requesterObjectId, string name) =>
        new(requesterObjectId, name, "buff");
}
