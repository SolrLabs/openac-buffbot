namespace SolrLabs.BuffBot.Web;

/// <summary>How a bot names itself on the mesh: <c>world/objectId</c>, with <c>local</c> standing in for whatever the host cannot tell us — a headless host started before its character has finished logging in can carry an empty <c>WorldName</c> for a tick or two.</summary>
internal static class MeshIdentity
{
    internal static string World(string worldName) => string.IsNullOrEmpty(worldName) ? "local" : worldName;

    internal static string BotId(string worldName, uint objectId) => $"{World(worldName)}/{objectId}";
}
