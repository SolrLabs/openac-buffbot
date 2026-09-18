using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Casting;

internal enum CastOutcome
{
    Cast,
    Skipped,

    Failed,
}

// Failure is set only when Outcome is Failed.
internal readonly record struct CastStep(
    string Line, uint SpellId, CastOutcome Outcome, CastFailure? Failure = null);

// A fatal kind (IsFatal) stops the whole run; every other costs only the spell, bounded by
// MaxConsecutivePerSpellFailures.
internal enum CastFailureKind
{
    // IMagicCommands.EvaluateGate refused this spell.
    GateRefused,

    // One call later than GateRefused; MissingComponents is its own kind, below.
    RequestRefused,

    // The completion carried a nonzero weenie error, mapped where known.
    CastFailed,

    // The gate stayed Busy past the timeout.
    TimedOut,

    NoWand,

    WieldFailed,

    // Magic mode was refused or could not be confirmed.
    ModeFailed,

    // The confirmation window elapsed with no landed-cast chat line; usually one dropped cast, not
    // a broken session.
    DidNotLand,

    // A fizzle retries the same spell rather than costing it.
    // A run of consecutive fizzles earns a one-rung tier drop; past FizzleSkipBound on one spell, it's skipped.
    Fizzled,

    // The next spell needed more mana than the bounded mana-upkeep bounce could recover, or
    // nothing was learned to bounce with.
    OutOfMana,

    // The run's own circuit breaker: MaxConsecutivePerSpellFailures in a row means broken, not unlucky.
    TooManyFailures,

    // Recorded up front by Begin, never from an attempted cast.
    NotLearned,

    // Recorded up front like NotLearned: a bane line pulled in when the requester has no shield equipped.
    NoShield,

    // The caster lacks components for this spell, and no lower tier is left to fall back to.
    MissingComponents,
}

internal static class CastFailureKindExtensions
{
    internal static bool IsFatal(this CastFailureKind kind) => kind switch
    {
        CastFailureKind.NoWand => true,
        CastFailureKind.WieldFailed => true,
        CastFailureKind.ModeFailed => true,
        CastFailureKind.OutOfMana => true,
        CastFailureKind.TooManyFailures => true,
        CastFailureKind.MissingComponents => true,
        _ => false,
    };
}

internal readonly record struct CastFailure(CastFailureKind Kind, string Line, string Detail);

internal enum CastRunOutcome
{
    Success,

    // Ran to the plan's end, but at least one step failed.
    Partial,

    // Stopped before the plan's end: a fatal failure, or too many consecutive failures.
    Failed,

    // Stopped between spells, on request; a cast already in the air resolves first.
    Stopped,
}

internal enum RunStopReason
{
    RequesterCancelled,

    BotDisabling,
}

// ComponentCeilingRung is non-null only when a step accepted a lower tier for lack of components.
internal sealed record CastRunResult(
    CastRunOutcome Outcome, IReadOnlyList<CastStep> Steps, CastFailure? Failure,
    RunStopReason? StopReason = null, int? ComponentCeilingRung = null)
{
    // Deliberately excludes Partial.
    internal bool IsSuccess => Outcome == CastRunOutcome.Success;

    internal static CastRunResult Success(
        IReadOnlyList<CastStep> steps, int? componentCeilingRung = null) =>
        new(CastRunOutcome.Success, steps, null, ComponentCeilingRung: componentCeilingRung);

    internal static CastRunResult Partial(
        IReadOnlyList<CastStep> steps, int? componentCeilingRung = null) =>
        new(CastRunOutcome.Partial, steps, null, ComponentCeilingRung: componentCeilingRung);

    internal static CastRunResult Failed(IReadOnlyList<CastStep> steps, CastFailure failure) =>
        new(CastRunOutcome.Failed, steps, failure);

    internal static CastRunResult Stopped(IReadOnlyList<CastStep> steps, RunStopReason reason) =>
        new(CastRunOutcome.Stopped, steps, null, reason);
}

// Host-free: driven by Advance using only this tick's elapsed time and chat batch; never touches IPluginHost.
// A zero WeenieError completion is not proof a targeted cast landed; only the server's own chat line is.
internal sealed class CastStateMachine
{
    private const double DefaultSkipThresholdSeconds = 60d;
    private const double DefaultBusyTimeoutSeconds = 10d;
    private const double DefaultPrepTimeoutSeconds = 5d;

    private const double DefaultConfirmationTimeoutSeconds = 12d;

    // The completion still reports a zero-error use-done for a fizzle; this text is the only signal.
    private const string FizzleText = "Your spell fizzled.";

    private const int MaxManaBouncesPerRun = 24;

    private const int MaxUnproductiveManaBounces = 2;

    internal const double DefaultManaBounceLowWaterFraction = 0.20;

    internal const double DefaultManaBounceHighWaterFraction = 0.80;

    // Whole-chain count, not per spell (unlike FizzleSkipBound).
    private const int ConsecutiveFizzlesBeforeChainTierDrop = 3;

    internal const int DefaultFizzleSkipBound = 6;

    // A successful cast or already-up skip resets the count.
    private const int MaxConsecutivePerSpellFailures = 3;

    // Below this, a mana bounce is tried instead of an even weaker buff.
    private const int MaxAffordabilityStepDowns = 2;

    private enum Phase
    {
        NotStarted,
        WieldingWand,
        EnteringMagicMode,
        WaitingBusy,
        WaitingForConfirmation,

        SplittingComponent, // mid-chain pea-split retry, waiting on IPeaSplitCoordinator.Poll, one poll per tick
    }

    private readonly IMagicCommands _magic;
    private readonly IEnchantmentAutomation _enchantments;
    private readonly IItemAutomation _items;
    private readonly IEquipmentAutomation _equipment;
    private readonly ICombatAutomation _combat;
    private readonly ICharacterInfo _character;

    private readonly IPeaSplitCoordinator? _peaSplitCoordinator;

    // Resolves a live spell's component id to the weenie class id PeaSplitTable is keyed by; null tries the id directly.
    private readonly ISpellCatalog? _catalog;
    private readonly double _skipThresholdSeconds;
    private readonly double _busyTimeoutSeconds;
    private readonly double _prepTimeoutSeconds;
    private readonly double _confirmationTimeoutSeconds;

    // Snapshot of the pending dial below, taken by Begin/BeginTopUp; a console write reaches only the next run.
    private double _manaBounceLowWaterFraction;

    private double _manaBounceHighWaterFraction;

    private bool _tierFallbackEnabled; // off: an unaffordable spell always bounces for mana rather than a weaker tier

    private int _fizzleSkipBound;

    internal bool TierFallbackEnabled { get; set; }

    internal int FizzleSkipBound { get; set; }

    internal double ManaBounceLowWaterFraction { get; set; }

    internal double ManaBounceHighWaterFraction { get; set; }

    // Retail's scarab-only formula for a given school; read live since the carried focus can change
    // mid-run. Null is never scarab-only.
    internal Func<uint, bool>? UsesScarabOnlyFormula { get; set; }

    private readonly Action<string>? _trace;
    private readonly Action<string>? _warn;

    private readonly BotStats? _stats;

    // Spells this run has itself confirmed landed; never fed by the host's own enchantment
    // tracker, which trusts the same optimistic signal this file distrusts.
    private readonly Dictionary<(uint Target, uint Family), (uint SpellId, double SecondsRemaining)>
        _confirmed = [];

    private List<ResolvedSpell> _plan = [];
    private IReadOnlyList<ResolvedSpell> _manaUpkeepPlan = Array.Empty<ResolvedSpell>();

    private HashSet<uint> _manaUpkeepSpellIds = []; // spell ids the affordability gate never applies to

    private int _bounceAttemptsThisRun;

    private int _unproductiveBounces;
    private uint _manaAtLastBounce;

    private int _consecutiveFailures;

    // Hysteresis for the mana window: once mana falls below the low-water mark this stays true,
    // even as mana climbs back past it, until it reaches the high-water mark.
    private bool _manaWindowOpen;

    private int _chainConsecutiveFizzles; // fizzles seen in a row anywhere in the chain, not per spell

    private int _stepConsecutiveFizzles; // fizzles on the step at _index only; a fresh spell starts at zero

    private int _chainTierStepDown; // rungs down from top tier this chain casts at; outlives the spell that earned it

    // The rung a components refusal was last forced down to and accepted at.
    // Unlike _chainTierStepDown, this outlives the run; cleared only on a stock change or re-enable.
    private int? _componentCeilingRung;

    private bool _componentCeilingAppliedThisRun;

    private bool _currentStepHadComponentRefusal;

    private bool _currentStepHadPeaSplitAttempt;

    private string _pendingMissingComponentsDetail = string.Empty;

    private bool _isToppingUp;

    // Every target-facing step always casts, never consulting the ledger; false for idle self-buff or BeginTopUp.
    private bool _isPlayerRequest;

    // Checked once the current step resolves, so a cast in flight is never interrupted; unlike Cancel's immediate stop.
    private bool _stopRequested;
    private RunStopReason _stopReason;

    private List<CastStep> _steps = [];
    private uint _target;
    private string _targetName = string.Empty;
    private int _index;
    private Phase _phase;
    private bool _prepared;
    private bool _preparedForCasting;
    private bool _needsPeaceReturn;
    private uint _wieldTargetObjectId;
    private bool _modeRequestSent;
    private bool _wandSwapAttempted;
    private bool _wandSwapUnavailableLogged;
    private double _prepElapsedSeconds;
    private double _busyElapsedSeconds;
    private bool _observedCastingThisCast;
    private double _confirmationElapsedSeconds;
    private long _revisionBeforeCast;

    // manaBounceLowWaterFraction/manaBounceHighWaterFraction are the mana window's adjustable
    // breakpoints, fractions of ICharacterInfo.MaxMana.
    internal CastStateMachine(
        IMagicCommands magic,
        IEnchantmentAutomation enchantments,
        IItemAutomation items,
        IEquipmentAutomation equipment,
        ICombatAutomation combat,
        ICharacterInfo? character = null,
        double skipThresholdSeconds = DefaultSkipThresholdSeconds,
        double busyTimeoutSeconds = DefaultBusyTimeoutSeconds,
        double prepTimeoutSeconds = DefaultPrepTimeoutSeconds,
        double confirmationTimeoutSeconds = DefaultConfirmationTimeoutSeconds,
        double manaBounceLowWaterFraction = DefaultManaBounceLowWaterFraction,
        double manaBounceHighWaterFraction = DefaultManaBounceHighWaterFraction,
        bool tierFallbackEnabled = true,
        int fizzleSkipBound = DefaultFizzleSkipBound,
        Action<string>? trace = null,
        Action<string>? warn = null,
        BotStats? stats = null,
        IPeaSplitCoordinator? peaSplitCoordinator = null,
        ISpellCatalog? catalog = null)
    {
        _magic = magic;
        _enchantments = enchantments;
        _items = items;
        _equipment = equipment;
        _combat = combat;
        _character = character ?? UnlimitedManaCharacter.Instance;
        _peaSplitCoordinator = peaSplitCoordinator;
        _catalog = catalog;
        _skipThresholdSeconds = skipThresholdSeconds;
        _busyTimeoutSeconds = busyTimeoutSeconds;
        _prepTimeoutSeconds = prepTimeoutSeconds;
        _confirmationTimeoutSeconds = confirmationTimeoutSeconds;
        ManaBounceLowWaterFraction = manaBounceLowWaterFraction;
        ManaBounceHighWaterFraction = manaBounceHighWaterFraction;
        _manaBounceLowWaterFraction = manaBounceLowWaterFraction;
        _manaBounceHighWaterFraction = manaBounceHighWaterFraction;
        TierFallbackEnabled = tierFallbackEnabled;
        FizzleSkipBound = fizzleSkipBound;
        _tierFallbackEnabled = tierFallbackEnabled;
        _fizzleSkipBound = fizzleSkipBound;
        _trace = trace;
        _warn = warn;
        _stats = stats;
    }

    internal bool IsRunning { get; private set; }

    internal bool IsWaitingOnServer => _magic.IsCasting; // may still resolve after this machine stopped waiting locally

    internal string? CurrentSpellLine => IsRunning && _index < _plan.Count ? _plan[_index].Line : null;

    internal uint CurrentSpellId => IsRunning && _index < _plan.Count ? _plan[_index].Spell.SpellId : 0u;

    internal int StepIndex => IsRunning ? _index : 0;

    internal int StepCount => IsRunning ? _plan.Count : 0;

    internal uint CurrentMana => _character.CurrentMana;

    internal uint MaxMana => _character.MaxMana;

    internal uint CurrentHealth => _character.CurrentHealth;

    internal uint MaxHealth => _character.MaxHealth;

    internal uint CurrentStamina => _character.CurrentStamina;

    internal uint MaxStamina => _character.MaxStamina;

    internal int TotalCastsLanded { get; private set; }

    internal int TotalFizzles { get; private set; }

    internal int TotalManaBounces { get; private set; }

    internal int TotalTierStepDowns { get; private set; }

    // manaUpkeepPlan is cast on demand when a step needs more mana than the caster has.
    // unlearnedLines/noShieldLines are recorded up front as failed steps, never attempted.
    internal void Begin(
        IReadOnlyList<ResolvedSpell> plan,
        uint targetObjectId,
        string targetName,
        IReadOnlyList<ResolvedSpell>? manaUpkeepPlan = null,
        bool isPlayerRequest = false,
        IReadOnlyList<string>? unlearnedLines = null,
        IReadOnlyList<string>? noShieldLines = null)
    {
        _plan = new List<ResolvedSpell>(plan);
        _manaUpkeepPlan = manaUpkeepPlan ?? Array.Empty<ResolvedSpell>();
        _manaUpkeepSpellIds = [];
        foreach (ResolvedSpell upkeepSpell in _manaUpkeepPlan)
            _manaUpkeepSpellIds.Add(upkeepSpell.Spell.SpellId);
        _bounceAttemptsThisRun = 0;
        _unproductiveBounces = 0;
        _manaAtLastBounce = 0;
        _consecutiveFailures = 0;
        _target = targetObjectId;
        _targetName = targetName;
        _index = 0;
        _currentStepHadComponentRefusal = false;
        _currentStepHadPeaSplitAttempt = false;
        _componentCeilingAppliedThisRun = false;
        _steps = [];
        if (unlearnedLines is not null)
        {
            foreach (string line in unlearnedLines)
            {
                _steps.Add(new CastStep(
                    line, 0u, CastOutcome.Failed,
                    new CastFailure(CastFailureKind.NotLearned, line, "not learned")));
                _stats?.RecordSpellSkipped(line, "not learned");
            }
        }
        if (noShieldLines is not null)
        {
            foreach (string line in noShieldLines)
            {
                _steps.Add(new CastStep(
                    line, 0u, CastOutcome.Failed,
                    new CastFailure(CastFailureKind.NoShield, line, "no shield equipped")));
                _stats?.RecordSpellSkipped(line, "no shield equipped");
            }
        }
        _phase = Phase.NotStarted;
        _prepared = false;
        _preparedForCasting = false;
        _wieldTargetObjectId = 0;
        _modeRequestSent = false;
        _wandSwapAttempted = false;
        _wandSwapUnavailableLogged = false;
        _prepElapsedSeconds = 0;
        _busyElapsedSeconds = 0;
        _manaWindowOpen = false;
        _chainConsecutiveFizzles = 0;
        _stepConsecutiveFizzles = 0;
        _chainTierStepDown = 0;
        _isToppingUp = false;
        _isPlayerRequest = isPlayerRequest;
        _stopRequested = false;
        _tierFallbackEnabled = TierFallbackEnabled;
        _fizzleSkipBound = FizzleSkipBound;
        _manaBounceLowWaterFraction = ManaBounceLowWaterFraction;
        _manaBounceHighWaterFraction = ManaBounceHighWaterFraction;
        IsRunning = true;
    }

    // Repeated until mana reaches the high mark or the caster stops progressing.
    // Does not reuse Begin: preparation state carries over.
    internal void BeginTopUp(IReadOnlyList<ResolvedSpell> manaUpkeepPlan)
    {
        _manaUpkeepPlan = manaUpkeepPlan;
        _manaUpkeepSpellIds = [];
        foreach (ResolvedSpell upkeepSpell in _manaUpkeepPlan)
            _manaUpkeepSpellIds.Add(upkeepSpell.Spell.SpellId);
        // Starts at 1, matching how the in-chain window counts its own first bounce attempt.
        _bounceAttemptsThisRun = 1;
        _unproductiveBounces = 0;
        _manaAtLastBounce = _character.CurrentMana;
        _consecutiveFailures = 0;
        _target = SelfLedgerTarget;
        _targetName = string.Empty;
        _plan = new List<ResolvedSpell>(manaUpkeepPlan);
        _index = 0;
        _currentStepHadComponentRefusal = false;
        _currentStepHadPeaSplitAttempt = false;
        _componentCeilingAppliedThisRun = false;
        _steps = [];
        _phase = Phase.NotStarted;
        _manaWindowOpen = false;
        _chainConsecutiveFizzles = 0;
        _stepConsecutiveFizzles = 0;
        _chainTierStepDown = 0;
        _isToppingUp = true;
        // Never a player request: nobody asked, so every target-facing rule above is moot.
        _isPlayerRequest = false;
        _stopRequested = false;
        _tierFallbackEnabled = TierFallbackEnabled;
        _fizzleSkipBound = FizzleSkipBound;
        _manaBounceLowWaterFraction = ManaBounceLowWaterFraction;
        _manaBounceHighWaterFraction = ManaBounceHighWaterFraction;
        IsRunning = true;
    }

    // Interrupts immediately, unlike RequestStop: nothing is worth waiting to confirm out of range.
    internal void Cancel()
    {
        if (!IsRunning)
            return;
        IsRunning = false;
        if (_preparedForCasting)
            _needsPeaceReturn = true;
    }

    // Unlike Cancel, a cast already sent is let through to its own confirmation first.
    internal void RequestStop(RunStopReason reason)
    {
        _stopRequested = true;
        _stopReason = reason;
    }

    // chatMessages is this tick's already-captured batch, never captured again here.
    // Returns the closing result once the run finishes or fails, null while in progress or idle.
    internal CastRunResult? Advance(double deltaSeconds, IReadOnlyList<PluginChatMessage> chatMessages)
    {
        DecayConfirmed(deltaSeconds);

        if (!IsRunning)
        {
            TickIdle();
            return null;
        }

        while (_index < _plan.Count || (_isToppingUp && ShouldRepeatTopUp()))
        {
            if (_index >= _plan.Count)
            {
                // The top-up's plan ran to its end without reaching the high mark; cast it again from the top.
                bool giveUp = NoteBounceAndShouldGiveUp();
                _trace?.Invoke(
                    $"topping up mana ({_character.CurrentMana}/{_character.MaxMana}) - "
                    + $"bounce {_bounceAttemptsThisRun}"
                    + (giveUp ? " - not gaining, stopping the top-up" : string.Empty));
                if (giveUp)
                    break;
                _index = 0;
                _currentStepHadComponentRefusal = false;
                _currentStepHadPeaSplitAttempt = false;
                continue;
            }

            ResolvedSpell current = _plan[_index];

            switch (_phase)
            {
                case Phase.NotStarted:
                    // Applied before this step is gated or sent, so a shortage already learned is never retried.
                    ApplyChainTierOffsetToCurrentIndex();
                    current = _plan[_index];

                    if (_stopRequested)
                    {
                        return Finish(CastRunResult.Stopped(_steps, _stopReason));
                    }

                    if (IsAlreadyUp(current))
                    {
                        string skipDetail = current.Spell.IsSelfTargeted ? "on self" : $"on {EffectiveTarget(current)}";
                        _trace?.Invoke($"skip {current.Line}: already up {skipDetail}");
                        _steps.Add(new CastStep(current.Line, current.Spell.SpellId, CastOutcome.Skipped));
                        _stats?.RecordSpellSkipped(current.Line, "already up");
                        _index++;
                        _currentStepHadComponentRefusal = false;
                        _currentStepHadPeaSplitAttempt = false;
                        _chainConsecutiveFizzles = 0;
                        _stepConsecutiveFizzles = 0;
                        _consecutiveFailures = 0;
                        ApplyChainTierOffsetToCurrentIndex();
                        continue;
                    }

                    if (!IsManaUpkeepSpell(current.Spell))
                    {
                        // Once open, the window bounces unconditionally to the high mark; the affordability
                        // lever only gets a turn while the window is closed.
                        bool windowRequiresBounce = ManaWindowRequiresBounce();
                        if (windowRequiresBounce || SpellExceedsCurrentMana(current.Spell))
                        {
                            if (_tierFallbackEnabled && !windowRequiresBounce
                                && TryStepDownForAffordability(current) is { } affordableTier)
                            {
                                _trace?.Invoke(
                                    $"{current.Line}: can't afford {current.Spell.Name} "
                                    + $"({_character.CurrentMana}/{current.Spell.ManaCost} mana) — "
                                    + $"casting {affordableTier.Name} instead of bouncing for it");
                                _plan[_index] = current with { Spell = affordableTier };
                                continue;
                            }

                            _manaWindowOpen = true;
                            CastRunResult? bounceStopped = TryQueueManaBounce(current.Line);
                            if (bounceStopped is not null)
                                return bounceStopped;
                            continue;
                        }
                    }

                    // _prepared alone isn't trustworthy: a peace return can drop combat mode without
                    // clearing it. _modeRequestSent must reset too, since a pea split returns to peace the same way.
                    if (!_prepared || _combat.Snapshot.Mode != PluginCombatMode.Magic)
                    {
                        _prepared = false;
                        _modeRequestSent = false;
                        _phase = Phase.WieldingWand;
                        _prepElapsedSeconds = 0;
                        continue;
                    }

                    PluginCastGate gate = current.Spell.IsSelfTargeted
                        ? _magic.EvaluateGate(current.Spell.SpellId)
                        : _magic.EvaluateGate(current.Spell.SpellId, EffectiveTarget(current));
                    _trace?.Invoke($"gate {current.Line} -> {gate}");

                    if (gate == PluginCastGate.Ready)
                    {
                        PluginCastRequestResult sent = current.Spell.IsSelfTargeted
                            ? _magic.RequestCast(current.Spell.SpellId)
                            : _magic.RequestCast(current.Spell.SpellId, EffectiveTarget(current));
                        _trace?.Invoke($"cast {current.Line} -> {sent}");
                        if (sent != PluginCastRequestResult.Sent)
                        {
                            // Client-side refusal, before anything reaches the wire, so stepping down a rung costs nothing.
                            if (sent == PluginCastRequestResult.MissingComponents)
                            {
                                if (!TryRecoverFromMissingComponents(
                                    current, sent.ToString(), out CastRunResult? recoveryResult))
                                    return recoveryResult;
                                continue;
                            }

                            if (!HandleSpellFailure(
                                current,
                                new CastFailure(CastFailureKind.RequestRefused, current.Line, sent.ToString()),
                                out CastRunResult? requestRefusedResult))
                                return requestRefusedResult;
                            continue;
                        }

                        if (_currentStepHadComponentRefusal)
                        {
                            int acceptedRung = IndexInLadder(current);
                            if (acceptedRung > 0)
                                SetComponentCeiling(acceptedRung, current.Line, current.Spell);
                            _currentStepHadComponentRefusal = false;
                            _currentStepHadPeaSplitAttempt = false;
                            _componentCeilingAppliedThisRun = true;
                        }

                        _revisionBeforeCast = _magic.LastCompletion.Revision;
                        _observedCastingThisCast = false;
                        _confirmationElapsedSeconds = 0;
                        _phase = Phase.WaitingForConfirmation;
                        return null;
                    }

                    if (gate == PluginCastGate.Busy)
                    {
                        _busyElapsedSeconds = 0;
                        _phase = Phase.WaitingBusy;
                        return null;
                    }

                    if (!HandleSpellFailure(
                        current,
                        new CastFailure(CastFailureKind.GateRefused, current.Line, gate.ToString()),
                        out CastRunResult? gateRefusedResult))
                        return gateRefusedResult;
                    continue;

                case Phase.WieldingWand:
                    if (!TickWieldingWand(current, deltaSeconds, out CastRunResult? wieldFailure))
                        return wieldFailure;
                    continue;

                case Phase.EnteringMagicMode:
                    if (!TickEnteringMagicMode(current, deltaSeconds, out CastRunResult? modeFailure))
                        return modeFailure;
                    continue;

                case Phase.WaitingBusy:
                    _busyElapsedSeconds += deltaSeconds;
                    PluginCastGate retryGate = current.Spell.IsSelfTargeted
                        ? _magic.EvaluateGate(current.Spell.SpellId)
                        : _magic.EvaluateGate(current.Spell.SpellId, EffectiveTarget(current));

                    if (retryGate == PluginCastGate.Ready)
                    {
                        _phase = Phase.NotStarted;
                        continue;
                    }

                    if (retryGate != PluginCastGate.Busy)
                    {
                        if (!HandleSpellFailure(
                            current,
                            new CastFailure(CastFailureKind.GateRefused, current.Line, retryGate.ToString()),
                            out CastRunResult? busyGateRefusedResult))
                            return busyGateRefusedResult;
                        continue;
                    }

                    if (_busyElapsedSeconds >= _busyTimeoutSeconds)
                    {
                        _trace?.Invoke($"timeout waiting on busy for {current.Line}");
                        if (!HandleSpellFailure(
                            current,
                            new CastFailure(CastFailureKind.TimedOut, current.Line, "busy too long"),
                            out CastRunResult? busyTimedOutResult))
                            return busyTimedOutResult;
                        continue;
                    }

                    return null;

                case Phase.SplittingComponent:
                    PeaSplitPollResult splitPoll = _peaSplitCoordinator!.Poll(deltaSeconds);
                    if (splitPoll == PeaSplitPollResult.Pending)
                        return null;

                    if (splitPoll == PeaSplitPollResult.Confirmed)
                    {
                        _trace?.Invoke($"{current.Line}: pea split confirmed - retrying {current.Spell.Name}");
                        _phase = Phase.NotStarted;
                        continue;
                    }

                    // Failed, or a defensive Idle that should never happen right after TryStart accepted it.
                    if (!FallBackFromMissingComponents(
                        current, _pendingMissingComponentsDetail, out CastRunResult? splitFailedResult))
                        return splitFailedResult;
                    continue;

                case Phase.WaitingForConfirmation:
                    if (_magic.IsCasting)
                        _observedCastingThisCast = true;

                    string confirmationTargetName = current.Spell.IsSelfTargeted
                        ? "yourself"
                        : current.TargetOverride?.Name ?? _targetName;
                    if (FindConfirmation(chatMessages, current.Spell.Name, confirmationTargetName) is { } confirmedText)
                    {
                        _trace?.Invoke($"confirmed {current.Line} -> \"{confirmedText}\"");
                        _enchantments.ReportCast(
                            EffectiveTarget(current), current.Spell.SpellId, current.Spell.DurationSeconds);
                        Remember(current);
                        _steps.Add(new CastStep(current.Line, current.Spell.SpellId, CastOutcome.Cast));
                        TotalCastsLanded++;
                        // Upkeep is decided per spell; everything else falls back to whether the run began on a player's own ask.
                        _stats?.RecordCastLanded(
                            current.Line, confirmationTargetName,
                            isPlayerRequest: _isPlayerRequest && !current.Spell.IsSelfTargeted);
                        _index++;
                        _chainConsecutiveFizzles = 0;
                        _stepConsecutiveFizzles = 0;
                        _consecutiveFailures = 0;
                        _phase = Phase.NotStarted;
                        ApplyChainTierOffsetToCurrentIndex();
                        continue;
                    }

                    if (FindFizzle(chatMessages))
                    {
                        // A fizzle alone never counts toward the consecutive-failure bound.
                        _chainConsecutiveFizzles++;
                        _stepConsecutiveFizzles++;
                        TotalFizzles++;
                        _stats?.RecordFizzle(current.Line);
                        _trace?.Invoke(
                            $"fizzled {current.Line} ({_chainConsecutiveFizzles} in a row this "
                            + $"chain, {_stepConsecutiveFizzles} on this spell) - retrying");

                        if (_tierFallbackEnabled
                            && _chainConsecutiveFizzles % ConsecutiveFizzlesBeforeChainTierDrop == 0)
                        {
                            _chainTierStepDown++;
                            TotalTierStepDowns++;
                            ApplyChainTierOffsetToCurrentIndex();
                            _trace?.Invoke(
                                $"{_chainConsecutiveFizzles} fizzles in a row this chain - "
                                + $"dropping to {_plan[_index].Spell.Name} for the rest of it");
                        }

                        // Applies regardless of whether the drop above just ran.
                        if (_stepConsecutiveFizzles >= _fizzleSkipBound)
                        {
                            ResolvedSpell fizzledOut = _plan[_index];
                            _trace?.Invoke(
                                $"{fizzledOut.Line} fizzled {_stepConsecutiveFizzles} times in a "
                                + "row - skipping this spell");
                            _steps.Add(new CastStep(
                                fizzledOut.Line, fizzledOut.Spell.SpellId, CastOutcome.Failed,
                                new CastFailure(
                                    CastFailureKind.Fizzled, fizzledOut.Line,
                                    $"fizzled {_stepConsecutiveFizzles} times in a row")));
                            // Not RecordOtherAttemptFailure: each fizzle already counted its own attempt.
                            _stats?.RecordSpellSkipped(
                                fizzledOut.Line, $"fizzled {_stepConsecutiveFizzles} times in a row");
                            _index++;
                            _stepConsecutiveFizzles = 0;
                            _phase = Phase.NotStarted;
                            ApplyChainTierOffsetToCurrentIndex();
                            continue;
                        }

                        _phase = Phase.NotStarted;
                        continue;
                    }

                    PluginCastCompletion completion = _magic.LastCompletion;
                    if (completion.Revision != _revisionBeforeCast)
                    {
                        _trace?.Invoke(
                            $"completion {current.Line} -> success={completion.IsSuccess} "
                            + $"error={completion.WeenieError}");

                        if (completion.WeenieError != 0)
                        {
                            // Same reagent shortage a client-side refusal catches up front; takes the same step-down path.
                            if (completion.WeenieError == WeenieErrorReplies.NoComponentsCode)
                            {
                                if (!TryRecoverFromMissingComponents(
                                    current,
                                    WeenieErrorReplies.Describe(completion.WeenieError),
                                    out CastRunResult? recoveryResult))
                                    return recoveryResult;
                                continue;
                            }

                            if (!HandleSpellFailure(
                                current,
                                new CastFailure(
                                    CastFailureKind.CastFailed,
                                    current.Line,
                                    WeenieErrorReplies.Describe(completion.WeenieError)),
                                out CastRunResult? castFailedResult))
                                return castFailedResult;
                            continue;
                        }

                        if (!_observedCastingThisCast)
                        {
                            _warn?.Invoke(
                                $"completion for {current.Line} arrived without ever observing "
                                + "IsCasting — possible server-side drop; deciding by server "
                                + "confirmation instead");
                        }

                        // Consumed: don't re-warn every tick while still waiting on the chat line.
                        _revisionBeforeCast = completion.Revision;
                    }

                    _confirmationElapsedSeconds += deltaSeconds;
                    if (_confirmationElapsedSeconds >= _confirmationTimeoutSeconds)
                    {
                        _trace?.Invoke($"no confirmation for {current.Line} within the window");
                        if (!HandleSpellFailure(
                            current,
                            new CastFailure(
                                CastFailureKind.DidNotLand, current.Line, "no confirmation received"),
                            out CastRunResult? didNotLandResult))
                            return didNotLandResult;
                        continue;
                    }

                    return null;

                default:
                    throw new InvalidOperationException($"Unknown phase: {_phase}.");
            }
        }

        bool anyFailed = false;
        foreach (CastStep step in _steps)
        {
            if (step.Outcome != CastOutcome.Failed)
                continue;
            anyFailed = true;
            break;
        }

        // Only set when this run itself needed the ceiling; it stays set for the next run regardless.
        int? componentCeilingForClosing = _componentCeilingAppliedThisRun ? _componentCeilingRung : null;
        return Finish(anyFailed
            ? CastRunResult.Partial(_steps, componentCeilingForClosing)
            : CastRunResult.Success(_steps, componentCeilingForClosing));
    }

    // A fatal kind ends the run here; anything else costs only this step, unless
    // MaxConsecutivePerSpellFailures have now landed back-to-back. False means the caller must stop this tick.
    private bool HandleSpellFailure(ResolvedSpell current, CastFailure failure, out CastRunResult? result)
    {
        if (failure.Kind.IsFatal())
        {
            result = Finish(CastRunResult.Failed(_steps, failure));
            return false;
        }

        _trace?.Invoke($"{current.Line} failed ({failure.Kind}: {failure.Detail}) - skipping this spell");
        _steps.Add(new CastStep(current.Line, current.Spell.SpellId, CastOutcome.Failed, failure));
        _stats?.RecordOtherAttemptFailure(current.Line);
        _index++;
        _currentStepHadComponentRefusal = false;
        _currentStepHadPeaSplitAttempt = false;
        // Always a non-fizzle failure here, so any fizzle streak this chain had is broken.
        _chainConsecutiveFizzles = 0;
        _stepConsecutiveFizzles = 0;
        _phase = Phase.NotStarted;
        _consecutiveFailures++;
        ApplyChainTierOffsetToCurrentIndex();

        if (_consecutiveFailures >= MaxConsecutivePerSpellFailures)
        {
            result = Finish(CastRunResult.Failed(
                _steps,
                new CastFailure(
                    CastFailureKind.TooManyFailures,
                    current.Line,
                    $"{MaxConsecutivePerSpellFailures} spells in a row didn't land, something's wrong")));
            return false;
        }

        result = null;
        return true;
    }

    // Shared by a client-side and a server-side components refusal, naming the same shortage
    // caught at different points. Pea splitting gets first refusal, once per step.
    private bool TryRecoverFromMissingComponents(
        ResolvedSpell current, string detail, out CastRunResult? result)
    {
        if (!_currentStepHadPeaSplitAttempt
            && _peaSplitCoordinator is not null
            && TryStartPeaSplitForMissingComponent(current))
        {
            _currentStepHadPeaSplitAttempt = true;
            _pendingMissingComponentsDetail = detail;
            // A split leaves magic mode for peace, so the caster is no longer prepared once it resolves.
            _prepared = false;
            _modeRequestSent = false;
            _phase = Phase.SplittingComponent;
            result = null;
            return true;
        }

        return FallBackFromMissingComponents(current, detail, out result);
    }

    private bool TryStartPeaSplitForMissingComponent(ResolvedSpell current)
    {
        bool useScarabOnly = UsesScarabOnlyFormula?.Invoke(current.Spell.School) ?? false;
        foreach (uint componentId in ComponentReportBuilder.ComponentIdsOf(current.Spell, useScarabOnly))
        {
            if (TryStartSplitForWeenieId(componentId, current.Line))
                return true;

            // Tried directly first; a live spell's own formula names components in a different id space.
            if (_catalog is not null
                && _catalog.TryGetComponent(componentId, out PluginSpellComponentInfo info)
                && info.WeenieClassId != componentId
                && TryStartSplitForWeenieId(info.WeenieClassId, current.Line))
                return true;
        }
        return false;
    }

    private bool TryStartSplitForWeenieId(uint componentWeenieId, string line)
    {
        if (!PeaSplitTable.TryGetRecipeForComponent(componentWeenieId, out _))
            return false;
        return _peaSplitCoordinator!.TryStart(componentWeenieId, $"missing components for {line}");
    }

    private bool FallBackFromMissingComponents(
        ResolvedSpell current, string detail, out CastRunResult? result)
    {
        if (TryStepDownForComponents(current) is { } lowerTier)
        {
            _trace?.Invoke(
                $"{current.Line}: missing components for {current.Spell.Name} "
                + $"- retrying {lowerTier.Name} at no cost");
            _plan[_index] = current with { Spell = lowerTier };
            _currentStepHadComponentRefusal = true;
            _phase = Phase.NotStarted;
            result = null;
            return true;
        }

        return HandleSpellFailure(
            current, new CastFailure(CastFailureKind.MissingComponents, current.Line, detail), out result);
    }

    // False (with a possibly-null result) means the caller should stop looping this tick.
    private bool TickWieldingWand(ResolvedSpell current, double deltaSeconds, out CastRunResult? result)
    {
        IReadOnlyList<PluginInventoryItem> items = _items.CaptureOwnedItems();
        PluginInventoryItem? wielded = FindWieldedWand(items);

        // An accepted Equip is not a landed one: the wand in hand a tick later may still be the old one, mid-swap.
        if (_wieldTargetObjectId != 0u)
        {
            if (wielded is { } landed && landed.ObjectId == _wieldTargetObjectId)
            {
                _trace?.Invoke($"wand equipped: {landed.Name}");
                _wieldTargetObjectId = 0u;
                _wandSwapAttempted = true;
                _phase = Phase.EnteringMagicMode;
                _prepElapsedSeconds = 0;
                result = null;
                return true;
            }

            if (wielded is null)
            {
                // Nothing in hand, unlike the case below; worth asking again rather than only watching.
                PluginEquipmentCommandResult retry = _equipment.Equip(_wieldTargetObjectId);
                if (retry.Status != PluginEquipmentCommandStatus.Busy)
                    _trace?.Invoke($"wield (still waiting to land) -> {retry.Status}");
            }

            _prepElapsedSeconds += deltaSeconds;
            if (_prepElapsedSeconds < _prepTimeoutSeconds)
            {
                result = null;
                return false;
            }

            _wandSwapAttempted = true;
            _wieldTargetObjectId = 0u;
            if (wielded is { } fallback)
            {
                // The swap was an optimization, never a precondition; cast through whatever is actually in hand.
                _trace?.Invoke($"gave up waiting for it to land — casting through {fallback.Name}");
                _phase = Phase.EnteringMagicMode;
                _prepElapsedSeconds = 0;
                result = null;
                return true;
            }

            result = Finish(CastRunResult.Failed(
                _steps,
                new CastFailure(CastFailureKind.WieldFailed, current.Line, "timed out waiting to wield")));
            return false;
        }

        if (wielded is { } w)
        {
            // Evaluated once per run, here only: a spell already in flight must never be interrupted for a swap.
            if (!_wandSwapAttempted && FindBestWand(items) is { } preferred
                && preferred.ObjectId != w.ObjectId)
            {
                PluginEquipmentCommandResult swap = _equipment.Equip(preferred.ObjectId);
                if (swap.Status != PluginEquipmentCommandStatus.Unavailable
                    || !_wandSwapUnavailableLogged)
                {
                    // Logged once; a host that can never equip would otherwise flood this every tick.
                    _wandSwapUnavailableLogged |= swap.Status == PluginEquipmentCommandStatus.Unavailable;
                    _trace?.Invoke($"swap wand {w.Name} -> {preferred.Name}: {swap.Status}");
                }

                if (swap.Accepted)
                {
                    _wieldTargetObjectId = preferred.ObjectId;
                    _prepElapsedSeconds = 0;
                    result = null;
                    return false; // the wait above resolves it, once the swap actually lands.
                }

                // Unavailable and Busy aren't real answers about the item, unlike Refused/InvalidItem.
                if (swap.Status is PluginEquipmentCommandStatus.Unavailable
                    or PluginEquipmentCommandStatus.Busy)
                {
                    _prepElapsedSeconds += deltaSeconds;
                    if (_prepElapsedSeconds < _prepTimeoutSeconds)
                    {
                        result = null;
                        return false;
                    }
                    _trace?.Invoke(
                        $"swap wand {w.Name} -> {preferred.Name}: gave up waiting for the "
                        + "equipment surface");
                }

                _wandSwapAttempted = true;
            }

            _trace?.Invoke($"wand already wielded: {w.Name}");
            _phase = Phase.EnteringMagicMode;
            _prepElapsedSeconds = 0;
            result = null;
            return true;
        }

        if (FindBestWand(items) is not { } wand)
        {
            result = Finish(CastRunResult.Failed(
                _steps,
                new CastFailure(CastFailureKind.NoWand, current.Line, "no wand in inventory")));
            return false;
        }

        PluginEquipmentCommandResult equip = _equipment.Equip(wand.ObjectId);
        _trace?.Invoke($"wield {wand.Name} (mana conversion unknown) -> {equip.Status}");

        if (equip.Accepted)
        {
            _wieldTargetObjectId = wand.ObjectId;
            _prepElapsedSeconds = 0;
            result = null;
            return false;
        }

        if (equip.Status == PluginEquipmentCommandStatus.Busy)
        {
            // Busy is the host's own in-flight equip still settling, not a real refusal.
            _prepElapsedSeconds += deltaSeconds;
            if (_prepElapsedSeconds < _prepTimeoutSeconds)
            {
                result = null;
                return false;
            }
        }

        result = Finish(CastRunResult.Failed(
            _steps,
            new CastFailure(
                CastFailureKind.WieldFailed,
                current.Line,
                equip.Notice ?? equip.Status.ToString())));
        return false;
    }

    private bool TickEnteringMagicMode(ResolvedSpell current, double deltaSeconds, out CastRunResult? result)
    {
        if (_combat.Snapshot.Mode == PluginCombatMode.Magic)
        {
            _trace?.Invoke("magic mode confirmed");
            _prepared = true;
            _preparedForCasting = true;
            _phase = Phase.NotStarted;
            result = null;
            return true;
        }

        if (!_modeRequestSent)
        {
            PluginCombatCommandResult entered = _combat.EnterMode(PluginCombatMode.Magic);
            _trace?.Invoke($"mode Magic -> {entered.Status}");
            if (!entered.Accepted)
            {
                result = Finish(CastRunResult.Failed(
                    _steps,
                    new CastFailure(
                        CastFailureKind.ModeFailed,
                        current.Line,
                        entered.Notice ?? entered.Status.ToString())));
                return false;
            }

            _modeRequestSent = true;
            _prepElapsedSeconds = 0;
            result = null;
            return false;
        }

        _prepElapsedSeconds += deltaSeconds;
        if (_prepElapsedSeconds >= _prepTimeoutSeconds)
        {
            result = Finish(CastRunResult.Failed(
                _steps,
                new CastFailure(
                    CastFailureKind.ModeFailed, current.Line, "timed out entering magic mode")));
            return false;
        }

        result = null;
        return false;
    }

    private void TickIdle()
    {
        if (!_needsPeaceReturn)
            return;

        if (_magic.IsCasting)
        {
            // The server may still be resolving a cast this machine gave up waiting on; peace now would race it.
            return;
        }

        PluginCombatCommandResult result = _combat.EnterMode(PluginCombatMode.Peace);
        _trace?.Invoke($"peace -> {result.Status}");
        _needsPeaceReturn = false; // best effort: one attempt per drain, not a retry loop
    }

    private CastRunResult Finish(CastRunResult result)
    {
        IsRunning = false;
        if (_preparedForCasting)
            _needsPeaceReturn = true;
        return result;
    }

    // Independent of the mana window.
    private bool SpellExceedsCurrentMana(PluginSpellInfo spell) =>
        _character.CurrentMana < (uint)Math.Max(0, spell.ManaCost);

    // Hysteresis: once below the low mark, stays true until the high mark is reached.
    private bool ManaWindowRequiresBounce()
    {
        if (_manaWindowOpen)
        {
            if (_character.CurrentMana >= HighWaterMana())
            {
                _manaWindowOpen = false;
                return false;
            }
            return true;
        }

        if (_character.CurrentMana < LowWaterMana())
        {
            _manaWindowOpen = true;
            return true;
        }

        return false;
    }

    // Asked once per finished chain, unlike the mid-chain hysteresis in ManaWindowRequiresBounce.
    internal bool NeedsManaTopUp => _character.CurrentMana < HighWaterMana();

    private uint LowWaterMana() => MarkOf(_manaBounceLowWaterFraction);

    private uint HighWaterMana() => MarkOf(_manaBounceHighWaterFraction);

    private uint MarkOf(double fraction) => (uint)(_character.MaxMana * fraction);

    private bool ShouldRepeatTopUp() =>
        _manaUpkeepPlan.Count > 0
        && _character.CurrentMana < HighWaterMana()
        && _character.CurrentStamina > 0
        && _unproductiveBounces < MaxUnproductiveManaBounces
        && _bounceAttemptsThisRun <= MaxManaBouncesPerRun;

    // True means this bounce, and the one before it, both gained nothing.
    private bool NoteBounceAndShouldGiveUp()
    {
        TotalManaBounces++;
        uint mana = _character.CurrentMana;
        // No "before" to report on the very first call of a run.
        if (_bounceAttemptsThisRun > 0)
            _stats?.RecordManaBounce(_manaAtLastBounce, mana);
        if (_bounceAttemptsThisRun > 0 && mana <= _manaAtLastBounce)
            _unproductiveBounces++;
        else
            _unproductiveBounces = 0;
        _manaAtLastBounce = mana;
        _bounceAttemptsThisRun++;
        return _unproductiveBounces >= MaxUnproductiveManaBounces
            || _bounceAttemptsThisRun > MaxManaBouncesPerRun;
    }

    // The affordability gate must not apply to these, or the check would block its own fix.
    private bool IsManaUpkeepSpell(PluginSpellInfo spell) => _manaUpkeepSpellIds.Contains(spell.SpellId);

    private static int IndexInLadder(ResolvedSpell current)
    {
        IReadOnlyList<PluginSpellInfo> ladder = current.LearnedTiersDescending;
        for (int i = 0; i < ladder.Count; i++)
        {
            if (ladder[i].SpellId == current.Spell.SpellId)
                return i;
        }
        return -1;
    }

    // Moves the spell at _index down to the deeper of the fizzle tier-drop and component-ceiling
    // floors, so the two levers compose rather than fight.
    private void ApplyChainTierOffsetToCurrentIndex()
    {
        int ceilingFloor = _componentCeilingRung ?? 0;
        int floor = Math.Max(_chainTierStepDown, ceilingFloor);
        if (floor <= 0 || _index >= _plan.Count)
            return;

        ResolvedSpell next = _plan[_index];
        IReadOnlyList<PluginSpellInfo> ladder = next.LearnedTiersDescending;
        if (ladder.Count == 0)
            return;

        int currentIndex = IndexInLadder(next);
        int floorIndex = Math.Min(floor, ladder.Count - 1);
        int targetIndex = Math.Max(currentIndex < 0 ? 0 : currentIndex, floorIndex);
        if (ladder[targetIndex].SpellId != next.Spell.SpellId)
        {
            _plan[_index] = next with { Spell = ladder[targetIndex] };
            // Worth reporting even without a fresh refusal this run, if the ceiling is the deeper floor.
            if (ceilingFloor > 0 && ceilingFloor >= _chainTierStepDown)
                _componentCeilingAppliedThisRun = true;
        }
    }

    // Spends no attempt; null when nothing lower is affordable or learned.
    private PluginSpellInfo? TryStepDownForAffordability(ResolvedSpell current)
    {
        int index = IndexInLadder(current);
        if (index < 0)
            return null;

        IReadOnlyList<PluginSpellInfo> ladder = current.LearnedTiersDescending;
        int floor = Math.Min(index + MaxAffordabilityStepDowns, ladder.Count - 1);
        for (int i = index + 1; i <= floor; i++)
        {
            if ((uint)Math.Max(0, ladder[i].ManaCost) <= _character.CurrentMana)
                return ladder[i];
        }

        return null;
    }

    // Unconditional and unbounded, unlike the affordability lever, since the refusal is free (client-side).
    private static PluginSpellInfo? TryStepDownForComponents(ResolvedSpell current)
    {
        int index = IndexInLadder(current);
        if (index < 0)
            return null;

        IReadOnlyList<PluginSpellInfo> ladder = current.LearnedTiersDescending;
        return index + 1 < ladder.Count ? ladder[index + 1] : null;
    }

    // Only ever tightens; a shallower rung changes nothing.
    private void SetComponentCeiling(int rung, string line, PluginSpellInfo tier)
    {
        if (_componentCeilingRung is int existing && existing >= rung)
            return;

        _componentCeilingRung = rung;
        _trace?.Invoke(
            $"component ceiling: {line} only had components for {tier.Name} — capping later "
            + "steps to this tier or lower until the reagent stock changes or this character is "
            + "re-enabled");
    }

    internal int? ComponentCeilingRung => _componentCeilingRung;

    internal void ClearComponentCeiling(string reason)
    {
        if (_componentCeilingRung is null)
            return;

        _componentCeilingRung = null;
        _trace?.Invoke($"component ceiling cleared: {reason}");
    }

    // Splices the resolved mana-upkeep pair in ahead of blockedLine so the normal machinery casts it.
    // Returns the run's closing failure once bouncing cannot help; otherwise null to retry from the spliced step.
    private CastRunResult? TryQueueManaBounce(string blockedLine)
    {
        if (_manaUpkeepPlan.Count == 0)
        {
            return Finish(CastRunResult.Failed(
                _steps,
                new CastFailure(
                    CastFailureKind.OutOfMana,
                    blockedLine,
                    "I'm low on mana and don't know a spell to get it back")));
        }

        if (_character.CurrentStamina == 0)
        {
            _trace?.Invoke($"stamina exhausted (0/{_character.MaxStamina}) - can't bounce for {blockedLine}");
            return Finish(CastRunResult.Failed(
                _steps,
                new CastFailure(
                    CastFailureKind.OutOfMana,
                    blockedLine,
                    "I'm low on mana and out of stamina to convert into more")));
        }

        if (NoteBounceAndShouldGiveUp())
        {
            return Finish(CastRunResult.Failed(
                _steps,
                new CastFailure(
                    CastFailureKind.OutOfMana,
                    blockedLine,
                    "I tried bouncing my mana back up and it's still not enough")));
        }

        _trace?.Invoke(
            $"mana low for {blockedLine} ({_character.CurrentMana}/{_character.MaxMana}) "
            + $"- bounce {_bounceAttemptsThisRun}");
        _plan.InsertRange(_index, _manaUpkeepPlan);
        return null;
    }

    /// <summary>Stands in for a real character when a caller has no opinion about mana: mana
    /// never falls short.</summary>
    private sealed class UnlimitedManaCharacter : ICharacterInfo
    {
        internal static readonly UnlimitedManaCharacter Instance = new();

        public bool IsInWorld => true;
        public uint ObjectId => 0;
        public uint CurrentHealth => uint.MaxValue;
        public uint MaxHealth => uint.MaxValue;
        public uint CurrentStamina => uint.MaxValue;
        public uint MaxStamina => uint.MaxValue;
        public uint CurrentMana => uint.MaxValue;
        public uint MaxMana => uint.MaxValue;
        public IReadOnlyList<PluginSkillInfo> Skills => Array.Empty<PluginSkillInfo>();
        public IReadOnlyList<PluginAttributeInfo> Attributes => Array.Empty<PluginAttributeInfo>();
        public IReadOnlyList<PluginActiveEnchantment> ActiveEnchantments => Array.Empty<PluginActiveEnchantment>();

        public bool TryGetSkill(uint skillId, out PluginSkillInfo skill)
        {
            skill = default;
            return false;
        }
    }

    /// <summary>Sentinel ledger key for a self-targeted step; no real object id is ever 0.</summary>
    private const uint SelfLedgerTarget = 0u;

    // The plan's own target, or a bane step's shield override.
    private uint EffectiveTarget(ResolvedSpell current) =>
        current.TargetOverride?.ObjectId ?? _target;

    private uint LedgerTarget(ResolvedSpell current) =>
        current.Spell.IsSelfTargeted ? SelfLedgerTarget : EffectiveTarget(current);

    // A player request's target-facing steps never consult the ledger; self-targeted steps and idle/top-up runs still do.
    private bool IsAlreadyUp(ResolvedSpell current)
    {
        uint ledgerTarget = LedgerTarget(current);
        if (_isPlayerRequest && ledgerTarget != SelfLedgerTarget)
            return false;

        if (!_confirmed.TryGetValue((ledgerTarget, current.Spell.Family), out var entry))
            return false;

        return entry.SecondsRemaining > _skipThresholdSeconds;
    }

    private void Remember(ResolvedSpell current) =>
        _confirmed[(LedgerTarget(current), current.Spell.Family)] =
            (current.Spell.SpellId, current.Spell.DurationSeconds);

    private void DecayConfirmed(double deltaSeconds)
    {
        if (_confirmed.Count == 0)
            return;

        List<(uint Target, uint Family)>? expired = null;
        foreach ((uint Target, uint Family) key in _confirmed.Keys.ToArray())
        {
            (uint SpellId, double SecondsRemaining) entry = _confirmed[key];
            double remaining = entry.SecondsRemaining - deltaSeconds;
            if (remaining <= 0d)
            {
                (expired ??= []).Add(key);
                continue;
            }
            _confirmed[key] = (entry.SpellId, remaining);
        }

        if (expired is null)
            return;
        foreach ((uint Target, uint Family) key in expired)
            _confirmed.Remove(key);
    }

    // ACE sends one of two shapes, never both: "You cast SPELL on TARGET" (or "on yourself"), or,
    // for a vital-restore like Revitalize Self, "You cast SPELL and restore N points of your VITAL."
    private static string? FindConfirmation(
        IReadOnlyList<PluginChatMessage> messages, string spellName, string targetName)
    {
        string enchantmentPrefix = $"You cast {spellName} on {targetName}";
        string vitalRestorePrefix = $"You cast {spellName} and restore ";
        foreach (PluginChatMessage message in messages)
        {
            if (message.Kind != (int)ChatKind.System)
                continue;
            if (message.Text.StartsWith(enchantmentPrefix, StringComparison.Ordinal)
                || message.Text.StartsWith(vitalRestorePrefix, StringComparison.Ordinal))
                return message.Text;
        }
        return null;
    }

    // Delivered as ChatKind.System; the completion itself cannot tell us this.
    private static bool FindFizzle(IReadOnlyList<PluginChatMessage> messages)
    {
        foreach (PluginChatMessage message in messages)
        {
            if (message.Kind != (int)ChatKind.System)
                continue;
            if (message.Text == FizzleText)
                return true;
        }
        return false;
    }

    private static PluginInventoryItem? FindWieldedWand(IReadOnlyList<PluginInventoryItem> items)
    {
        foreach (PluginInventoryItem item in items)
        {
            if (item.ObjectClass == PluginObjectClass.WandStaffOrb && item.IsEquipped)
                return item;
        }
        return null;
    }

    // No mana-conversion modifier to pick a wand by, so prefer any non-Training wand over one that is.
    private static PluginInventoryItem? FindBestWand(IReadOnlyList<PluginInventoryItem> items)
    {
        PluginInventoryItem? best = null;
        foreach (PluginInventoryItem item in items)
        {
            if (item.ObjectClass != PluginObjectClass.WandStaffOrb)
                continue;

            if (best is not { } current)
            {
                best = item;
                continue;
            }

            if (IsTrainingWand(current) && !IsTrainingWand(item))
                best = item;
        }
        return best;
    }

    private static bool IsTrainingWand(PluginInventoryItem item) =>
        item.Name.Contains("Training", StringComparison.Ordinal);
}
