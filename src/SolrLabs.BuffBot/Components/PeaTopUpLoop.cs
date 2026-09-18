namespace SolrLabs.BuffBot.Components;

/// <summary><see cref="Casting.CastStateMachine"/>'s retry is exempt from the rolling-window cap
/// below, since it already splits at most once per chain step.</summary>
internal sealed class PeaTopUpLoop
{
    internal const int DefaultMaxTopUpsPerReagent = 3;
    internal const double DefaultWindowSeconds = 600d; // 10 minutes of accumulated tick time

    private readonly IPeaSplitCoordinator _splitter;
    private readonly Action<string> _warn;
    private readonly Action _requestResample;
    private readonly int _maxTopUpsPerReagent;
    private readonly double _windowSeconds;

    /// <summary>Accumulated tick time, never the wall clock.</summary>
    private double _clockSeconds;

    /// <summary>The report generation a fresh start must wait for, or <see langword="null"/> if
    /// nothing is gating a start right now.</summary>
    private int? _minReportGenerationForNextStart;

    private readonly Dictionary<uint, List<double>> _recentStartSeconds = new();
    private readonly HashSet<uint> _warnedCappedThisWindow = new();

    internal PeaTopUpLoop(
        IPeaSplitCoordinator splitter,
        Action<string> warn,
        Action requestResample,
        int maxTopUpsPerReagent = DefaultMaxTopUpsPerReagent,
        double windowSeconds = DefaultWindowSeconds)
    {
        _splitter = splitter;
        _warn = warn;
        _requestResample = requestResample;
        _maxTopUpsPerReagent = maxTopUpsPerReagent;
        _windowSeconds = windowSeconds;
    }

    /// <summary>Always polls whatever is in flight first, so the caller never needs a separate "is
    /// something running" read.</summary>
    internal void Pump(
        double deltaSeconds, ComponentReport components, int componentsGeneration, int lowStockThreshold)
    {
        _clockSeconds += deltaSeconds;

        PeaSplitPollResult result = _splitter.Poll(deltaSeconds);
        switch (result)
        {
            case PeaSplitPollResult.Pending:
                return;
            case PeaSplitPollResult.Confirmed:
            case PeaSplitPollResult.Failed:
                // Wait for a report sampled after this tick's own generation before a fresh start.
                _minReportGenerationForNextStart = componentsGeneration + 1;
                _requestResample();
                return;
        }

        // Nothing in flight, but still gated on a fresh-enough report if a resolution just happened.
        if (_minReportGenerationForNextStart is { } minGeneration && componentsGeneration < minGeneration)
            return;

        uint? eligible = PeaSplitter.SelectTopUpCandidate(components, lowStockThreshold, IsCapped);
        if (eligible is { } candidate)
        {
            if (_splitter.TryStart(candidate, "topping up reagent stock"))
                RecordStart(candidate);
            return;
        }

        // If the only reason nothing is eligible is that the worst candidate is capped, say so once.
        uint? worst = PeaSplitter.SelectTopUpCandidate(components, lowStockThreshold);
        if (worst is { } cappedReagent)
            WarnCappedOnce(cappedReagent);
    }

    private bool IsCapped(uint componentWeenieId)
    {
        if (!_recentStartSeconds.TryGetValue(componentWeenieId, out List<double>? starts))
            return false;

        PurgeExpired(starts);
        return starts.Count >= _maxTopUpsPerReagent;
    }

    private void PurgeExpired(List<double> starts)
    {
        double cutoff = _clockSeconds - _windowSeconds;
        int expiredCount = 0;
        while (expiredCount < starts.Count && starts[expiredCount] < cutoff)
            expiredCount++;
        if (expiredCount > 0)
            starts.RemoveRange(0, expiredCount);
    }

    private void RecordStart(uint componentWeenieId)
    {
        if (!_recentStartSeconds.TryGetValue(componentWeenieId, out List<double>? starts))
        {
            starts = new List<double>();
            _recentStartSeconds[componentWeenieId] = starts;
        }

        starts.Add(_clockSeconds);
        _warnedCappedThisWindow.Remove(componentWeenieId);
    }

    private void WarnCappedOnce(uint componentWeenieId)
    {
        if (!_warnedCappedThisWindow.Add(componentWeenieId))
            return;

        string name = PeaSplitTable.TryGetRecipeForComponent(componentWeenieId, out PeaSplitRecipe recipe)
            ? recipe.ComponentName
            : componentWeenieId.ToString();
        _warn($"[split] hit the top-up cap ({_maxTopUpsPerReagent} splits within the window) for "
            + $"{name}; pausing top-ups for it until the window clears.");
    }
}
