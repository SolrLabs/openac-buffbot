using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Policy;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Casting;

// Only BuffBotPlugin.OnTick touches the host; everything here takes automation as plain interfaces.
internal sealed class BuffCoordinator
{
    private const double IdleSelfBuffIntervalSeconds = 60d;

    private readonly RequestQueue _queue;
    private readonly CastStateMachine _caster;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _spellSets;
    private readonly IReadOnlyList<string> _selfSpellLines;
    private readonly IReadOnlyList<string> _manaUpkeepLines;

    // Null keeps the top learned tier; self-buff and mana upkeep always use the top tier regardless.
    private int? _targetTier;

    internal int? TargetTier
    {
        get => _targetTier;
        set => _targetTier = value;
    }

    // A shared instance survives disable/re-enable; otherwise a private one is owned.
    private readonly BotStats _stats;

    private BuffRequest? _current;

    // No requester to reply to and no queue slot to release.
    private bool _isIdleSelfBuffRun;

    // The queue is left untouched, so the next request is still waiting when this finishes.
    private bool _toppingUpBeforeNext;

    private double _idleSecondsSinceSelfBuffCheck;

    private bool _isIdleTopUp;

    // Reset whenever mana could have changed, so a caster below the high mark isn't retried every tick.
    private bool _idleTopUpAttempted;

    private readonly Action<string>? _trace;

    // A real request is never gated by this backoff.
    // Cleared on a stock change, a landed cast, or re-enable.
    private bool _idleComponentBackoffActive;

    // Null when unavailable; only a landed request or re-enable can clear the backoff then.
    private IReadOnlyList<ComponentUsage>? _componentStockAtBackoffStart;

    private int? _lastObservedComponentCeilingRung;

    // Watched independently of the idle backoff above.
    private IReadOnlyList<ComponentUsage>? _componentStockAtCeilingSet;

    private double _refusalRangeMeters = RangePolicy.MaxCastRangeMeters * 0.9;

    internal double RefusalRangeMeters
    {
        get => _refusalRangeMeters;
        set => _refusalRangeMeters = value;
    }

    internal bool TierFallbackEnabled
    {
        get => _caster.TierFallbackEnabled;
        set => _caster.TierFallbackEnabled = value;
    }

    internal int FizzlesBeforeSkip
    {
        get => _caster.FizzleSkipBound;
        set => _caster.FizzleSkipBound = value;
    }

    internal double ManaBounceLowWaterFraction
    {
        get => _caster.ManaBounceLowWaterFraction;
        set => _caster.ManaBounceLowWaterFraction = value;
    }

    internal double ManaBounceHighWaterFraction
    {
        get => _caster.ManaBounceHighWaterFraction;
        set => _caster.ManaBounceHighWaterFraction = value;
    }

    // The first request served is never delayed by a top-up.
    private bool _hasServedARequest;

    internal bool WillTopUpBeforeNextRequest => _hasServedARequest && _caster.NeedsManaTopUp;

    // catalog resolves a component id to a weenie class id for the pea-split retry.
    internal BuffCoordinator(
        RequestQueue queue,
        IMagicCommands magic,
        IEnchantmentAutomation enchantments,
        IItemAutomation items,
        IEquipmentAutomation equipment,
        ICombatAutomation combat,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? spellSets = null,
        Action<string>? trace = null,
        Action<string>? warn = null,
        ICharacterInfo? character = null,
        double manaBounceLowWaterFraction = CastStateMachine.DefaultManaBounceLowWaterFraction,
        double manaBounceHighWaterFraction = CastStateMachine.DefaultManaBounceHighWaterFraction,
        int? targetTier = null,
        bool tierFallbackEnabled = true,
        int fizzleSkipBound = CastStateMachine.DefaultFizzleSkipBound,
        BotStats? stats = null,
        IPeaSplitCoordinator? peaSplitCoordinator = null,
        ISpellCatalog? catalog = null)
    {
        _queue = queue;
        _spellSets = spellSets ?? DefaultSpellSets.Table;
        _selfSpellLines = _spellSets.TryGetValue(DefaultSpellSets.Self, out var selfLines)
            ? selfLines
            : Array.Empty<string>();
        _manaUpkeepLines = _spellSets.TryGetValue(DefaultSpellSets.ManaUpkeep, out var upkeepLines)
            ? upkeepLines
            : Array.Empty<string>();
        _targetTier = targetTier;
        _stats = stats ?? new BotStats();
        _trace = trace;
        _caster = new CastStateMachine(
            magic, enchantments, items, equipment, combat, character,
            manaBounceLowWaterFraction: manaBounceLowWaterFraction,
            manaBounceHighWaterFraction: manaBounceHighWaterFraction,
            tierFallbackEnabled: tierFallbackEnabled,
            fizzleSkipBound: fizzleSkipBound,
            trace: trace, warn: warn, stats: _stats,
            peaSplitCoordinator: peaSplitCoordinator,
            catalog: catalog);
    }

    // While tradeOpen, no new request, idle self-buff or top-up may start; a run in flight still finishes.
    // A null distanceToRequester means the position is unknown, not out of range.
    internal BuffBotStatus Pump(
        double deltaSeconds,
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments,
        IReadOnlyList<PluginChatMessage> chatMessages,
        bool selfBuffingEnabled,
        Func<uint, double?> distanceToRequester,
        Action<uint, string, string> sendReply,
        int tellsAnswered = 0,
        IReadOnlyList<MutedEntry>? muted = null,
        IReadOnlyList<PluginWorldObject>? capturedObjects = null,
        ComponentReport? components = null,
        Func<uint, bool>? usesScarabOnlyFormula = null,
        bool tradeOpen = false)
    {
        IReadOnlyList<MutedEntry> mutedEntries = muted ?? Array.Empty<MutedEntry>();
        IReadOnlyList<PluginWorldObject> objects = capturedObjects ?? Array.Empty<PluginWorldObject>();

        // Fed fresh every tick, unlike the next-run-only settings above.
        _caster.UsesScarabOnlyFormula = usesScarabOnlyFormula;

        if (_idleComponentBackoffActive
            && components is { Available: true } freshReport
            && !StockMatches(freshReport.Items, _componentStockAtBackoffStart))
        {
            ClearComponentBackoff("the reagent stock changed");
        }

        int? currentCeilingRung = _caster.ComponentCeilingRung;
        if (currentCeilingRung != _lastObservedComponentCeilingRung)
        {
            _lastObservedComponentCeilingRung = currentCeilingRung;
            _componentStockAtCeilingSet = currentCeilingRung is not null
                && components is { Available: true } ceilingSetReport
                ? ceilingSetReport.Items
                : null;
        }
        else if (currentCeilingRung is not null
            && components is { Available: true } freshCeilingReport
            && !StockMatches(freshCeilingReport.Items, _componentStockAtCeilingSet))
        {
            _caster.ClearComponentCeiling("the reagent stock changed");
            _lastObservedComponentCeilingRung = null;
            _componentStockAtCeilingSet = null;
        }

        if (_current is null && !_isIdleSelfBuffRun && !_isIdleTopUp)
        {
            if (_caster.IsWaitingOnServer)
            {
                // A previous cast may still be resolving server-side; starting a new one now would race it.
                _caster.Advance(deltaSeconds, chatMessages);
                return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
            }

            if (tradeOpen)
            {
                // A run already in flight never reaches this branch, so it finishes undisturbed.
                _caster.Advance(deltaSeconds, chatMessages);
                return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
            }

            if (_toppingUpBeforeNext)
            {
                // Runs below via the shared Advance call; the queue stays untouched until it closes.
            }
            else if (_queue.TryDequeue(out BuffRequest next))
            {
                _current = next;
                // Measured here, not at Complete, so a top-up delay afterward isn't counted as queue wait.
                _stats.RecordWaitSeconds(_queue.WaitSecondsSince(next.RequesterObjectId));
                if (WillTopUpBeforeNextRequest)
                {
                    _toppingUpBeforeNext = true;
                    _caster.BeginTopUp(ManaUpkeepPlan(catalog));
                }
                else if (!TryStart(next, catalog, activeEnchantments, distanceToRequester, sendReply, objects))
                {
                    return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
                }
            }
            else if (!TryStartIdleSelfBuff(deltaSeconds, selfBuffingEnabled, catalog, activeEnchantments)
                && !TryStartIdleTopUp(catalog))
            {
                _caster.Advance(deltaSeconds, chatMessages);
                return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
            }
        }
        else if (!_toppingUpBeforeNext
            && _current is { } inProgress
            && !IsRequesterInRange(inProgress, distanceToRequester))
        {
            // Told once and the run ends there; idle self-buff and a pending top-up never reach here.
            _caster.Cancel();
            Abandon(inProgress, DefaultReplies.OutOfRange, RefusalReason.OutOfRange, sendReply);
            return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
        }

        CastRunResult? result = _caster.Advance(deltaSeconds, chatMessages);
        if (result is null)
            return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);

        if (_toppingUpBeforeNext)
        {
            // TryStart re-checks range and re-resolves the spell set fresh at this moment.
            _toppingUpBeforeNext = false;
            TryStart(_current!.Value, catalog, activeEnchantments, distanceToRequester, sendReply, objects);
            return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
        }

        if (_isIdleSelfBuffRun)
        {
            _isIdleSelfBuffRun = false;
            _idleTopUpAttempted = false;
            if (IsMissingComponents(result))
                BeginComponentBackoff(components);
            return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
        }

        if (_isIdleTopUp)
        {
            _isIdleTopUp = false;
            if (IsMissingComponents(result))
                BeginComponentBackoff(components);
            return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
        }

        BuffRequest finished = _current!.Value;
        _queue.Complete(finished.RequesterObjectId);
        _current = null;
        _hasServedARequest = true;

        // A landed cast is evidence of reagents, since a real request attempts regardless of the backoff.
        if (result.Steps.Any(static step => step.Outcome == CastOutcome.Cast))
        {
            _stats.RecordPlayerServed(finished.RequesterObjectId);
            ClearComponentBackoff("a request landed a cast");
        }

        string closing = result.Outcome switch
        {
            CastRunOutcome.Success => DefaultReplies.ClosingReply(result.Steps, result.ComponentCeilingRung),
            CastRunOutcome.Partial => DefaultReplies.ClosingPartial(result.Steps, result.ComponentCeilingRung),
            CastRunOutcome.Stopped => DefaultReplies.ClosingStopped(result.StopReason!.Value),
            _ => DefaultReplies.ClosingFailure(result.Failure!.Value, result.Steps),
        };
        sendReply(finished.RequesterObjectId, finished.RequesterName, closing);
        return BuildStatus(selfBuffingEnabled, tellsAnswered, mutedEntries);
    }

    private BuffBotStatus BuildStatus(bool enabled, int tellsAnswered, IReadOnlyList<MutedEntry> muted)
    {
        BotActivity activity = _toppingUpBeforeNext || _isIdleTopUp
            ? BotActivity.ToppingUp
            : _isIdleSelfBuffRun
                ? BotActivity.SelfBuffing
                : _current is not null
                    ? BotActivity.Serving
                    : BotActivity.Idle;

        BotStatsSnapshot statsSnapshot = _stats.Snapshot();
        return new BuffBotStatus(
            enabled,
            activity,
            CurrentRequesterName: _current?.RequesterName,
            CurrentRequesterObjectId: _current?.RequesterObjectId,
            CurrentSpellLine: _caster.CurrentSpellLine,
            CurrentSpellId: _caster.CurrentSpellId,
            StepIndex: _caster.StepIndex,
            StepCount: _caster.StepCount,
            CurrentMana: _caster.CurrentMana,
            MaxMana: _caster.MaxMana,
            Waiting: _queue.Waiting(),
            CurrentHealth: _caster.CurrentHealth,
            MaxHealth: _caster.MaxHealth,
            CurrentStamina: _caster.CurrentStamina,
            MaxStamina: _caster.MaxStamina,
            Muted: muted,
            Counters: new SessionCounters(
                tellsAnswered,
                _caster.TotalCastsLanded,
                _caster.TotalFizzles,
                _caster.TotalManaBounces,
                _caster.TotalTierStepDowns),
            Stats: statsSnapshot.Session,
            Recent: statsSnapshot.Recent);
    }

    private bool TryStartIdleSelfBuff(
        double deltaSeconds,
        bool selfBuffingEnabled,
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments)
    {
        if (!selfBuffingEnabled)
        {
            _idleSecondsSinceSelfBuffCheck = 0;
            return false;
        }

        _idleSecondsSinceSelfBuffCheck += deltaSeconds;
        if (_idleSecondsSinceSelfBuffCheck < IdleSelfBuffIntervalSeconds)
            return false;

        _idleSecondsSinceSelfBuffCheck = 0;

        if (_idleComponentBackoffActive)
            return false;

        IReadOnlyList<ResolvedSpell> due =
            SelfBuffPlanner.PlanDue(catalog, _selfSpellLines, activeEnchantments);
        if (due.Count == 0)
            return false;

        _isIdleSelfBuffRun = true;
        _caster.Begin(due, targetObjectId: 0u, targetName: string.Empty, ManaUpkeepPlan(catalog));
        return true;
    }

    private bool TryStartIdleTopUp(IReadOnlyList<PluginSpellInfo> catalog)
    {
        if (_idleTopUpAttempted || !_caster.NeedsManaTopUp || _idleComponentBackoffActive)
            return false;

        _idleTopUpAttempted = true;
        _isIdleTopUp = true;
        _caster.BeginTopUp(ManaUpkeepPlan(catalog));
        return true;
    }

    private static bool IsMissingComponents(CastRunResult result) =>
        result.Outcome == CastRunOutcome.Failed
        && result.Failure is { Kind: CastFailureKind.MissingComponents };

    private void BeginComponentBackoff(ComponentReport? components)
    {
        if (_idleComponentBackoffActive)
            return;

        _idleComponentBackoffActive = true;
        _componentStockAtBackoffStart = components is { Available: true } report ? report.Items : null;
        _trace?.Invoke(
            "idle backoff: missing components — pausing idle self-buff and idle top-up until the "
            + "reagent stock changes, this character is re-enabled, or a request lands a cast");
    }

    private void ClearComponentBackoff(string reason)
    {
        if (!_idleComponentBackoffActive)
            return;

        _idleComponentBackoffActive = false;
        _componentStockAtBackoffStart = null;
        _trace?.Invoke($"idle backoff cleared: {reason}");
    }

    // A null baseline never reports a change.
    private static bool StockMatches(
        IReadOnlyList<ComponentUsage> current, IReadOnlyList<ComponentUsage>? baseline)
    {
        if (baseline is null)
            return true;

        if (current.Count != baseline.Count)
            return false;

        var baselineStock = new Dictionary<uint, int>(baseline.Count);
        foreach (ComponentUsage usage in baseline)
            baselineStock[usage.WeenieClassId] = usage.Stock;

        foreach (ComponentUsage usage in current)
            if (!baselineStock.TryGetValue(usage.WeenieClassId, out int stock) || stock != usage.Stock)
                return false;

        return true;
    }

    internal void ClearComponentBackoffForReenable()
    {
        ClearComponentBackoff("this character was re-enabled");
        _caster.ClearComponentCeiling("this character was re-enabled");
        _lastObservedComponentCeilingRung = null;
        _componentStockAtCeilingSet = null;
    }

    // On a false return, the refusal reply is already sent and _current already cleared.
    private bool TryStart(
        BuffRequest request,
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments,
        Func<uint, double?> distanceToRequester,
        Action<uint, string, string> sendReply,
        IReadOnlyList<PluginWorldObject> capturedObjects)
    {
        // Mana is about to move, so an idle top-up gets to try fresh next time the queue empties.
        _idleTopUpAttempted = false;

        if (!IsRequesterInRange(request, distanceToRequester))
        {
            Abandon(request, DefaultReplies.OutOfRange, RefusalReason.OutOfRange, sendReply);
            return false;
        }

        IReadOnlyList<string> lines = _spellSets.TryGetValue(request.SetName, out var found)
            ? found
            : Array.Empty<string>();

        // An unlearned line is folded into the closing tell; refusal is only for malformed set
        // data or a profile with nothing learned at all.
        SpellSelectionResult selection =
            SpellSelector.ResolveForPlayerRequest(catalog, lines, targetTier: _targetTier);
        if (!selection.IsSuccess)
        {
            Abandon(
                request, DefaultReplies.UnknownSpellLine(selection.Failure!.Value.Line),
                RefusalReason.UnknownLine, sendReply);
            return false;
        }

        if (selection.Plan.Count == 0)
        {
            Abandon(
                request, DefaultReplies.NothingLearnedInProfile(request.SetName),
                RefusalReason.NothingLearned, sendReply);
            return false;
        }

        // Reorders so buffs the requester can most afford to lose expire first, the ones keeping them alive last.
        IReadOnlyList<ResolvedSpell> orderedPlan = SurvivalOrder.Apply(selection.Plan, _trace);

        // Best effort: an untrained self line doesn't block the rest, unlike the requester's own line above.
        IReadOnlyList<ResolvedSpell> selfDue =
            SelfBuffPlanner.PlanDue(catalog, _selfSpellLines, activeEnchantments);
        var plan = new List<ResolvedSpell>(selfDue.Count + orderedPlan.Count);
        plan.AddRange(selfDue);
        plan.AddRange(orderedPlan);

        var unlearnedLines = new List<string>(selection.UnlearnedLines);

        // Banes are cast on the requester's wielded shield, so this runs last.
        IReadOnlyList<string> baneLines = DefaultSpellSets.BaneLinesFor(request.SetName);
        List<string>? noShieldLines = null;
        if (baneLines.Count > 0)
        {
            if (ShieldFinder.TryFindWieldedShield(
                capturedObjects, request.RequesterObjectId, out PluginWorldObject shield))
            {
                SpellSelectionResult baneSelection =
                    SpellSelector.ResolveForPlayerRequest(catalog, baneLines, targetTier: _targetTier);
                foreach (ResolvedSpell resolved in baneSelection.Plan)
                    plan.Add(resolved with { TargetOverride = (shield.ObjectId, shield.Name) });
                unlearnedLines.AddRange(baneSelection.UnlearnedLines);
            }
            else
            {
                // No shield to target: every bane line is skipped with one shared reason.
                noShieldLines = new List<string>(baneLines);
            }
        }

        // isPlayerRequest is the one run whose target-facing steps always cast, never consulting the ledger.
        _caster.Begin(
            plan, request.RequesterObjectId, request.RequesterName, ManaUpkeepPlan(catalog),
            isPlayerRequest: true, unlearnedLines: unlearnedLines, noShieldLines: noShieldLines);
        return true;
    }

    internal bool WouldFindNothingLearned(string setName, IReadOnlyList<PluginSpellInfo> catalog)
    {
        IReadOnlyList<string> lines = _spellSets.TryGetValue(setName, out var found)
            ? found
            : Array.Empty<string>();
        SpellSelectionResult selection = SpellSelector.ResolveForPlayerRequest(catalog, lines, targetTier: _targetTier);
        return selection.IsSuccess && selection.Plan.Count == 0;
    }

    internal bool HasActiveRequester => _current is not null && !_toppingUpBeforeNext;

    internal BuffRequest? ActiveRequester => HasActiveRequester ? _current : null;

    internal bool TryStopActiveRun(uint requesterObjectId, RunStopReason reason)
    {
        if (!HasActiveRequester || _current!.Value.RequesterObjectId != requesterObjectId)
            return false;

        _caster.RequestStop(reason);
        return true;
    }

    // Never touches an idle run or a top-up.
    internal IReadOnlyList<BuffRequest> RequestStopForDisable()
    {
        if (HasActiveRequester)
            _caster.RequestStop(RunStopReason.BotDisabling);
        return _queue.DrainWaiting();
    }

    private IReadOnlyList<ResolvedSpell> ManaUpkeepPlan(IReadOnlyList<PluginSpellInfo> catalog) =>
        SpellSelector.ResolveLenient(catalog, _manaUpkeepLines, SpellTargetKind.Self);

    /// <summary>A null distance means the requester's position could not be determined, not that
    /// they are too far away; the server enforces range either way.</summary>
    private bool IsRequesterInRange(BuffRequest request, Func<uint, double?> distanceToRequester)
    {
        double? distance = distanceToRequester(request.RequesterObjectId);
        return distance is null || RangePolicy.IsInRange(distance.Value, _refusalRangeMeters);
    }

    private void Abandon(
        BuffRequest request, string reply, RefusalReason reason, Action<uint, string, string> sendReply)
    {
        _queue.Complete(request.RequesterObjectId);
        _current = null;
        _stats.RecordRefusal(reason, request.RequesterName);
        sendReply(request.RequesterObjectId, request.RequesterName, reply);
    }
}
