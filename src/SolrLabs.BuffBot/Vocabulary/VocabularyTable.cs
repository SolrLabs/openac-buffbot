namespace SolrLabs.BuffBot.Vocabulary;

/// <summary>Phrase matching is case-insensitive.</summary>
internal sealed class VocabularyTable
{
    private readonly IReadOnlyDictionary<string, Intent> _phrases;
    private readonly IReadOnlyList<string> _phraseOrder;

    internal VocabularyTable(IEnumerable<(string Phrase, Intent Intent)> phrases)
    {
        ArgumentNullException.ThrowIfNull(phrases);

        var map = new Dictionary<string, Intent>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        foreach ((string phrase, Intent intent) in phrases)
        {
            string key = phrase.Trim();
            if (key.Length == 0)
                continue;
            if (!map.ContainsKey(key))
                order.Add(key);
            map[key] = intent;
        }

        _phrases = map;
        _phraseOrder = order;
    }

    /// <summary>Declaration order, not alphabetical.</summary>
    internal IReadOnlyList<string> Phrases => _phraseOrder;

    internal bool TryResolve(string text, out Intent intent)
    {
        ArgumentNullException.ThrowIfNull(text);
        return _phrases.TryGetValue(text.Trim(), out intent);
    }
}
