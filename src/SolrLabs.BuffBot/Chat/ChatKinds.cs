namespace SolrLabs.BuffBot.Chat;

/// <summary>Chat message kinds as the host encodes them on <c>PluginChatMessage.Kind</c>; the host exports no enum for these.</summary>
internal enum ChatKind
{
    Tell = 3,

    /// <summary>Server system chat, including the cast-confirmation message.</summary>
    System = 4,
}
