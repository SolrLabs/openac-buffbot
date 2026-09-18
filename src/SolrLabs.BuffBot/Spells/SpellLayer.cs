namespace SolrLabs.BuffBot.Spells;

/// <summary>Both kinds share one ordered list, so nesting position is never fixed relative to a
/// layer's own lines — <c>_generic</c> puts "prots" after its own lines, unlike a style layer.</summary>
internal readonly record struct SpellLayerElement
{
    private SpellLayerElement(string? line, string? layerName)
    {
        Line = line;
        LayerName = layerName;
    }

    internal string? Line { get; } // the spell line to cast, when this element is not a nested layer

    internal string? LayerName { get; } // the layer to expand here, when this element is not a spell line

    internal static SpellLayerElement Cast(string line) => new(line, null);

    internal static SpellLayerElement Layer(string name) => new(null, name);
}

/// <summary>A named, ordered composition of spell lines and nested layers; <see
/// cref="SpellProfileResolver"/> flattens one, and everything it includes, into a deduplicated plan.</summary>
internal sealed record SpellLayer(string Name, IReadOnlyList<SpellLayerElement> Elements);
