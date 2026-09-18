using System.Globalization;
using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;

namespace SolrLabs.BuffBot.Donations;

/// <summary>One parsed "&lt;Name&gt; gives you &lt;item&gt;." chat line. <see cref="ItemName"/> has any leading article or count already stripped — for a single-item gift (<see cref="Count"/> 1) it matches the item's own singular name exactly; for a counted gift it is ACE's own plural text ("5 Lead Scarabs").</summary>
internal readonly record struct GiveLine(string GiverName, string ItemName, int Count);

/// <summary>Turns one captured chat line into a <see cref="GiveLine"/>, or <see langword="null"/> when it is not a completed direct give. A direct give arrives as system chat with no sender: <c>Sender</c> empty, <c>SenderObjectId</c> 0.</summary>
internal static class GiveLineParser
{
    private const string GivesMarker = " gives you ";
    private const string TriesToGiveMarker = " tries to give you ";

    internal static GiveLine? TryParse(PluginChatMessage message)
    {
        if (message.Kind != (int)ChatKind.System || message.SenderObjectId != 0u)
            return null;

        string text = message.Text;
        int markerIndex = text.IndexOf(GivesMarker, StringComparison.Ordinal);
        if (markerIndex <= 0)
            return null;

        string giver = text[..markerIndex];
        string rest = text[(markerIndex + GivesMarker.Length)..];
        if (!rest.EndsWith('.'))
            return null;
        rest = rest[..^1];

        (string itemName, int count) = SplitCountAndItem(rest);
        if (giver.Length == 0 || itemName.Length == 0)
            return null;

        return new GiveLine(giver, itemName, count);
    }

    /// <summary>The giver's name out of "&lt;Name&gt; tries to give you &lt;item&gt;." — a refused give ACE never completes, so only the giver is worth reading. Same message shape as <see cref="TryParse"/>.</summary>
    internal static string? TryParseTriesToGiveGiverName(PluginChatMessage message)
    {
        if (message.Kind != (int)ChatKind.System || message.SenderObjectId != 0u)
            return null;

        string text = message.Text;
        int markerIndex = text.IndexOf(TriesToGiveMarker, StringComparison.Ordinal);
        return markerIndex > 0 ? text[..markerIndex] : null;
    }

    /// <summary>"5 Pyreal Scarabs" -&gt; (Pyreal Scarabs, 5); ACE writes a thousands separator into the count, so it is parsed with <see cref="NumberStyles.AllowThousands"/>. "a Fine Aged Cheese" -&gt; (Fine Aged Cheese, 1); an unrecognised shape passes through unchanged with a count of 1.</summary>
    private static (string ItemName, int Count) SplitCountAndItem(string rest)
    {
        int firstSpace = rest.IndexOf(' ');
        if (firstSpace > 0
            && int.TryParse(
                rest[..firstSpace], NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out int parsedCount)
            && parsedCount > 0)
        {
            return (rest[(firstSpace + 1)..], parsedCount);
        }

        if (rest.StartsWith("a ", StringComparison.Ordinal))
            return (rest[2..], 1);

        if (rest.StartsWith("an ", StringComparison.Ordinal))
            return (rest[3..], 1);

        return (rest, 1);
    }
}
