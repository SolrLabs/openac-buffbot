using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Policy;
using SolrLabs.BuffBot.Portals;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot;

/// <summary>Never calls <see cref="IPluginChat.Submit"/> itself. A recognised <see
/// cref="Intent.Buffs"/> acks immediately; the closing reply comes later from <see cref="Casting.BuffCoordinator"/>.</summary>
internal sealed class Responder
{
    private readonly VocabularyTable _vocabulary;
    private readonly AccessPolicy _policy;
    private readonly RequestQueue _queue;
    private readonly string _version;
    private readonly LoopGuard _guard;
    private readonly Func<bool> _selfCastsDue;
    private readonly Func<bool> _manaTopUpDue;
    private readonly BotStats? _stats;
    private readonly Func<bool> _intakePaused;
    private readonly Func<string, bool> _nothingLearnedFor;
    private readonly Func<uint, bool> _stopActiveRun;
    private readonly Func<ContributionSummary> _contributionSummary;
    private readonly Func<PortalTieSlot, PortalTie> _portalTieFor;
    private readonly Func<PortalTieSlot, bool> _knowsPortalSpell;
    private readonly Func<PortalRequest, EnqueueResult> _enqueuePortal;
    private readonly Func<uint, bool> _hasPendingPortal;
    private readonly Func<uint, int?> _portalPosition;
    private readonly Func<uint, bool> _cancelPortal;

    /// <summary><paramref name="nothingLearnedFor"/> is checked before enqueuing so a doomed
    /// request is refused before the ack; the portal delegates play the same role.</summary>
    internal Responder(
        VocabularyTable vocabulary,
        AccessPolicy policy,
        RequestQueue queue,
        string version,
        LoopGuard guard,
        Func<bool>? selfCastsDue = null,
        Func<bool>? manaTopUpDue = null,
        BotStats? stats = null,
        Func<bool>? intakePaused = null,
        Func<string, bool>? nothingLearnedFor = null,
        Func<uint, bool>? stopActiveRun = null,
        Func<ContributionSummary>? contributionSummary = null,
        Func<PortalTieSlot, PortalTie>? portalTieFor = null,
        Func<PortalTieSlot, bool>? knowsPortalSpell = null,
        Func<PortalRequest, EnqueueResult>? enqueuePortal = null,
        Func<uint, bool>? hasPendingPortal = null,
        Func<uint, int?>? portalPosition = null,
        Func<uint, bool>? cancelPortal = null)
    {
        _vocabulary = vocabulary;
        _policy = policy;
        _queue = queue;
        _version = version;
        _guard = guard;
        _selfCastsDue = selfCastsDue ?? (static () => false);
        _manaTopUpDue = manaTopUpDue ?? (static () => false);
        _stats = stats;
        _intakePaused = intakePaused ?? (static () => false);
        _nothingLearnedFor = nothingLearnedFor ?? (static _ => false);
        _stopActiveRun = stopActiveRun ?? (static _ => false);
        _contributionSummary = contributionSummary ?? (static () => ContributionSummary.Unavailable);
        _portalTieFor = portalTieFor ?? (static _ => PortalTie.Empty);
        _knowsPortalSpell = knowsPortalSpell ?? (static _ => false);
        _enqueuePortal = enqueuePortal ?? (static _ => EnqueueResult.QueueFull);
        _hasPendingPortal = hasPendingPortal ?? (static _ => false);
        _portalPosition = portalPosition ?? (static _ => null);
        _cancelPortal = cancelPortal ?? (static _ => false);
    }

    /// <summary><paramref name="tell"/>'s sender is passed through exactly as received, never
    /// parsed or stripped, so an admin-prefixed name still addresses correctly.</summary>
    internal string? Reply(PluginChatMessage tell, int tellsAnswered)
    {
        if (_guard.IsBotShapedAndShouldDrop(tell.SenderObjectId, tell.Sender, tell.Text))
            return null;

        AccessDecision decision = _policy.Evaluate(new AccessRequest(tell.Sender, tell.Text));
        string text = decision.IsAllowed
            ? ResolveIntent(tell, tellsAnswered)
            : decision.Reason ?? DefaultReplies.Unresolved;

        string? admitted = _guard.Admit(
            tell.SenderObjectId, tell.Sender, text, isUnresolvedReply: text == DefaultReplies.Unresolved,
            requestText: tell.Text);
        if (admitted is null)
            return null;

        return $"/tell {tell.Sender}, {admitted}";
    }

    private string ResolveIntent(PluginChatMessage tell, int tellsAnswered)
    {
        if (!_vocabulary.TryResolve(tell.Text, out Intent intent))
            return DefaultReplies.Unresolved;

        return intent switch
        {
            Intent.Help => DefaultReplies.Help(_vocabulary, PortalsOffered()),
            Intent.Status => DefaultReplies.Status(_version, tellsAnswered),
            Intent.Buffs => Enqueue(tell, DefaultSpellSets.Buff),
            Intent.Prots => Enqueue(tell, DefaultSpellSets.Prots),
            Intent.Heavy => Enqueue(tell, DefaultSpellSets.Heavy),
            Intent.Light => Enqueue(tell, DefaultSpellSets.Light),
            Intent.Finesse => Enqueue(tell, DefaultSpellSets.Finesse),
            Intent.Missile => Enqueue(tell, DefaultSpellSets.Missile),
            Intent.Void => Enqueue(tell, DefaultSpellSets.Void),
            Intent.Mage => Enqueue(tell, DefaultSpellSets.Mage),
            Intent.TwoHanded => Enqueue(tell, DefaultSpellSets.TwoHanded),
            Intent.Dual => Enqueue(tell, DefaultSpellSets.Dual),
            Intent.Tink => Enqueue(tell, DefaultSpellSets.Tink),
            Intent.Trades => Enqueue(tell, DefaultSpellSets.Trades),
            Intent.Position => Position(tell),
            Intent.Cancel => Cancel(tell),
            Intent.Contribute => DefaultReplies.Contribute(_contributionSummary()),
            Intent.Where => Where(),
            Intent.PortalPrimary => EnqueuePortal(tell, PortalTieSlot.Primary),
            Intent.PortalSecondary => EnqueuePortal(tell, PortalTieSlot.Secondary),
            _ => DefaultReplies.Unresolved,
        };
    }

    private bool PortalsOffered() =>
        _portalTieFor(PortalTieSlot.Primary).IsOffered || _portalTieFor(PortalTieSlot.Secondary).IsOffered;

    private string Where() =>
        DefaultReplies.Where(
            _portalTieFor(PortalTieSlot.Primary).Description, _portalTieFor(PortalTieSlot.Secondary).Description);

    /// <summary>Mirrors <see cref="Enqueue"/>'s "refuse before acking" shape; a second ask while
    /// already pending still reaches <see cref="_enqueuePortal"/>, which answers that itself.</summary>
    private string EnqueuePortal(PluginChatMessage tell, PortalTieSlot slot)
    {
        if (_intakePaused())
            return DefaultReplies.IntakePaused;

        if (!_portalTieFor(slot).IsOffered)
            return DefaultReplies.PortalNotOffered;

        if (!_knowsPortalSpell(slot))
            return DefaultReplies.CantSummonYet;

        EnqueueResult result = _enqueuePortal(new PortalRequest(tell.SenderObjectId, tell.Sender, slot));
        return result switch
        {
            EnqueueResult.Enqueued => PortalStartingReply(tell.SenderObjectId),
            EnqueueResult.AlreadyQueued => DefaultReplies.AlreadyQueued,
            EnqueueResult.QueueFull => DefaultReplies.QueueFull,
            _ => DefaultReplies.Unresolved,
        };
    }

    /// <summary>The portal lane always cuts in ahead of any buff chain, so 0 ahead reads as
    /// starting now rather than "you're next" the way a buff queue's own position does.</summary>
    private string PortalStartingReply(uint requesterObjectId) =>
        _portalPosition(requesterObjectId) is { } ahead && ahead > 0
            ? DefaultReplies.Queued(ahead)
            : DefaultReplies.Starting;

    private string Enqueue(PluginChatMessage tell, string setName)
    {
        if (_intakePaused())
            return DefaultReplies.IntakePaused;

        QueueStanding standing = _queue.TryGetStanding(tell.SenderObjectId, out _);

        // A second request while already being served does not touch the run in flight; it is
        // pointed at cancel instead, same as a second cancel would be.
        if (standing == QueueStanding.Active)
            return DefaultReplies.AlreadyBeingServed;

        // A second request from someone already waiting replaces their queued request rather
        // than being refused, unless it names the same profile again, which stays AlreadyQueued.
        if (standing == QueueStanding.Waiting)
        {
            if (_queue.TryGetWaiting(tell.SenderObjectId, out BuffRequest existing)
                && string.Equals(existing.SetName, setName, StringComparison.Ordinal))
                return DefaultReplies.AlreadyQueued;

            string previousSetName = _queue.TryReplaceWaiting(
                new BuffRequest(tell.SenderObjectId, tell.Sender, setName)) ?? setName;
            return DefaultReplies.ReplacedQueuedRequest(setName, previousSetName);
        }

        // Known up front, before ever enqueuing, so the "On it." ack never precedes this refusal.
        if (_nothingLearnedFor(setName))
            return DefaultReplies.NothingLearnedInProfile(setName);

        var request = new BuffRequest(tell.SenderObjectId, tell.Sender, setName);
        EnqueueResult result = _queue.TryEnqueue(request, out int aheadCount);

        if (result == EnqueueResult.Enqueued)
        {
            // Logs the archetype keyword the request resolved to, never the raw tell text.
            _stats?.RecordRequestAccepted(tell.Sender, setName);
        }

        return result switch
        {
            EnqueueResult.Enqueued when aheadCount == 0 => StartingReply(),
            EnqueueResult.Enqueued => DefaultReplies.Queued(aheadCount),
            EnqueueResult.AlreadyQueued => DefaultReplies.AlreadyQueued,
            EnqueueResult.QueueFull => DefaultReplies.QueueFull,
            _ => DefaultReplies.Unresolved,
        };
    }

    private string StartingReply()
    {
        if (_selfCastsDue())
            return DefaultReplies.StartingWithSelfBuffs;
        if (_manaTopUpDue())
            return DefaultReplies.StartingWithManaTopUp;
        return DefaultReplies.Starting;
    }

    /// <summary>Cheap to ask and easy to spam, so this rides through the same <see
    /// cref="LoopGuard.Admit"/> call every other reply does. A buff standing takes precedence.</summary>
    private string Position(PluginChatMessage tell)
    {
        QueueStanding standing = _queue.TryGetStanding(tell.SenderObjectId, out int aheadCount);
        if (standing != QueueStanding.NotQueued)
        {
            return standing == QueueStanding.Active
                ? DefaultReplies.BeingServedNow
                : DefaultReplies.Position(aheadCount);
        }

        if (!_hasPendingPortal(tell.SenderObjectId))
            return DefaultReplies.NotInLine;

        // Null means already dequeued into the running summon, same as a buff queue's own Active.
        return _portalPosition(tell.SenderObjectId) is { } portalAhead
            ? DefaultReplies.Position(portalAhead)
            : DefaultReplies.BeingServedNow;
    }

    /// <summary>The active recipient's run is asked to stop via <see cref="_stopActiveRun"/>; a
    /// pending portal is tried only once the buff queue has nothing for this sender.</summary>
    private string Cancel(PluginChatMessage tell)
    {
        QueueStanding standing = _queue.TryGetStanding(tell.SenderObjectId, out _);
        if (standing == QueueStanding.Active)
        {
            return _stopActiveRun(tell.SenderObjectId)
                ? DefaultReplies.StoppingAfterThisCast
                : DefaultReplies.AlreadyBeingServed; // race: no longer active by the time this ran
        }

        if (_queue.TryCancel(tell.SenderObjectId))
            return DefaultReplies.RemovedFromLine;

        return _cancelPortal(tell.SenderObjectId) ? DefaultReplies.RemovedFromLine : DefaultReplies.NotInLine;
    }
}
