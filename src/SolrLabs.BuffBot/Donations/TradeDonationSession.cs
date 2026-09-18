using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Donations;

/// <summary><see cref="Count"/> is the stack size the item resolver reported at the moment this session verified and accepted it, never a guess from a chat line.</summary>
internal readonly record struct DonatedItem(string Name, uint WeenieClassId, int Count);

/// <summary>One trade's worth of accepted items, raised exactly once, the tick "Trade Complete!" is seen following an <see cref="ITradeView.Accept"/> this session itself issued.</summary>
internal readonly record struct CompletedDonation(
    string DonorName, uint DonorObjectId, IReadOnlyList<DonatedItem> Items, DateTime UtcTime);

/// <summary>The narrow surface <see cref="TradeDonationSession"/> needs from <see cref="AcDream.Plugin.Abstractions.ITradeAutomation"/>, small enough a test can fake it directly. <c>Add</c> and <c>Decline</c> are left out on purpose: this session never stages an item of its own and never withdraws an acceptance without also ending the trade outright.</summary>
internal interface ITradeView
{
    bool IsOpen { get; }
    uint PartnerObjectId { get; }
    IReadOnlyList<uint> MyItems { get; }
    IReadOnlyList<uint> PartnerItems { get; }
    bool MyAccepted { get; }
    bool PartnerAccepted { get; }
    PluginTradeCommandResult Accept();
    PluginTradeCommandResult Reset();
    PluginTradeCommandResult End();
}

/// <summary>Drives one trade window. The exact set of ids in <see cref="ITradeView.PartnerItems"/>
/// is the progress marker: a changed set restarts classification and both timers.</summary>
internal sealed class TradeDonationSession
{
    internal const double StallTimeoutSeconds = 90d;
    internal const double UnresolvedItemWaitSeconds = 2d;
    internal const int MaxSpaceRefusals = 3;

    /// <summary>WeenieError.TradeComplete (0x0529), mapped to plugin chat verbatim.</summary>
    private const string TradeCompleteText = "Trade Complete!";

    /// <summary>ACE's two refusal strings addressed to "You" — the bot is always the side that can run short on space, since it never stages an item of its own.</summary>
    private const string EncumberedText = "You are too encumbered to complete the trade!";
    private const string NoSpaceText = "You do not have enough free slots to complete the trade!";

    private readonly ITradeView _view;
    private readonly Func<uint, (uint wcid, string name, int stack)?> _resolve;
    private readonly Func<bool> _botHoldsSplittingTool;

    private readonly Queue<string> _replies = new();
    private readonly Queue<CompletedDonation> _completed = new();

    private bool _wasOpen;
    private IReadOnlyList<uint> _lastStageIds = Array.Empty<uint>();
    private double _stageAgeSeconds;
    private bool _resetIssuedForStage;
    private double _stallSeconds;
    private int _spaceRefusalCount;
    private readonly Action<string> _trace;
    private IReadOnlyList<DonatedItem>? _lastAcceptedItems;
    private uint _acceptedPartnerId;
    private string _acceptedPartnerName = string.Empty;

    internal TradeDonationSession(
        ITradeView view,
        Func<uint, (uint wcid, string name, int stack)?> resolve,
        Func<bool> botHoldsSplittingTool,
        Action<string>? trace = null)
    {
        _view = view;
        _resolve = resolve;
        _botHoldsSplittingTool = botHoldsSplittingTool;
        _trace = trace ?? (_ => { });
    }

    /// <summary>Every tell queued since the last drain, in order.</summary>
    internal IReadOnlyList<string> DrainReplies()
    {
        if (_replies.Count == 0)
            return Array.Empty<string>();

        var drained = new List<string>(_replies.Count);
        while (_replies.TryDequeue(out string? reply))
            drained.Add(reply);
        return drained;
    }

    /// <summary>Every donation completed since the last drain — one per finished trade.</summary>
    internal IReadOnlyList<CompletedDonation> DrainCompletedDonations()
    {
        if (_completed.Count == 0)
            return Array.Empty<CompletedDonation>();

        var drained = new List<CompletedDonation>(_completed.Count);
        while (_completed.TryDequeue(out CompletedDonation donation))
            drained.Add(donation);
        return drained;
    }

    /// <summary><paramref name="chatMessages"/> is this tick's already-captured batch — never polled a second time.</summary>
    internal void Tick(double deltaSeconds, IReadOnlyList<PluginChatMessage> chatMessages)
    {
        if (!_view.IsOpen)
        {
            if (_wasOpen)
            {
                _trace($"[trade] closed; mine={_view.MyAccepted} theirs={_view.PartnerAccepted} "
                    + $"pending={_lastAcceptedItems?.Count ?? -1}");
                // "Trade Complete!" does not reach plugin chat on every host, so a close on an
                // acceptance nothing withdrew is the trade going through.
                CompleteDonation();
                ResetPerTradeState();
            }
            _wasOpen = false;
            return;
        }

        if (!_wasOpen)
        {
            ResetPerTradeState();
            _wasOpen = true;
            _trace($"[trade] opened with {_view.PartnerObjectId:X8}");
        }

        bool completedThisTick = false;
        foreach (PluginChatMessage message in chatMessages)
        {
            if (string.Equals(message.Text, TradeCompleteText, StringComparison.Ordinal))
                completedThisTick = true;
            else if (string.Equals(message.Text, EncumberedText, StringComparison.Ordinal)
                || string.Equals(message.Text, NoSpaceText, StringComparison.Ordinal))
                _spaceRefusalCount++;
        }

        if (completedThisTick)
        {
            CompleteDonation();
            return;
        }

        if (_view.MyItems.Count > 0)
        {
            _view.End();
            ResetPerTradeState();
            return;
        }

        if (_spaceRefusalCount >= MaxSpaceRefusals)
        {
            _view.End();
            _replies.Enqueue(DefaultReplies.DonationNoSpace);
            ResetPerTradeState();
            return;
        }

        IReadOnlyList<uint> currentIds = _view.PartnerItems;
        bool stageChanged = !SameIds(currentIds, _lastStageIds);
        if (stageChanged)
        {
            // Copied: the host may hand back the same list instance every poll, and a stored
            // reference would mutate with it and hide the next change.
            _lastStageIds = [.. currentIds];
            _stageAgeSeconds = 0;
            _resetIssuedForStage = false;
            _stallSeconds = 0;

            // The server empties the window when the trade goes through, before the client's own
            // MyAccepted catches up, so our own pending acceptance is the mark that survives.
            _trace($"[trade] stage now {currentIds.Count} item(s); mine={_view.MyAccepted} "
                + $"theirs={_view.PartnerAccepted}");
            if (currentIds.Count == 0 && _lastAcceptedItems is { Count: > 0 })
            {
                CompleteDonation();
                return;
            }

            // Staging anything clears both sides' acceptance server-side.
            _lastAcceptedItems = null;
            _acceptedPartnerId = 0u;
            _acceptedPartnerName = string.Empty;
        }
        else if (_view.PartnerAccepted)
            _stallSeconds = 0;
        else
            _stallSeconds += deltaSeconds;

        if (_stallSeconds >= StallTimeoutSeconds)
        {
            _view.End();
            _replies.Enqueue(DefaultReplies.DonationStallClosing);
            ResetPerTradeState();
            return;
        }

        if (currentIds.Count == 0)
            return;

        if (!stageChanged)
            _stageAgeSeconds += deltaSeconds;

        ClassifyAndAdvance(currentIds);
    }

    private void ClassifyAndAdvance(IReadOnlyList<uint> currentIds)
    {
        bool botHoldsSplittingTool = _botHoldsSplittingTool();
        var wanted = new List<DonatedItem>(currentIds.Count);
        var unwantedNames = new List<string>();
        bool anyUnresolved = false;

        foreach (uint id in currentIds)
        {
            (uint wcid, string name, int stack)? resolved = _resolve(id);
            if (resolved is null)
            {
                anyUnresolved = true;
                continue;
            }

            (uint wcid, string name, int stack) item = resolved.Value;
            if (DonationPolicy.IsWanted(item.wcid, item.name, botHoldsSplittingTool))
                wanted.Add(new DonatedItem(item.name, item.wcid, item.stack));
            else
                unwantedNames.Add(item.name);
        }

        // An unwanted item decides this stage outright, without waiting out the unresolved timer.
        if (unwantedNames.Count > 0)
        {
            RejectStage(unwantedNames, botHoldsSplittingTool);
            return;
        }

        if (anyUnresolved)
        {
            if (_stageAgeSeconds < UnresolvedItemWaitSeconds)
                return;

            RejectStage(["an unidentified item"], botHoldsSplittingTool);
            return;
        }

        if (_view.PartnerAccepted && !_view.MyAccepted && _view.MyItems.Count == 0
            && SameIds(_view.PartnerItems, currentIds))
        {
            PluginTradeCommandResult accepted = _view.Accept();
            _trace($"[trade] accept -> {accepted.Status}");
            _lastAcceptedItems = wanted;
            _acceptedPartnerId = _view.PartnerObjectId;
            _acceptedPartnerName = PartnerName();
        }
    }

    private void RejectStage(IReadOnlyList<string> unwantedNames, bool botHoldsSplittingTool)
    {
        if (_resetIssuedForStage)
            return;

        _view.Reset();
        _replies.Enqueue(
            DefaultReplies.DonationUnwanted(unwantedNames, DonationPolicy.WantedItemsDescription(botHoldsSplittingTool)));
        _resetIssuedForStage = true;
    }

    /// <summary>Turns an acceptance that was never withdrawn into a recorded donation. Does
    /// nothing when there is none, so it is safe to call on any close.</summary>
    private void CompleteDonation()
    {
        if (_lastAcceptedItems is not { Count: > 0 } items)
            return;

        _replies.Enqueue(DefaultReplies.DonationThanks);
        _completed.Enqueue(
            new CompletedDonation(_acceptedPartnerName, _acceptedPartnerId, items, DateTime.UtcNow));
        _lastAcceptedItems = null;
        _trace($"[trade] donation recorded: {items.Count} item(s) from {_acceptedPartnerName}");

        // Close the window ourselves rather than leave the partner waiting out the stall timer.
        if (_view.IsOpen)
            _view.End();
    }

    private string PartnerName() => _resolve(_view.PartnerObjectId)?.name ?? string.Empty;

    private void ResetPerTradeState()
    {
        _lastStageIds = Array.Empty<uint>();
        _stageAgeSeconds = 0;
        _resetIssuedForStage = false;
        _stallSeconds = 0;
        _spaceRefusalCount = 0;
        _lastAcceptedItems = null;
        _acceptedPartnerId = 0u;
        _acceptedPartnerName = string.Empty;
    }

    private static bool SameIds(IReadOnlyList<uint> a, IReadOnlyList<uint> b)
    {
        if (a.Count != b.Count)
            return false;

        var set = new HashSet<uint>(a);
        foreach (uint id in b)
            if (!set.Contains(id))
                return false;
        return true;
    }
}
