namespace SolrLabs.BuffBot.Spells;

/// <summary>No spell-line literal lives here; every one comes from the layer table a caller
/// supplies (<see cref="DefaultSpellSets"/>).</summary>
internal static class SpellProfileResolver
{
    /// <summary>A layer that includes itself, directly or through a cycle, is expanded at most
    /// once per branch, so resolution always terminates.</summary>
    internal static IReadOnlyList<string> Resolve(
        IReadOnlyDictionary<string, SpellLayer> layers, string layerName)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layerName);

        var lines = new List<string>();
        var seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var openBranch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Expand(layers, layerName, openBranch, seenLines, lines);
        return lines;
    }

    private static void Expand(
        IReadOnlyDictionary<string, SpellLayer> layers,
        string layerName,
        HashSet<string> openBranch,
        HashSet<string> seenLines,
        List<string> lines)
    {
        if (!openBranch.Add(layerName))
            return; // cyclic include: this layer is already being expanded higher up this branch.

        if (layers.TryGetValue(layerName, out SpellLayer? layer))
        {
            foreach (SpellLayerElement element in layer.Elements)
            {
                if (element.LayerName is { } nested)
                {
                    Expand(layers, nested, openBranch, seenLines, lines);
                    continue;
                }

                string line = element.Line!;
                if (seenLines.Add(line))
                    lines.Add(line);
            }
        }
        // An include naming a layer the table does not define contributes nothing — not this
        // resolver's problem to diagnose; DefaultSpellSets owns the table and its own tests do.

        openBranch.Remove(layerName);
    }
}
