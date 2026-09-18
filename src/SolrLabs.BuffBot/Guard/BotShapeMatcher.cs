using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Guard;

/// <summary>Matches <see cref="DefaultReplies.BotShapePatterns"/>, where <c>*</c> stands for the
/// variable part.</summary>
internal static class BotShapeMatcher
{
    internal static bool IsBotShaped(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string trimmed = text.Trim();

        foreach (string pattern in DefaultReplies.BotShapePatterns)
        {
            if (Matches(trimmed, pattern))
                return true;
        }

        return false;
    }

    private static bool Matches(string text, string pattern)
    {
        if (!pattern.Contains('*'))
            return string.Equals(text, pattern, StringComparison.Ordinal);

        string[] segments = pattern.Split('*');
        int cursor = 0;

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i];
            bool isFirst = i == 0;
            bool isLast = i == segments.Length - 1;

            if (segment.Length == 0)
                continue;

            if (isFirst)
            {
                if (!text.StartsWith(segment, StringComparison.Ordinal))
                    return false;
                cursor = segment.Length;
                continue;
            }

            if (isLast)
            {
                return text.Length - segment.Length >= cursor
                    && text.EndsWith(segment, StringComparison.Ordinal);
            }

            int found = text.IndexOf(segment, cursor, StringComparison.Ordinal);
            if (found < 0)
                return false;
            cursor = found + segment.Length;
        }

        return true;
    }
}
