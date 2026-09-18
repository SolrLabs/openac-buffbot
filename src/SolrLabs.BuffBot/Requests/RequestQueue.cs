using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Requests;

internal enum EnqueueResult
{
    Enqueued,
    AlreadyQueued,
    QueueFull,
}

internal enum QueueStanding
{
    /// <summary>Never asked, or already finished.</summary>
    NotQueued,

    Waiting,

    /// <summary>Dequeued and currently the one being cast on.</summary>
    Active,
}

/// <summary>One request per requester, enforced from <see cref="TryEnqueue"/> until <see
/// cref="Complete"/>, not just while waiting in line.</summary>
internal sealed class RequestQueue
{
    private readonly Queue<BuffRequest> _waiting = new();
    private readonly HashSet<uint> _tracked = new();
    private readonly Dictionary<uint, DateTimeOffset> _enqueuedAt = new();
    private readonly IClock _clock;
    private readonly int _capacity;

    /// <summary><paramref name="clock"/> defaults to the real clock so existing callers keep working
    /// unchanged.</summary>
    internal RequestQueue(int capacity, IClock? clock = null)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _clock = clock ?? SystemClock.Instance;
    }

    internal int WaitingCount => _waiting.Count;

    internal EnqueueResult TryEnqueue(BuffRequest request, out int aheadCount)
    {
        aheadCount = _waiting.Count;

        if (_tracked.Contains(request.RequesterObjectId))
            return EnqueueResult.AlreadyQueued;

        if (_waiting.Count >= _capacity)
            return EnqueueResult.QueueFull;

        _waiting.Enqueue(request);
        _tracked.Add(request.RequesterObjectId);
        _enqueuedAt[request.RequesterObjectId] = _clock.UtcNow;
        return EnqueueResult.Enqueued;
    }

    /// <summary>Removes and returns the head, still counting it against its requester until
    /// <see cref="Complete"/>.</summary>
    internal bool TryDequeue(out BuffRequest request) => _waiting.TryDequeue(out request);

    /// <summary>Read between <see cref="TryDequeue"/> and <see cref="Complete"/>, which removes
    /// the timestamp. 0 for an id this queue never admitted.</summary>
    internal double WaitSecondsSince(uint requesterObjectId) =>
        _enqueuedAt.TryGetValue(requesterObjectId, out DateTimeOffset since)
            ? (_clock.UtcNow - since).TotalSeconds
            : 0d;

    /// <summary>Releases a requester's slot once their request — waiting or processed — is
    /// finished, successfully or not.</summary>
    internal void Complete(uint requesterObjectId)
    {
        _tracked.Remove(requesterObjectId);
        _enqueuedAt.Remove(requesterObjectId);
    }

    /// <summary>Never includes the one already dequeued and being cast on — the same "waiting"
    /// line <see cref="TryGetStanding"/> draws.</summary>
    internal IReadOnlyList<QueueEntry> Waiting()
    {
        DateTimeOffset now = _clock.UtcNow;
        var entries = new List<QueueEntry>(_waiting.Count);
        foreach (BuffRequest request in _waiting)
        {
            double waitingSeconds = _enqueuedAt.TryGetValue(request.RequesterObjectId, out DateTimeOffset since)
                ? (now - since).TotalSeconds
                : 0d;
            entries.Add(new QueueEntry(
                request.RequesterObjectId, request.RequesterName, request.SetName, waitingSeconds));
        }
        return entries;
    }

    /// <summary><paramref name="aheadCount"/> is meaningful only for <see
    /// cref="QueueStanding.Waiting"/>.</summary>
    internal QueueStanding TryGetStanding(uint requesterObjectId, out int aheadCount)
    {
        aheadCount = 0;
        if (!_tracked.Contains(requesterObjectId))
            return QueueStanding.NotQueued;

        foreach (BuffRequest request in _waiting)
        {
            if (request.RequesterObjectId == requesterObjectId)
                return QueueStanding.Waiting;
            aheadCount++;
        }

        return QueueStanding.Active;
    }

    /// <summary>Never touches the one already dequeued and being cast on, so the run in flight
    /// finishes on its own.</summary>
    internal IReadOnlyList<BuffRequest> DrainWaiting()
    {
        var drained = new List<BuffRequest>(_waiting.Count);
        while (_waiting.TryDequeue(out BuffRequest request))
        {
            _tracked.Remove(request.RequesterObjectId);
            _enqueuedAt.Remove(request.RequesterObjectId);
            drained.Add(request);
        }
        return drained;
    }

    /// <summary>Lets a caller compare a second request against what is already queued (no-op vs
    /// <see cref="TryReplaceWaiting"/>).</summary>
    internal bool TryGetWaiting(uint requesterObjectId, out BuffRequest request)
    {
        foreach (BuffRequest candidate in _waiting)
        {
            if (candidate.RequesterObjectId == requesterObjectId)
            {
                request = candidate;
                return true;
            }
        }
        request = default;
        return false;
    }

    /// <summary>Keeps the requester's place in line, unlike <see cref="TryCancel"/> then <see
    /// cref="TryEnqueue"/>, which sends them to the back.</summary>
    internal string? TryReplaceWaiting(BuffRequest newRequest)
    {
        int originalCount = _waiting.Count;
        string? previousSetName = null;
        for (int i = 0; i < originalCount; i++)
        {
            BuffRequest request = _waiting.Dequeue();
            if (previousSetName is null && request.RequesterObjectId == newRequest.RequesterObjectId)
            {
                previousSetName = request.SetName;
                _waiting.Enqueue(newRequest);
                continue;
            }
            _waiting.Enqueue(request);
        }
        return previousSetName;
    }

    /// <summary>False if not tracked, or already the active recipient — this does not interrupt a
    /// cast under way.</summary>
    internal bool TryCancel(uint requesterObjectId)
    {
        int originalCount = _waiting.Count;
        bool removed = false;
        for (int i = 0; i < originalCount; i++)
        {
            BuffRequest request = _waiting.Dequeue();
            if (!removed && request.RequesterObjectId == requesterObjectId)
            {
                removed = true;
                continue;
            }
            _waiting.Enqueue(request);
        }

        if (removed)
            _tracked.Remove(requesterObjectId);

        return removed;
    }
}
