using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Web;

/// <summary>Lock-guarded — a hub receives heartbeats from spoke request-handler threads and its
/// own status from the tick thread at the same time.</summary>
internal sealed class MeshRegistry
{
    private static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan DefaultRemoveAfter = TimeSpan.FromSeconds(120);

    private readonly IClock _clock;
    private readonly TimeSpan _staleAfter;
    private readonly TimeSpan _removeAfter;
    private readonly object _gate = new();
    private readonly Dictionary<string, BotRecord> _bots = new();
    private string? _hubBotId;

    internal MeshRegistry(IClock clock, TimeSpan? staleAfter = null, TimeSpan? removeAfter = null)
    {
        _clock = clock;
        _staleAfter = staleAfter ?? DefaultStaleAfter;
        _removeAfter = removeAfter ?? DefaultRemoveAfter;
    }

    /// <summary>Called for the hub's own bot in-process, never over HTTP, and for every spoke from its heartbeat.</summary>
    internal void Report(string botId, string name, string world, bool isHub, MeshStatus status)
    {
        lock (_gate)
        {
            if (isHub)
                _hubBotId = botId;

            if (!_bots.TryGetValue(botId, out BotRecord? record))
            {
                record = new BotRecord();
                _bots[botId] = record;
            }
            record.Name = name;
            record.World = world;
            record.IsHub = isHub;
            record.Status = status;
            record.LastSeen = _clock.UtcNow;
        }
    }

    internal bool Exists(string botId)
    {
        lock (_gate)
            return _bots.ContainsKey(botId);
    }

    internal void EnqueueCommand(string botId, MeshCommand command)
    {
        lock (_gate)
        {
            if (_bots.TryGetValue(botId, out BotRecord? record))
                record.Pending.Enqueue(command);
        }
    }

    /// <summary>Hands a spoke its pending commands and clears them in the same step — a spoke's next heartbeat is the only delivery it gets.</summary>
    internal IReadOnlyList<MeshCommand> DrainCommands(string botId)
    {
        lock (_gate)
        {
            if (!_bots.TryGetValue(botId, out BotRecord? record) || record.Pending.Count == 0)
                return Array.Empty<MeshCommand>();

            var drained = new List<MeshCommand>(record.Pending.Count);
            while (record.Pending.Count > 0)
                drained.Add(record.Pending.Dequeue());
            return drained;
        }
    }

    internal MeshBotsSnapshot Snapshot()
    {
        DateTimeOffset now = _clock.UtcNow;
        lock (_gate)
        {
            List<string>? expired = null;
            foreach ((string id, BotRecord record) in _bots)
                if (now - record.LastSeen > _removeAfter)
                    (expired ??= new List<string>()).Add(id);
            if (expired is not null)
                foreach (string id in expired)
                    _bots.Remove(id);

            var bots = new List<MeshBot>(_bots.Count);
            foreach ((string id, BotRecord record) in _bots)
            {
                double lastSeenSeconds = (now - record.LastSeen).TotalSeconds;
                bool stale = lastSeenSeconds > _staleAfter.TotalSeconds;
                bots.Add(new MeshBot(id, record.Name, record.World, record.IsHub, stale, lastSeenSeconds, record.Status!));
            }
            bots.Sort(static (a, b) => string.CompareOrdinal(a.BotId, b.BotId));

            return new MeshBotsSnapshot(_hubBotId ?? string.Empty, bots);
        }
    }

    private sealed class BotRecord
    {
        internal string Name = string.Empty;
        internal string World = string.Empty;
        internal bool IsHub;
        internal MeshStatus? Status;
        internal DateTimeOffset LastSeen;
        internal Queue<MeshCommand> Pending { get; } = new();
    }
}
