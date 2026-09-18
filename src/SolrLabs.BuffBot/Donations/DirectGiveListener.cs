using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Donations;

/// <summary>A "&lt;Name&gt; tries to give you &lt;item&gt;." line, resolved to a nearby object the bot can address. Never produced for an unresolvable giver.</summary>
internal readonly record struct DirectGiveRefusal(uint GiverObjectId, string GiverName);

/// <summary>What one tick's worth of captured chat produced.</summary>
internal readonly record struct DirectGiveOutcome(
    IReadOnlyList<DirectGiveRefusal> Refusals, bool WarnOperatorAboutLandedGive);

/// <summary>"X tries to give you Y" (refused) resolves the giver so a tell can address them. A
/// landed "X gives you Y" only gets a one-time operator warning; the item stays where it landed.</summary>
internal sealed class DirectGiveListener
{
    private bool _warnedOperatorThisSession;

    internal DirectGiveOutcome Process(
        IReadOnlyList<PluginChatMessage> chatMessages, IReadOnlyList<PluginWorldObject> capturedObjects)
    {
        List<DirectGiveRefusal>? refusals = null;
        bool warn = false;

        foreach (PluginChatMessage message in chatMessages)
        {
            string? triesGiverName = GiveLineParser.TryParseTriesToGiveGiverName(message);
            if (triesGiverName is { Length: > 0 })
            {
                GiverResolver.GiverResolution resolution =
                    GiverResolver.Resolve(capturedObjects, triesGiverName);
                if (resolution.Result == GiverResolver.GiverLookup.Found)
                {
                    refusals ??= new List<DirectGiveRefusal>();
                    refusals.Add(new DirectGiveRefusal(resolution.ObjectId, resolution.Name));
                }
                continue;
            }

            if (!warn && !_warnedOperatorThisSession && GiveLineParser.TryParse(message) is not null)
                warn = true;
        }

        if (warn)
            _warnedOperatorThisSession = true;

        return new DirectGiveOutcome(
            (IReadOnlyList<DirectGiveRefusal>?)refusals ?? Array.Empty<DirectGiveRefusal>(), warn);
    }
}
