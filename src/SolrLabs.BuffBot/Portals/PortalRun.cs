using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Portals;

/// <summary>Turns to face the summon, and reports whether it can currently turn at all.</summary>
internal interface IPortalFacing
{
    /// <summary>Null when the current heading cannot be read — the headless fallback.</summary>
    float? CurrentHeading { get; }

    bool Face(float degrees);
}

/// <summary>The default when nothing wires a real facing surface in: no heading, so a run casts
/// without ever turning.</summary>
internal sealed class NullPortalFacing : IPortalFacing
{
    internal static readonly NullPortalFacing Instance = new();

    private NullPortalFacing()
    {
    }

    public float? CurrentHeading => null;

    public bool Face(float degrees) => false;
}

internal enum PortalRunOutcome
{
    Success,
    Failed,
}

/// <summary>One summon, cutting into whatever the coordinator's shared <see cref="CastStateMachine"/>
/// was doing. Every exit path turns back to the recorded heading before it reports done.</summary>
internal sealed class PortalRun
{
    // From the summon being sent, not from the run's own creation: stance, mana top-up and
    // fizzle retries ahead of it keep their own timeouts and never draw down this one.
    private const double BudgetSeconds = 20d;

    private const double TurnBoundSeconds = 3d;

    private const float HeadingToleranceDegrees = 5f;

    // How long a clean, unconfirmed completion is trusted before declaring success — ACE sends no
    // landed-cast line for a portal, but a refusal always arrives as chat before it.
    private const double RefusalGraceSeconds = 1.5d;

    private const string SkillRefusalDetail = nameof(PluginCastGate.NotKnown);

    private enum Phase
    {
        Recording,
        TurningToFace,
        Casting,
        TurningBack,
        Done,
    }

    private readonly CastStateMachine _caster;
    private readonly IPortalFacing _facing;
    private readonly Action<string> _sayLocal;
    private readonly Action<uint, string, string> _sendReply;
    private readonly Action<string>? _trace;
    private readonly Action<string>? _warn;
    private readonly IReadOnlyList<PluginSpellInfo> _spells;
    private readonly PortalTie _tie;
    private readonly IReadOnlyList<ResolvedSpell> _manaUpkeepPlan;

    private Phase _phase = Phase.Recording;
    private float? _recordedHeading;
    private float _targetHeading;
    private double _turnElapsedSeconds;
    private int _spellIndex;
    private bool _announced;
    private bool _castSent;
    private double _sentElapsedSeconds;
    private double _graceElapsedSeconds;
    private PortalRunOutcome _pendingOutcome;
    private string? _pendingReply;

    internal PortalRequest Request { get; }

    internal PortalRun(
        PortalRequest request,
        PortalTie tie,
        IReadOnlyList<PluginSpellInfo> spells,
        CastStateMachine caster,
        IPortalFacing facing,
        Action<string> sayLocal,
        Action<uint, string, string> sendReply,
        Action<string>? trace = null,
        Action<string>? warn = null,
        IReadOnlyList<ResolvedSpell>? manaUpkeepPlan = null)
    {
        Request = request;
        _tie = tie;
        _spells = spells;
        _caster = caster;
        _facing = facing;
        _sayLocal = sayLocal;
        _sendReply = sendReply;
        _trace = trace;
        _warn = warn;
        _manaUpkeepPlan = manaUpkeepPlan ?? Array.Empty<ResolvedSpell>();
    }

    /// <summary>Null while the summon is still in progress.</summary>
    internal PortalRunOutcome? Advance(double deltaSeconds, IReadOnlyList<PluginChatMessage> chatMessages)
    {
        if (_phase == Phase.Recording)
            BeginTurningToFace();

        if (_phase == Phase.TurningToFace)
        {
            if (!AdvanceTurn(deltaSeconds, _targetHeading))
                return null;
            _phase = Phase.Casting;
        }

        if (_phase == Phase.Casting && !AdvanceCasting(deltaSeconds, chatMessages))
            return null;

        if (_phase == Phase.TurningBack)
        {
            if (_recordedHeading is { } recorded && !AdvanceTurn(deltaSeconds, recorded))
                return null;
            _phase = Phase.Done;
        }

        if (_phase != Phase.Done)
            return null;

        if (_pendingReply is { } reply)
        {
            _pendingReply = null; // sent exactly once, even if Advance is somehow called again
            _sendReply(Request.RequesterObjectId, Request.RequesterName, reply);
        }
        return _pendingOutcome;
    }

    private void BeginTurningToFace()
    {
        _recordedHeading = _facing.CurrentHeading;
        if (_recordedHeading is { } recorded)
        {
            _targetHeading = PortalHeading.For(recorded, _tie.Direction);
            _facing.Face(_targetHeading);
            _phase = Phase.TurningToFace;
        }
        else
        {
            _phase = Phase.Casting; // headless fallback: nothing to turn by
        }
    }

    // Shared by facing the target and turning back afterward; true once within tolerance of
    // target, the heading turns unreadable, or the bound elapses. Resets the shared timer either way.
    private bool AdvanceTurn(double deltaSeconds, float target)
    {
        _turnElapsedSeconds += deltaSeconds;
        float? current = _facing.CurrentHeading;
        bool reached = current is not { } heading || PortalHeading.IsWithin(heading, target, HeadingToleranceDegrees);
        if (!reached && _turnElapsedSeconds < TurnBoundSeconds)
            return false;

        _turnElapsedSeconds = 0;
        return true;
    }

    // True once the cast has concluded and BeginTurnBack has recorded the outcome; false to keep waiting.
    private bool AdvanceCasting(double deltaSeconds, IReadOnlyList<PluginChatMessage> chatMessages)
    {
        if (!_announced)
        {
            _announced = true;
            string announcement = DefaultReplies.SummoningPortal(_tie.Description);
            _sendReply(Request.RequesterObjectId, Request.RequesterName, announcement);
            _sayLocal(announcement);
            BeginCast();
        }

        if (_castSent && CheckSentOutcome(deltaSeconds, chatMessages))
            return true;

        while (true)
        {
            CastRunResult? result = _caster.Advance(deltaSeconds, chatMessages);
            if (result is null)
            {
                if (!_castSent && _caster.IsWaitingForConfirmation
                    && _caster.CurrentSpellId == _spells[_spellIndex].SpellId)
                    _castSent = true;
                return false;
            }

            if (!Evaluate(result, out bool retry))
            {
                if (!retry)
                    return false;
                continue;
            }
            return true;
        }
    }

    // The refusal chat line arrives before the completion, so it always wins when both show up
    // the same tick. A clean completion with none seen after the grace period reads as success.
    private bool CheckSentOutcome(double deltaSeconds, IReadOnlyList<PluginChatMessage> chatMessages)
    {
        if (FindRefusal(chatMessages) is { } reply)
        {
            _caster.Cancel();
            BeginTurnBack(PortalRunOutcome.Failed, reply);
            return true;
        }

        _graceElapsedSeconds = _caster.CurrentStepCompletionObserved ? _graceElapsedSeconds + deltaSeconds : 0;
        if (_graceElapsedSeconds >= RefusalGraceSeconds)
        {
            _trace?.Invoke("summon completed, no refusal seen");
            _caster.Cancel();
            BeginTurnBack(PortalRunOutcome.Success, null);
            return true;
        }

        _sentElapsedSeconds += deltaSeconds;
        if (_sentElapsedSeconds >= BudgetSeconds)
        {
            _caster.Cancel();
            BeginTurnBack(PortalRunOutcome.Failed, DefaultReplies.PortalTimedOut);
            return true;
        }

        return false;
    }

    // targetObjectId/targetName are unused for a self-targeted spell, matching the idle self-buff idiom.
    private void BeginCast() =>
        _caster.Begin(
            [new ResolvedSpell(_spells[_spellIndex].Name, _spells[_spellIndex])], 0u, string.Empty,
            _manaUpkeepPlan);

    // retry means a skill refusal stepped down a tier and the caller should retry this same tick.
    // The return value is true only once BeginTurnBack has already run.
    private bool Evaluate(CastRunResult result, out bool retry)
    {
        retry = false;

        // A fatal failure (mana, components, too many failures...) can land after a mana-upkeep
        // cast has already succeeded; that landed step is not the portal's own, so it is checked first.
        if (result.Outcome == CastRunOutcome.Failed && result.Failure is { } runFailure)
            return HandleFailure(runFailure, out retry);

        CastStep? ownStep = FindOwnStep(result.Steps);
        if (ownStep is { Outcome: CastOutcome.Cast })
        {
            BeginTurnBack(PortalRunOutcome.Success, null);
            return true;
        }

        CastFailure? failure = ownStep is { Outcome: CastOutcome.Failed } step ? step.Failure : null;
        if (failure is not { } f)
        {
            _warn?.Invoke("portal summon stopped with no failure recorded");
            BeginTurnBack(PortalRunOutcome.Failed, DefaultReplies.CouldNotSummon);
            return true;
        }
        return HandleFailure(f, out retry);
    }

    // The last step whose spell is one this run itself tried, never a spliced-in mana-upkeep cast.
    private CastStep? FindOwnStep(IReadOnlyList<CastStep> steps)
    {
        for (int i = steps.Count - 1; i >= 0; i--)
        {
            foreach (PluginSpellInfo candidate in _spells)
            {
                if (steps[i].SpellId == candidate.SpellId)
                    return steps[i];
            }
        }
        return null;
    }

    private bool HandleFailure(CastFailure f, out bool retry)
    {
        retry = false;

        // No landing text either way; CheckSentOutcome's grace and budget above already decide
        // once a completion was actually seen, so reaching this with nothing seen means keep waiting.
        if (f.Kind == CastFailureKind.DidNotLand)
            return false;

        if (f.Kind == CastFailureKind.GateRefused && f.Detail == SkillRefusalDetail
            && _spellIndex + 1 < _spells.Count)
        {
            _spellIndex++;
            BeginCast();
            retry = true;
            return false;
        }

        _warn?.Invoke($"portal summon failed: {f.Detail}");
        BeginTurnBack(PortalRunOutcome.Failed, DefaultReplies.CouldNotSummon);
        return true;
    }

    private void BeginTurnBack(PortalRunOutcome outcome, string? reply)
    {
        _pendingOutcome = outcome;
        _pendingReply = reply;
        _turnElapsedSeconds = 0;
        if (_recordedHeading is { } recorded)
        {
            _facing.Face(recorded);
            _phase = Phase.TurningBack;
        }
        else
        {
            _phase = Phase.Done;
        }
    }

    // Only these three exact server lines count as a refusal; every other Magic-class line (a
    // component-shortage notice ahead of the real one included) is ignored rather than stopping the scan.
    private string? FindRefusal(IReadOnlyList<PluginChatMessage> messages)
    {
        foreach (PluginChatMessage message in messages)
        {
            if (message.Kind != (int)ChatKind.System || message.LogTextType != LogTextTypes.Magic)
                continue;

            if (message.Text == WeenieErrorReplies.PortalNotTiedServerText)
            {
                _warn?.Invoke($"portal summon refused: {message.Text}");
                return DefaultReplies.NotTied(Request.Slot);
            }

            if (message.Text == WeenieErrorReplies.PortalCannotSummonServerText
                || message.Text == WeenieErrorReplies.PortalSummonFailedServerText)
            {
                _warn?.Invoke($"portal summon refused: {message.Text}");
                return DefaultReplies.CouldNotSummon;
            }

            _trace?.Invoke($"portal wait: ignoring unrelated line: {message.Text}");
        }
        return null;
    }
}
