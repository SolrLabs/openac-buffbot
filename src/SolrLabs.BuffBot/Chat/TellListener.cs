using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Chat;

/// <summary>Filters a captured batch of chat messages down to tells from someone other than the bot's own character, tracking the sequence cursor for the next capture.</summary>
internal sealed class TellListener
{
    private ulong _lastSequence;

    internal ulong LastSequence => _lastSequence;

    /// <summary><paramref name="ownObjectId"/> is the bot's own character; a tell from that id is never replied to.</summary>
    internal IReadOnlyList<PluginChatMessage> ExtractTells(
        IReadOnlyList<PluginChatMessage> messages, uint ownObjectId)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (messages.Count == 0)
            return Array.Empty<PluginChatMessage>();

        List<PluginChatMessage>? tells = null;
        foreach (PluginChatMessage message in messages)
        {
            if (message.Sequence > _lastSequence)
                _lastSequence = message.Sequence;

            if (message.Kind != (int)ChatKind.Tell)
                continue;
            if (message.SenderObjectId == ownObjectId)
                continue;

            (tells ??= []).Add(message);
        }

        return tells ?? (IReadOnlyList<PluginChatMessage>)Array.Empty<PluginChatMessage>();
    }
}
