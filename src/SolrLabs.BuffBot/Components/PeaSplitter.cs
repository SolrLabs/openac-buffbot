using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Components;

/// <summary><see cref="Confirmed"/> and <see cref="Failed"/> are each reported exactly once, the
/// tick the split resolves, before returning to <see cref="Idle"/>.</summary>
internal enum PeaSplitPollResult
{
    Idle,
    Pending,
    Confirmed,
    Failed,
}

/// <summary>Small enough that a test can fake it directly rather than standing up a real <see
/// cref="PeaSplitter"/>.</summary>
internal interface IPeaSplitCoordinator
{
    /// <summary>False if the session is disabled, no recipe exists (<see cref="PeaSplitTable"/>),
    /// or the pea and Splitting Tool are not both held; the caller falls through to its own behaviour.</summary>
    bool TryStart(uint componentWeenieId, string reason);

    /// <summary><paramref name="deltaSeconds"/> is real elapsed time, not a poll count — a host
    /// frame is not a fixed unit of wall time.</summary>
    PeaSplitPollResult Poll(double deltaSeconds);
}

/// <summary>One split in flight at a time, shared by <see cref="Casting.CastStateMachine"/>'s
/// retry and <see cref="BuffBotPlugin"/>'s top-up loop.</summary>
internal sealed class PeaSplitter : IPeaSplitCoordinator
{
    internal const double DefaultStanceSettleSeconds = 1.5d;
    internal const double DefaultBusyTimeoutSeconds = 10d;
    internal const double DefaultConfirmTimeoutSeconds = 10d;

    /// <summary>ACE's <c>WeenieError</c> for "You must be at rest in peace mode to do trade
    /// skills" — the one Apply refusal worth a single retry.</summary>
    private const uint StanceRequiredWeenieError = 0x043Au;

    /// <summary>A fresh <see cref="IItemAutomation.CaptureOwnedItems"/> readback is not free, and
    /// nothing here needs frame-by-frame freshness.</summary>
    private const double ConfirmSampleIntervalSeconds = 0.25d;

    private enum Phase
    {
        Idle,
        EnteringPeace,
        StanceRetrySettle,
        WaitingIdle,
        Confirming,
    }

    private readonly struct ActiveSplit
    {
        internal required PeaSplitRecipe Recipe { get; init; }
        internal required uint ToolObjectId { get; init; }
        internal required uint PeaObjectId { get; init; }
        internal required int PeaStackBefore { get; init; }
        internal required int ComponentStockBefore { get; init; }
        internal required string Reason { get; init; }

        /// <summary>The completion revision read just before Apply was issued — a newer one
        /// belongs to this Apply, never an earlier unrelated use.</summary>
        internal long CompletionRevisionBeforeApply { get; init; }

        internal bool StanceRetried { get; init; }
    }

    private readonly IItemAutomation _items;
    private readonly ICombatAutomation _combat;
    private readonly Action<string> _trace;
    private readonly Action<string> _warn;
    private readonly double _stanceSettleSeconds;
    private readonly double _busyTimeoutSeconds;
    private readonly double _confirmTimeoutSeconds;

    private Phase _phase = Phase.Idle;
    private ActiveSplit? _active;
    private double _phaseElapsedSeconds;
    private double _sinceLastConfirmSampleSeconds;
    private bool _peaceRequested;
    private int _lastPeaNow;
    private int _lastComponentNow;
    private bool _warnedNoTool;

    internal PeaSplitter(
        IItemAutomation items,
        ICombatAutomation combat,
        Action<string> trace,
        Action<string> warn,
        double stanceSettleSeconds = DefaultStanceSettleSeconds,
        double busyTimeoutSeconds = DefaultBusyTimeoutSeconds,
        double confirmTimeoutSeconds = DefaultConfirmTimeoutSeconds)
    {
        _items = items;
        _combat = combat;
        _trace = trace;
        _warn = warn;
        _stanceSettleSeconds = stanceSettleSeconds;
        _busyTimeoutSeconds = busyTimeoutSeconds;
        _confirmTimeoutSeconds = confirmTimeoutSeconds;
    }

    /// <summary>Once a split could not be confirmed, nothing here is trusted again for the rest of
    /// the session; <see cref="BuffBotPlugin"/> clears this on the next Enable.</summary>
    internal bool SessionDisabled { get; private set; }

    internal bool IsIdle => _phase == Phase.Idle;

    public bool TryStart(uint componentWeenieId, string reason)
    {
        if (SessionDisabled || _phase != Phase.Idle)
            return false;

        if (!PeaSplitTable.TryGetRecipeForComponent(componentWeenieId, out PeaSplitRecipe recipe))
            return false;

        IReadOnlyList<PluginInventoryItem> current = _items.CaptureOwnedItems();

        PluginInventoryItem? tool = Find(current, ContributionAdvisor.SplittingToolWeenieClassId);
        if (tool is null)
        {
            if (!_warnedNoTool)
            {
                _warn("[split] no Splitting Tool held; cannot split peas this session.");
                _warnedNoTool = true;
            }
            return false;
        }

        PluginInventoryItem? pea = Find(current, recipe.PeaWeenieId);
        if (pea is null)
            return false; // No pea held is the ordinary case, never warned.

        _active = new ActiveSplit
        {
            Recipe = recipe,
            ToolObjectId = tool.Value.ObjectId,
            PeaObjectId = pea.Value.ObjectId,
            PeaStackBefore = SumWeenie(current, recipe.PeaWeenieId),
            ComponentStockBefore = SumWeenie(current, recipe.ComponentWeenieId),
            Reason = reason,
        };
        _phaseElapsedSeconds = 0;
        _peaceRequested = false;
        _phase = Phase.EnteringPeace;
        _trace($"[split] attempting {recipe.PeaName} -> {recipe.ComponentName} ({reason})");
        return true;
    }

    public PeaSplitPollResult Poll(double deltaSeconds)
    {
        if (_active is not { } active)
            return PeaSplitPollResult.Idle;

        // Peace first: the server-side move-to radius for a trade-skill use shrinks outside it.
        if (_phase == Phase.EnteringPeace)
        {
            if (!_peaceRequested)
            {
                _peaceRequested = true;
                PluginCombatCommandResult peace = RequestPeace();
                if (!peace.Accepted)
                    return FailTransient(active, $"peace mode request refused: {peace.Status}");

                // AlreadyReady: nothing was sent, so there is nothing to wait for settling.
                // Anything else accepted still needs the settle wait below.
                if (peace.Status == PluginCombatCommandStatus.AlreadyReady)
                {
                    _phase = Phase.WaitingIdle;
                    _phaseElapsedSeconds = 0;
                }
            }

            if (_phase == Phase.EnteringPeace)
            {
                _phaseElapsedSeconds += deltaSeconds;
                if (_phaseElapsedSeconds < _stanceSettleSeconds)
                    return PeaSplitPollResult.Pending;

                // Snapshot.Mode flips the instant the request is accepted, well before the server settles.
                if (_combat.Snapshot.Mode != PluginCombatMode.Peace)
                    return FailTransient(active, "peace mode did not settle before the split");

                _phase = Phase.WaitingIdle;
                _phaseElapsedSeconds = 0;
            }
        }

        if (_phase == Phase.StanceRetrySettle)
        {
            // No EnterMode call here: the client's tracked mode already reads Peace, so re-sending
            // it would be a no-op. Only time can tell whether the server has actually caught up.
            _phaseElapsedSeconds += deltaSeconds;
            if (_phaseElapsedSeconds < _stanceSettleSeconds)
                return PeaSplitPollResult.Pending;

            _phase = Phase.WaitingIdle;
            _phaseElapsedSeconds = 0;
        }

        if (_phase == Phase.WaitingIdle)
        {
            if (_items.IsBusy)
            {
                _phaseElapsedSeconds += deltaSeconds;
                if (_phaseElapsedSeconds >= _busyTimeoutSeconds)
                    return Fail(active, "the item surface stayed busy too long");
                return PeaSplitPollResult.Pending;
            }

            long completionRevisionBeforeApply = _items.LastCompletion.Revision;
            PluginItemCommandResult result = _items.Apply(active.ToolObjectId, active.PeaObjectId);
            _trace($"[split] apply {active.Recipe.PeaName} -> {result.Status}");
            if (!result.Accepted)
                return Fail(active, $"apply refused: {result.Status}");

            active = active with { CompletionRevisionBeforeApply = completionRevisionBeforeApply };
            _active = active;
            _phase = Phase.Confirming;
            _phaseElapsedSeconds = 0;
            // Forces the first Confirming poll to sample right away.
            _sinceLastConfirmSampleSeconds = ConfirmSampleIntervalSeconds;
            return PeaSplitPollResult.Pending;
        }

        // Phase.Confirming
        PluginItemUseCompletion completion = _items.LastCompletion;
        if (completion.Revision > active.CompletionRevisionBeforeApply
            && completion.SourceObjectId == active.ToolObjectId
            && completion.TargetObjectId == active.PeaObjectId
            && completion.WeenieError != 0)
        {
            if (completion.WeenieError == StanceRequiredWeenieError && !active.StanceRetried)
            {
                _trace(
                    $"[split] apply refused (0x{completion.WeenieError:X4}, must be at rest in "
                    + "peace mode); retrying peace once.");
                _active = active with { StanceRetried = true };
                _phase = Phase.StanceRetrySettle;
                _phaseElapsedSeconds = 0;
                return PeaSplitPollResult.Pending;
            }

            return FailTransient(
                active,
                $"apply failed for {active.Recipe.PeaName} (error 0x{completion.WeenieError:X4})");
        }

        _sinceLastConfirmSampleSeconds += deltaSeconds;
        if (_sinceLastConfirmSampleSeconds >= ConfirmSampleIntervalSeconds)
        {
            IReadOnlyList<PluginInventoryItem> current = _items.CaptureOwnedItems();
            _lastPeaNow = SumWeenie(current, active.Recipe.PeaWeenieId);
            _lastComponentNow = SumWeenie(current, active.Recipe.ComponentWeenieId);
            _sinceLastConfirmSampleSeconds = 0;
        }

        if (_lastPeaNow < active.PeaStackBefore && _lastComponentNow > active.ComponentStockBefore)
        {
            _trace($"[split] confirmed {active.Recipe.PeaName} -> "
                + $"{_lastComponentNow - active.ComponentStockBefore} {active.Recipe.ComponentName}(s).");
            _active = null;
            _phase = Phase.Idle;
            return PeaSplitPollResult.Confirmed;
        }

        _phaseElapsedSeconds += deltaSeconds;
        if (_phaseElapsedSeconds < _confirmTimeoutSeconds)
            return PeaSplitPollResult.Pending;

        return Fail(active, $"could not confirm the split of {active.Recipe.PeaName}");
    }

    private PluginCombatCommandResult RequestPeace()
    {
        PluginCombatCommandResult result = _combat.EnterMode(PluginCombatMode.Peace);
        _trace($"[split] peace -> {result.Status}");
        return result;
    }

    private PeaSplitPollResult FailTransient(ActiveSplit active, string reason)
    {
        _active = null;
        _phase = Phase.Idle;
        _warn($"[split] {reason} ({active.Reason}).");
        return PeaSplitPollResult.Failed;
    }

    private PeaSplitPollResult Fail(ActiveSplit active, string reason)
    {
        SessionDisabled = true;
        _active = null;
        _phase = Phase.Idle;
        _warn($"[split] {reason} ({active.Reason}); disabling pea splitting for this session.");
        return PeaSplitPollResult.Failed;
    }

    private static PluginInventoryItem? Find(IReadOnlyList<PluginInventoryItem> items, uint weenieClassId)
    {
        foreach (PluginInventoryItem item in items)
            if (item.WeenieClassId == weenieClassId && !item.IsEquipped && item.StackSize > 0)
                return item;
        return null;
    }

    private static int SumWeenie(IReadOnlyList<PluginInventoryItem> items, uint weenieClassId)
    {
        int total = 0;
        foreach (PluginInventoryItem item in items)
            if (item.WeenieClassId == weenieClassId)
                total += item.StackSize;
        return total;
    }

    /// <summary><see langword="null"/> once every in-scope reagent is above <paramref
    /// name="lowStockThreshold"/>. <paramref name="isExcluded"/> skips to the next-lowest eligible one.</summary>
    internal static uint? SelectTopUpCandidate(
        ComponentReport allTiers, int lowStockThreshold, Func<uint, bool>? isExcluded = null)
    {
        if (!allTiers.Available)
            return null;

        uint? best = null;
        int bestStock = int.MaxValue;
        foreach (ComponentUsage row in allTiers.Items)
        {
            if (row.UsedBy <= 0 || row.Stock >= lowStockThreshold)
                continue;
            if (!PeaSplitTable.TryGetRecipeForComponent(row.WeenieClassId, out _))
                continue;
            if (isExcluded?.Invoke(row.WeenieClassId) == true)
                continue;
            if (row.Stock >= bestStock)
                continue;

            best = row.WeenieClassId;
            bestStock = row.Stock;
        }

        return best;
    }
}
