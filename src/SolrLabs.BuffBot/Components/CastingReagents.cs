using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Components;

/// <summary>Peas have no <see cref="PluginObjectClass"/> or <c>ItemType</c> flag, so they match
/// by weenie name instead.</summary>
internal static class CastingReagents
{
    // Item names are always singular, even for a stack.
    private static readonly string[] PeaNames =
    [
        "Lead Pea", "Iron Pea", "Copper Pea", "Silver Pea", "Gold Pea", "Pyreal Pea", "Prismatic Pea",
    ];

    internal static bool IsReagent(PluginInventoryItem item) => IsReagent(item.WeenieClassId, item.Name);

    /// <summary>Overload for callers without a full <see cref="PluginInventoryItem"/> yet.</summary>
    internal static bool IsReagent(uint weenieClassId, string name) =>
        IsScarab(weenieClassId) || IsTaper(weenieClassId) || IsPea(name);

    internal static bool IsScarab(uint weenieClassId) => ContributionAdvisor.ScarabWeenieIds.Contains(weenieClassId);

    internal static bool IsTaper(uint weenieClassId) => weenieClassId == ContributionAdvisor.PrismaticTaperWeenieId;

    internal static bool IsPea(string name)
    {
        foreach (string pea in PeaNames)
            if (string.Equals(pea, name, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
}
