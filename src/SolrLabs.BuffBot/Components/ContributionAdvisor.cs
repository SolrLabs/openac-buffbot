using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Components;

/// <summary>Peas and the Splitting Tool are never a spell's reagent, so their stock is read
/// directly from inventory by weenie class id.</summary>
internal static class ContributionAdvisor
{
    /// <summary>Scarab colours that have a matching pea, paired by ACE's own recipe data. Platinum,
    /// Diamond and Mana scarabs have no pea and are listed separately below.</summary>
    private static readonly (uint ScarabId, string ScarabName, string PeaName)[] ScarabsWithPeas =
    [
        (691u, "Lead Scarabs", "Lead Peas"),
        (689u, "Iron Scarabs", "Iron Peas"),
        (686u, "Copper Scarabs", "Copper Peas"),
        (688u, "Silver Scarabs", "Silver Peas"),
        (687u, "Gold Scarabs", "Gold Peas"),
        (690u, "Pyreal Scarabs", "Pyreal Peas"),
    ];

    private static readonly (uint ScarabId, string ScarabName)[] ScarabsWithoutPeas =
    [
        (8897u, "Platinum Scarabs"),
        (7299u, "Diamond Scarabs"),
        (37155u, "Mana Scarabs"),
    ];

    private const uint PrismaticTaperId = 20631u;
    private const string PrismaticTaperName = "Prismatic Tapers";
    private const string PrismaticPeaName = "Prismatic Peas";

    private const uint SplittingToolId = 8283u;

    internal const uint SplittingToolWeenieClassId = SplittingToolId;

    internal const uint PrismaticTaperWeenieId = PrismaticTaperId;

    /// <summary>Every scarab weenie class id known, flattened out of <see cref="ScarabsWithPeas"/>
    /// and <see cref="ScarabsWithoutPeas"/>, exposed for <see cref="CastingReagents"/>.</summary>
    internal static readonly IReadOnlyCollection<uint> ScarabWeenieIds = BuildScarabWeenieIds();

    /// <summary>Display text only, plural — <see cref="CastingReagents"/> uses the singular
    /// weenie name for identity matching.</summary>
    internal static readonly IReadOnlyList<string> KnownPeaNames = BuildKnownPeaNames();

    private static IReadOnlyCollection<uint> BuildScarabWeenieIds()
    {
        var ids = new HashSet<uint>();
        foreach ((uint scarabId, _, _) in ScarabsWithPeas)
            ids.Add(scarabId);
        foreach ((uint scarabId, _) in ScarabsWithoutPeas)
            ids.Add(scarabId);
        return ids;
    }

    private static IReadOnlyList<string> BuildKnownPeaNames()
    {
        var names = new List<string>(ScarabsWithPeas.Length + 1);
        foreach ((_, _, string peaName) in ScarabsWithPeas)
            names.Add(peaName);
        names.Add(PrismaticPeaName);
        return names;
    }

    /// <summary><paramref name="reagents"/> must be the all-tiers report, so a scarab <see
    /// cref="Casting.CastStateMachine"/> would step down to still counts as needed.</summary>
    internal static ContributionSummary Build(
        ComponentReport reagents,
        IReadOnlyList<PluginInventoryItem> ownedItems,
        bool inventoryReadable,
        int lowStockThreshold)
    {
        ArgumentNullException.ThrowIfNull(ownedItems);

        if (!inventoryReadable)
            return ContributionSummary.Unavailable;

        bool reagentCatalogAvailable = reagents.Available && reagents.CatalogAvailable;
        var low = new List<ContributionNeed>();
        if (reagentCatalogAvailable)
        {
            var stockByWeenie = new Dictionary<uint, int>();
            foreach (ComponentUsage row in reagents.Items)
            {
                // UsedBy 0 is the console's "held, not needed" row; excluded so this reply keeps
                // naming only what a learned spell actually needs.
                if (row.UsedBy > 0)
                    stockByWeenie[row.WeenieClassId] = row.Stock;
            }

            foreach ((uint scarabId, string scarabName, string peaName) in ScarabsWithPeas)
                AddIfLow(low, stockByWeenie, scarabId, scarabName, peaName, lowStockThreshold);
            foreach ((uint scarabId, string scarabName) in ScarabsWithoutPeas)
                AddIfLow(low, stockByWeenie, scarabId, scarabName, peaAlternative: null, lowStockThreshold);
            AddIfLow(low, stockByWeenie, PrismaticTaperId, PrismaticTaperName, PrismaticPeaName, lowStockThreshold);

            low.Sort(static (a, b) =>
                a.Stock != b.Stock ? a.Stock.CompareTo(b.Stock) : string.CompareOrdinal(a.Name, b.Name));
        }

        int splittingTools = 0;
        foreach (PluginInventoryItem item in ownedItems)
            if (item.WeenieClassId == SplittingToolId)
                splittingTools += item.StackSize;

        return new ContributionSummary(true, reagentCatalogAvailable, low, splittingTools <= 0);
    }

    private static void AddIfLow(
        List<ContributionNeed> low,
        IReadOnlyDictionary<uint, int> stockByWeenie,
        uint weenieClassId,
        string name,
        string? peaAlternative,
        int lowStockThreshold)
    {
        // Not in stockByWeenie means no learned spell needs this colour: never named, even at zero.
        if (!stockByWeenie.TryGetValue(weenieClassId, out int stock) || stock >= lowStockThreshold)
            return;

        low.Add(new ContributionNeed(name, stock, peaAlternative));
    }
}
