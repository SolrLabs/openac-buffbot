namespace SolrLabs.BuffBot.Web;

/// <summary>The one per-user directory the plugin's own files live under, shared by every node
/// and both hosts for the same user.</summary>
internal static class BuffBotAppData
{
    internal static string BaseDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "solrlabs.buffbot");
}
