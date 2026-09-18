using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Guard;

/// <summary>Owner-only commands, reachable only by typing at this client's own chat box.
/// </summary>
internal sealed class OperatorConsole
{
    private readonly RequestQueue _queue;
    private readonly LoopGuard _guard;

    internal OperatorConsole(RequestQueue queue, LoopGuard guard)
    {
        _queue = queue;
        _guard = guard;
    }

    /// <summary>What the bot thinks its own state is: enabled, waiting count, muted senders, tells
    /// answered — enough to tell a wedged bot from a busy one.</summary>
    internal string Inspect(bool enabled, int tellsAnswered) =>
        DefaultReplies.Inspect(enabled, _queue.WaitingCount, _guard.MutedSenderNames(), tellsAnswered);

    /// <summary>Drops every waiting request, never the one being cast on, and lifts every mute.
    /// </summary>
    internal string Unwedge()
    {
        int cleared = 0;
        while (_queue.TryDequeue(out BuffRequest request))
        {
            _queue.Complete(request.RequesterObjectId);
            cleared++;
        }

        MuteClearResult unmuted = _guard.ClearMutes();
        return DefaultReplies.Unwedged(cleared, unmuted);
    }
}
