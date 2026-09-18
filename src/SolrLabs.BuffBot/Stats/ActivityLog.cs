namespace SolrLabs.BuffBot.Stats;

/// <summary>A cast enters only once <see cref="Casting.CastStateMachine"/> has a server
/// confirmation, never on a bare <c>RequestCast</c> send.</summary>
internal sealed class ActivityLog
{
    internal const int Capacity = 50;

    private readonly List<RecentEvent> _events = new(Capacity);

    /// <summary>Inserts at the front so <see cref="Snapshot"/> is always newest-first without
    /// re-sorting — the ring is small enough that the O(n) shift costs less than a deque's bookkeeping.</summary>
    internal void Add(RecentEvent evt)
    {
        _events.Insert(0, evt);
        if (_events.Count > Capacity)
            _events.RemoveAt(_events.Count - 1);
    }

    internal IReadOnlyList<RecentEvent> Snapshot() => _events.ToArray();
}
