using SolrLabs.BuffBot.Timing;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Guard;

/// <summary>Keyed per sender object id so a name change does not reset the counters.</summary>
internal sealed class LoopGuard
{
    /// <summary>Counted on the request that starts an exchange, not every reply (see
    /// <see cref="Admit"/>'s <c>countsAsRequest</c>).</summary>
    internal const int MaxRequestsPerWindow = 12;
    internal static readonly TimeSpan RateLimitWindow = TimeSpan.FromSeconds(60);

    /// <summary>Repeats of the exact same reply text trip the breaker.</summary>
    internal const int RepeatThreshold = 3;
    internal static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan MuteDuration = TimeSpan.FromMinutes(10);

    internal static readonly TimeSpan UnresolvedReplyWindow = TimeSpan.FromSeconds(30);

    /// <summary>Shared by the bot-shape and rate-limit log lines.</summary>
    internal static readonly TimeSpan LogSuppressWindow = TimeSpan.FromMinutes(1);

    private readonly IClock _clock;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logWarn;
    private readonly Dictionary<uint, SenderState> _senders = new();
    private int _maxRequestsPerWindow = MaxRequestsPerWindow;

    internal LoopGuard(IClock clock, Action<string> logInfo, Action<string> logWarn)
    {
        _clock = clock;
        _logInfo = logInfo;
        _logWarn = logWarn;
    }

    /// <summary>The web console's Replies-per-sender control writes here on every tick, so a
    /// change takes effect on the sender's very next reply, not a fresh window.</summary>
    internal void SetMaxRequestsPerWindow(int value) => _maxRequestsPerWindow = value;

    /// <summary>Called before intent resolution.</summary>
    internal bool IsBotShapedAndShouldDrop(uint senderObjectId, string senderName, string text)
    {
        if (!BotShapeMatcher.IsBotShaped(text))
            return false;

        DateTimeOffset now = _clock.UtcNow;
        SenderState state = GetOrAdd(senderObjectId);
        if (state.LastBotShapeLog is null || now - state.LastBotShapeLog.Value >= LogSuppressWindow)
        {
            state.LastBotShapeLog = now;
            _logInfo($"ignored bot-shaped tell from {senderName}");
        }

        return true;
    }

    /// <summary>Returns the reply text, or <see langword="null"/> for nothing; <c>countsAsRequest</c>
    /// is false for a closing reply, <c>countsTowardMuteTrigger</c> false for a donation reply.</summary>
    internal string? Admit(
        uint senderObjectId,
        string senderName,
        string replyText,
        bool isUnresolvedReply,
        bool countsAsRequest = true,
        bool countsTowardMuteTrigger = true)
    {
        DateTimeOffset now = _clock.UtcNow;
        SenderState state = GetOrAdd(senderObjectId);
        state.LastKnownName = senderName;

        if (IsMuted(state, now))
            return null;

        if (countsAsRequest)
        {
            Trim(state.RequestTimestamps, now, RateLimitWindow);
            if (state.RequestTimestamps.Count >= _maxRequestsPerWindow)
            {
                if (state.LastRateLimitLog is null || now - state.LastRateLimitLog.Value >= LogSuppressWindow)
                {
                    state.LastRateLimitLog = now;
                    _logWarn($"rate limit exceeded for {senderName}");
                }
                return null;
            }
        }

        if (isUnresolvedReply
            && state.LastUnresolvedReply is { } lastUnresolved
            && now - lastUnresolved < UnresolvedReplyWindow)
        {
            return null;
        }

        if (!countsTowardMuteTrigger)
        {
            if (countsAsRequest)
                state.RequestTimestamps.Enqueue(now);
            if (isUnresolvedReply)
                state.LastUnresolvedReply = now;
            return replyText;
        }

        string outgoing = replyText;
        if (state.LastReplyText == replyText)
        {
            Trim(state.RepeatTimestamps, now, RepeatWindow);
        }
        else
        {
            state.RepeatTimestamps.Clear();
            state.LastReplyText = replyText;
        }
        state.RepeatTimestamps.Enqueue(now);

        if (state.RepeatTimestamps.Count >= RepeatThreshold)
        {
            state.AutoMutedUntil = now + MuteDuration;
            state.RepeatTimestamps.Clear();
            state.LastReplyText = null;
            _logWarn(
                $"muting {senderName} for {(int)MuteDuration.TotalMinutes} minutes "
                    + $"after {RepeatThreshold} repeats of the same reply");
            outgoing = DefaultReplies.Pausing;
        }

        if (countsAsRequest)
            state.RequestTimestamps.Enqueue(now);
        if (isUnresolvedReply)
            state.LastUnresolvedReply = now;

        return outgoing;
    }

    /// <summary>Console-only; the panel uses <see cref="MutedEntries"/> instead.</summary>
    internal IReadOnlyList<string> MutedSenderNames()
    {
        DateTimeOffset now = _clock.UtcNow;
        var names = new List<string>();
        foreach (SenderState state in _senders.Values)
        {
            if (IsMuted(state, now))
                names.Add(state.LastKnownName ?? "unknown");
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    /// <summary>ObjectId lets the panel act on a selected row without a name lookup.</summary>
    internal IReadOnlyList<MutedEntry> MutedEntries()
    {
        DateTimeOffset now = _clock.UtcNow;
        var entries = new List<MutedEntry>();
        foreach ((uint objectId, SenderState state) in _senders)
        {
            string name = state.LastKnownName ?? "unknown";
            if (state.IsManuallyMuted)
                entries.Add(new MutedEntry(objectId, name, Until: null, IsManual: true));
            else if (state.AutoMutedUntil is { } until && now < until)
                entries.Add(new MutedEntry(objectId, name, until, IsManual: false));
        }
        entries.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return entries;
    }

    /// <summary>Never lapses on its own, unlike the automatic mute; only <see cref="Unmute"/> or
    /// <see cref="ClearMutes"/> ends it. Takes <paramref name="name"/> directly, so an unseen sender can be muted pre-emptively.</summary>
    internal void Mute(uint senderObjectId, string name)
    {
        SenderState state = GetOrAdd(senderObjectId);
        state.LastKnownName = name;
        state.IsManuallyMuted = true;
    }

    /// <summary>Clears the repeat tally too, so a stale in-window count cannot immediately re-trip
    /// the breaker. No-op if this sender was never muted.</summary>
    internal void Unmute(uint senderObjectId)
    {
        if (!_senders.TryGetValue(senderObjectId, out SenderState? state))
            return;

        state.IsManuallyMuted = false;
        state.AutoMutedUntil = null;
        state.RepeatTimestamps.Clear();
        state.LastReplyText = null;
    }

    /// <summary>Clears repeat tallies too, as in <see cref="Unmute"/>. Returns how many of each
    /// kind were lifted.</summary>
    internal MuteClearResult ClearMutes()
    {
        DateTimeOffset now = _clock.UtcNow;
        int automatic = 0;
        int manual = 0;
        foreach (SenderState state in _senders.Values)
        {
            bool wasManual = state.IsManuallyMuted;
            bool wasAutomatic = !wasManual && state.AutoMutedUntil is { } until && now < until;
            if (!wasManual && !wasAutomatic)
                continue;

            state.IsManuallyMuted = false;
            state.AutoMutedUntil = null;
            state.RepeatTimestamps.Clear();
            state.LastReplyText = null;

            if (wasManual)
                manual++;
            else
                automatic++;
        }
        return new MuteClearResult(automatic, manual);
    }

    private SenderState GetOrAdd(uint senderObjectId)
    {
        if (!_senders.TryGetValue(senderObjectId, out SenderState? state))
        {
            state = new SenderState();
            _senders[senderObjectId] = state;
        }
        return state;
    }

    private static void Trim(Queue<DateTimeOffset> timestamps, DateTimeOffset now, TimeSpan window)
    {
        while (timestamps.Count > 0 && now - timestamps.Peek() >= window)
            timestamps.Dequeue();
    }

    private static bool IsMuted(SenderState state, DateTimeOffset now) =>
        state.IsManuallyMuted || (state.AutoMutedUntil is { } until && now < until);

    private sealed class SenderState
    {
        internal readonly Queue<DateTimeOffset> RequestTimestamps = new();
        internal readonly Queue<DateTimeOffset> RepeatTimestamps = new();
        internal string? LastReplyText;
        internal DateTimeOffset? LastUnresolvedReply;

        internal DateTimeOffset? AutoMutedUntil;
        internal bool IsManuallyMuted;
        internal DateTimeOffset? LastBotShapeLog;
        internal DateTimeOffset? LastRateLimitLog;
        internal string? LastKnownName;
    }
}
