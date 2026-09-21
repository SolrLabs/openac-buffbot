using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Web;

/// <summary>Decides whether the mesh console link is worth posting to the headless console.
/// Host-free.</summary>
internal sealed class ConsoleLinkAnnouncer
{
    private string? _lastAnnounced;

    /// <summary>The line to post, or null: nothing new, or <paramref name="hasUi"/> — a GUI
    /// operator already has the panel's Open web console button.</summary>
    internal string? Observe(string? link, bool hasUi)
    {
        if (hasUi || string.IsNullOrEmpty(link) || link == _lastAnnounced)
            return null;

        _lastAnnounced = link;
        return DefaultReplies.WebConsoleAnnouncement(link);
    }
}
