using System.Globalization;
using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Guard;

/// <summary>Console-only, behind <c>/buffbot inv</c> and <c>/buffbot chatdump</c>; the verbs
/// themselves call the abstractions interfaces directly from <c>BuffBotPlugin</c>.</summary>
internal static class OwnerItemCommands
{
    /// <summary>Accepts either a <c>0x</c>-prefixed hex object id or a plain decimal one — a log
    /// line prints hex, a player typing from memory usually doesn't.</summary>
    internal static bool TryParseObjectId(string token, out uint objectId)
    {
        ArgumentNullException.ThrowIfNull(token);
        token = token.Trim();

        if (token.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return uint.TryParse(
                token.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out objectId);

        return uint.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out objectId);
    }

    internal enum TargetLookup
    {
        Ok,
        NotFound,
        Ambiguous,
    }

    internal readonly record struct TargetResolution(TargetLookup Result, uint ObjectId, int MatchCount);

    /// <summary>Unlike <see cref="PlayerItemsLookup.TryFindByName"/>'s first match, more than one
    /// name match is reported ambiguous rather than silently picked, since <c>give</c> sends an item away.</summary>
    internal static TargetResolution ResolveTarget(IReadOnlyList<PluginWorldObject> objects, string token)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(token);

        if (TryParseObjectId(token, out uint id))
            return new TargetResolution(TargetLookup.Ok, id, 1);

        uint found = 0;
        int count = 0;
        foreach (PluginWorldObject candidate in objects)
        {
            if (!string.Equals(candidate.Name, token, StringComparison.OrdinalIgnoreCase))
                continue;

            found = candidate.ObjectId;
            count++;
        }

        return count switch
        {
            0 => new TargetResolution(TargetLookup.NotFound, 0, 0),
            1 => new TargetResolution(TargetLookup.Ok, found, 1),
            _ => new TargetResolution(TargetLookup.Ambiguous, 0, count),
        };
    }

    /// <summary>The <c>inv list [filter]</c> half that doesn't touch the host: an empty filter is
    /// everything, otherwise a case-insensitive substring match on the item's name.</summary>
    internal static IReadOnlyList<PluginInventoryItem> FilterOwnedItems(
        IReadOnlyList<PluginInventoryItem> items, string filter)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(filter);

        if (filter.Length == 0)
            return items;

        var matches = new List<PluginInventoryItem>();
        foreach (PluginInventoryItem item in items)
            if (item.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                matches.Add(item);

        return matches;
    }

    /// <summary><c>chatdump</c>'s own half of "last n" — <see
    /// cref="AcDream.Plugin.Abstractions.IPluginChat.CaptureMessages"/> itself has no notion of n.</summary>
    internal static IReadOnlyList<PluginChatMessage> TakeLastMessages(
        IReadOnlyList<PluginChatMessage> messages, int count)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (count <= 0 || messages.Count == 0)
            return Array.Empty<PluginChatMessage>();

        if (messages.Count <= count)
            return messages;

        return messages.Skip(messages.Count - count).ToList();
    }
}
