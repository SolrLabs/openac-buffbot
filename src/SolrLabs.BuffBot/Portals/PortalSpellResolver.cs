using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Portals;

/// <summary>Which summon-portal spells this character knows for a tie, highest level first.</summary>
internal static class PortalSpellResolver
{
    private static readonly uint[] Primary = [1637u, 158u, 157u];
    private static readonly uint[] Secondary = [2650u, 2649u, 2648u];

    internal static IReadOnlyList<PluginSpellInfo> Resolve(ISpellCatalog catalog, PortalTieSlot slot)
    {
        var known = new List<PluginSpellInfo>();
        foreach (uint id in slot == PortalTieSlot.Primary ? Primary : Secondary)
            if (catalog.IsKnown(id) && catalog.TryGet(id, out PluginSpellInfo info))
                known.Add(info);
        return known;
    }
}
