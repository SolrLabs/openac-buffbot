namespace SolrLabs.BuffBot.Chat;

/// <summary>Chat message kinds as the host encodes them on <c>PluginChatMessage.Kind</c>; the host exports no enum for these.</summary>
internal enum ChatKind
{
    Tell = 3,

    System = 4, // server chat, including the cast-confirmation message
}

/// <summary>Values on <c>PluginChatMessage.LogTextType</c>, needed beyond <see cref="ChatKind"/>.</summary>
internal static class LogTextTypes
{
    // Mirrors the host's RetailLogTextType.Magic; still ChatKind.System, unlike unrelated chat.
    internal const int Magic = 0x07;
}
