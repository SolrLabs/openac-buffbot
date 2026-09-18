using System.Reflection;

namespace SolrLabs.BuffBot.Web;

/// <summary>Loads the page <c>GET /</c> serves — an embedded resource, so it ships inside the plugin assembly rather than as a loose file the install step could drop.</summary>
internal static class ConsolePage
{
    private const string ResourceName = "SolrLabs.BuffBot.Web.console.html";

    internal static byte[] Load()
    {
        using Stream? resource = typeof(ConsolePage).Assembly.GetManifestResourceStream(ResourceName);
        if (resource is null)
            throw new InvalidOperationException($"embedded resource '{ResourceName}' not found.");

        using var buffer = new MemoryStream();
        resource.CopyTo(buffer);
        return buffer.ToArray();
    }
}
