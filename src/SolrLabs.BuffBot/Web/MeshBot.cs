namespace SolrLabs.BuffBot.Web;

/// <summary>One row of <c>GET /api/bots</c>: everything the page's roster needs about one bot on the mesh, hub or spoke.</summary>
internal sealed record MeshBot(
    string BotId, string Name, string World, bool IsHub, bool Stale, double LastSeenSeconds, MeshStatus Status);

internal sealed record MeshBotsSnapshot(string HubBotId, IReadOnlyList<MeshBot> Bots);
