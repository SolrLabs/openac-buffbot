using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Stats;

/// <summary><see cref="Attempts"/> excludes a skip (already up) or a never-attempted line —
/// neither cost the server a request.</summary>
internal readonly record struct SpellLineStats(string Line, int Attempts, int Landed, int Fizzles);

/// <summary><see cref="HourStartUtc"/> is the hour's floor, so the page can grey out any hour
/// before the plugin started rather than draw it as a real zero.</summary>
internal readonly record struct HourlyBucket(DateTimeOffset HourStartUtc, int Requested, int Upkeep, int Fizzles = 0);

internal readonly record struct RefusalCounts(int OutOfRange, int UnknownLine, int NothingLearned, int Unresolvable = 0)
{
    internal int Total => OutOfRange + UnknownLine + NothingLearned + Unresolvable;
}

/// <summary><see langword="null"/> for both until a request has actually been dequeued and
/// started.</summary>
internal readonly record struct WaitStats(double? MedianSeconds, double? LongestSeconds);

internal sealed record SessionStatsSnapshot(
    DateTimeOffset StartedUtc,
    RefusalCounts Refusals,
    IReadOnlyList<SpellLineStats> Lines,
    IReadOnlyList<HourlyBucket> Hours,
    WaitStats Wait,
    int PlayersServed,
    int PlayersReturning);

/// <summary>One instance lives for the plugin's own lifetime, rather than being rebuilt on every
/// character (re-)enable.</summary>
internal sealed class SessionStats
{
    internal const int WaitSampleCapacity = 500;

    private const int HourWindow = 12;

    private readonly IClock _clock;
    private readonly Dictionary<string, (int Attempts, int Landed, int Fizzles)> _lines = new();

    /// <summary>Keyed by hour-since-epoch rather than a floored <see cref="DateTimeOffset"/>, so
    /// two clocks that agree on the second always agree on the bucket.</summary>
    private readonly Dictionary<long, (int Requested, int Upkeep, int Fizzles)> _hours = new();

    private readonly Queue<double> _waitSamples = new();
    private readonly Dictionary<uint, int> _runsByRequester = new();
    private int _outOfRange;
    private int _unknownLine;
    private int _nothingLearned;
    private int _unresolvable;

    internal SessionStats(IClock? clock = null)
    {
        _clock = clock ?? SystemClock.Instance;
        StartedUtc = _clock.UtcNow;
    }

    internal DateTimeOffset StartedUtc { get; }

    internal void RecordRefusal(RefusalReason reason)
    {
        switch (reason)
        {
            case RefusalReason.OutOfRange: _outOfRange++; break;
            case RefusalReason.UnknownLine: _unknownLine++; break;
            case RefusalReason.NothingLearned: _nothingLearned++; break;
            case RefusalReason.Unresolvable: _unresolvable++; break;
        }
    }

    internal void RecordCastLanded(string line, bool isPlayerRequest)
    {
        (int attempts, int landed, int fizzles) = _lines.TryGetValue(line, out var existing) ? existing : default;
        _lines[line] = (attempts + 1, landed + 1, fizzles);
        BumpHour(isPlayerRequest);
    }

    internal void RecordFizzle(string line)
    {
        (int attempts, int landed, int fizzles) = _lines.TryGetValue(line, out var existing) ? existing : default;
        _lines[line] = (attempts + 1, landed, fizzles + 1);
        BumpHourFizzle();
    }

    /// <summary>An attempt that did not land but is not a fizzle either. Never reaches the Recent
    /// ring by itself, only this line's own tally.</summary>
    internal void RecordOtherAttemptFailure(string line)
    {
        (int attempts, int landed, int fizzles) = _lines.TryGetValue(line, out var existing) ? existing : default;
        _lines[line] = (attempts + 1, landed, fizzles);
    }

    private void BumpHour(bool isPlayerRequest)
    {
        long hourKey = _clock.UtcNow.ToUnixTimeSeconds() / 3600;
        (int requested, int upkeep, int fizzles) = _hours.TryGetValue(hourKey, out var existing) ? existing : default;
        _hours[hourKey] = isPlayerRequest ? (requested + 1, upkeep, fizzles) : (requested, upkeep + 1, fizzles);
    }

    private void BumpHourFizzle()
    {
        long hourKey = _clock.UtcNow.ToUnixTimeSeconds() / 3600;
        (int requested, int upkeep, int fizzles) = _hours.TryGetValue(hourKey, out var existing) ? existing : default;
        _hours[hourKey] = (requested, upkeep, fizzles + 1);
    }

    internal void RecordWaitSeconds(double seconds)
    {
        _waitSamples.Enqueue(seconds);
        if (_waitSamples.Count > WaitSampleCapacity)
            _waitSamples.Dequeue();
    }

    /// <summary>Called once per run, not once per spell, so "asked more than once" counts
    /// requests rather than casts.</summary>
    internal void RecordPlayerServed(uint requesterObjectId)
    {
        _runsByRequester[requesterObjectId] = _runsByRequester.GetValueOrDefault(requesterObjectId) + 1;
    }

    internal SessionStatsSnapshot Snapshot()
    {
        var lines = new List<SpellLineStats>(_lines.Count);
        foreach ((string line, (int attempts, int landed, int fizzles)) in _lines)
        {
            if (attempts > 0)
                lines.Add(new SpellLineStats(line, attempts, landed, fizzles));
        }

        long currentHour = _clock.UtcNow.ToUnixTimeSeconds() / 3600;
        var hours = new List<HourlyBucket>(HourWindow);
        for (int i = HourWindow - 1; i >= 0; i--)
        {
            long hourKey = currentHour - i;
            (int requested, int upkeep, int fizzles) = _hours.TryGetValue(hourKey, out var existing) ? existing : default;
            hours.Add(new HourlyBucket(
                DateTimeOffset.FromUnixTimeSeconds(hourKey * 3600), requested, upkeep, fizzles));
        }

        WaitStats wait = ComputeWaitStats();

        int playersServed = _runsByRequester.Count;
        int playersReturning = 0;
        foreach (int runs in _runsByRequester.Values)
        {
            if (runs > 1)
                playersReturning++;
        }

        return new SessionStatsSnapshot(
            StartedUtc,
            new RefusalCounts(_outOfRange, _unknownLine, _nothingLearned, _unresolvable),
            lines,
            hours,
            wait,
            playersServed,
            playersReturning);
    }

    private WaitStats ComputeWaitStats()
    {
        if (_waitSamples.Count == 0)
            return new WaitStats(null, null);

        double[] sorted = _waitSamples.ToArray();
        Array.Sort(sorted);
        double median = sorted.Length % 2 == 1
            ? sorted[sorted.Length / 2]
            : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2d;
        return new WaitStats(median, sorted[^1]);
    }
}
