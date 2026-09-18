using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Donations;

/// <summary>Judges a staged trade item wanted or unwanted. Wanted: every casting reagent, a Splitting Tool while the bot holds none, and a Trade Note worth 10,000 or more. Everything else is unwanted.</summary>
internal static class DonationPolicy
{
    /// <summary>A note below this is unwanted exactly like any other unwanted item.</summary>
    private const int WantedTradeNoteMinimumValue = 10_000;

    /// <summary>Every Trade Note weenie class id, paired with the value its name already spells out — checked first because it needs no parsing.</summary>
    private static readonly IReadOnlyDictionary<uint, int> TradeNoteValueByWeenieId = new Dictionary<uint, int>
    {
        [2621u] = 100,
        [2622u] = 500,
        [2623u] = 1_000,
        [2624u] = 5_000,
        [2625u] = 10_000,
        [7374u] = 15_000,
        [7375u] = 20_000,
        [7376u] = 25_000,
        [2626u] = 50_000,
        [7377u] = 75_000,
        [2627u] = 100_000,
        [20628u] = 150_000,
        [20629u] = 200_000,
        [20630u] = 250_000,
    };

    /// <summary>Used where a real <see cref="PluginInventoryItem"/> is in hand, so its class and value can gate the Trade Note check before falling back to parsing the name.</summary>
    internal static bool IsWanted(PluginInventoryItem item, bool botHoldsSplittingTool) =>
        CastingReagents.IsReagent(item)
        || IsWantedSplittingTool(item.WeenieClassId, botHoldsSplittingTool)
        || IsWantedTradeNote(item);

    /// <summary>For a staged trade item known only by weenie class id and name; a Trade Note is recognised by its known weenie id or, failing that, by the value its name spells out.</summary>
    internal static bool IsWanted(uint weenieClassId, string name, bool botHoldsSplittingTool) =>
        CastingReagents.IsReagent(weenieClassId, name)
        || IsWantedSplittingTool(weenieClassId, botHoldsSplittingTool)
        || IsWantedTradeNoteByIdOrName(weenieClassId, name);

    /// <summary>The wanted list used in a reply, with Splitting Tools left out while the bot already holds one.</summary>
    internal static string WantedItemsDescription(bool botHoldsSplittingTool)
    {
        var items = new List<string>(3) { "casting reagents" };
        if (!botHoldsSplittingTool)
            items.Add("Splitting Tools");
        items.Add("trade notes of 10,000+");
        return JoinWithOr(items);
    }

    private static string JoinWithOr(IReadOnlyList<string> items) =>
        items.Count switch
        {
            1 => items[0],
            2 => $"{items[0]} or {items[1]}",
            _ => $"{string.Join(", ", items.Take(items.Count - 1))}, or {items[^1]}",
        };

    private static bool IsWantedSplittingTool(uint weenieClassId, bool botHoldsSplittingTool) =>
        weenieClassId == ContributionAdvisor.SplittingToolWeenieClassId && !botHoldsSplittingTool;

    private static bool IsWantedTradeNote(PluginInventoryItem item)
    {
        if (item.ObjectClass != PluginObjectClass.TradeNote)
            return false;

        if (TradeNoteValueByWeenieId.TryGetValue(item.WeenieClassId, out int knownValue))
            return knownValue >= WantedTradeNoteMinimumValue;

        if (item.Value > 0)
            return item.Value >= WantedTradeNoteMinimumValue;

        return TryParseTradeNoteValue(item.Name, out int parsedValue) && parsedValue >= WantedTradeNoteMinimumValue;
    }

    private static bool IsWantedTradeNoteByIdOrName(uint weenieClassId, string name)
    {
        if (TradeNoteValueByWeenieId.TryGetValue(weenieClassId, out int knownValue))
            return knownValue >= WantedTradeNoteMinimumValue;

        if (!name.StartsWith("Trade Note", StringComparison.OrdinalIgnoreCase))
            return false;

        return TryParseTradeNoteValue(name, out int parsedValue) && parsedValue >= WantedTradeNoteMinimumValue;
    }

    /// <summary>"Trade Note (10,000)" -&gt; 10000, the last resort when neither a known id nor a real value is available.</summary>
    private static bool TryParseTradeNoteValue(string name, out int value)
    {
        value = 0;
        int open = name.IndexOf('(');
        int close = name.IndexOf(')');
        if (open < 0 || close < 0 || close <= open)
            return false;

        string digits = name[(open + 1)..close].Replace(",", string.Empty);
        return int.TryParse(digits, out value);
    }
}
