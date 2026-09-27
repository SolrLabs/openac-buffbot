namespace SolrLabs.BuffBot.Chat;

/// <summary>Whether a chat message's sender id falls in the player range the client itself uses for chat.</summary>
internal static class PlayerSender
{
    private const uint FirstPlayerObjectId = 0x50000001u;
    private const uint LastPlayerObjectId = 0x6FFFFFFFu;

    /// <summary>An NPC's line and a self-sent tell's zero id both fall outside this range.</summary>
    internal static bool IsPlayer(uint objectId) =>
        objectId is >= FirstPlayerObjectId and <= LastPlayerObjectId;
}
