using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Policy;
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

    /// <summary><paramref name="nothingLearnedFor"/> is checked before enqueuing so a doomed
    /// request is refused before the ack.</summary>
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
        Func<ContributionSummary>? contributionSummary = null)
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
            tell.SenderObjectId, tell.Sender, text, isUnresolvedReply: text == DefaultReplies.Unresolved);
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
            Intent.Help => DefaultReplies.Help(_vocabulary),
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
            _ => DefaultReplies.Unresolved,
        };
    }

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
    /// cref="LoopGuard.Admit"/> call every other reply does.</summary>
    private string Position(PluginChatMessage tell)
    {
        QueueStanding standing = _queue.TryGetStanding(tell.SenderObjectId, out int aheadCount);
        return standing switch
        {
            QueueStanding.Active => DefaultReplies.BeingServedNow,
            QueueStanding.Waiting => DefaultReplies.Position(aheadCount),
            _ => DefaultReplies.NotInLine,
        };
    }

    /// <summary>The active recipient's run is asked to stop once the cast already in the air
    /// resolves, via <see cref="_stopActiveRun"/>.</summary>
    private string Cancel(PluginChatMessage tell)
    {
        QueueStanding standing = _queue.TryGetStanding(tell.SenderObjectId, out _);
        if (standing == QueueStanding.Active)
        {
            return _stopActiveRun(tell.SenderObjectId)
                ? DefaultReplies.StoppingAfterThisCast
                : DefaultReplies.AlreadyBeingServed; // race: no longer active by the time this ran
        }

        return _queue.TryCancel(tell.SenderObjectId)
            ? DefaultReplies.RemovedFromLine
            : DefaultReplies.NotInLine;
    }
}
