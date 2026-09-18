using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Donations;

/// <summary>Resolves a parsed <see cref="GiveLine"/>'s giver name against this tick's captured objects by exact, case-insensitive name, with a leading <c>+</c> admin prefix stripped from both sides first. Ambiguous or not found both resolve to an unknown giver.</summary>
internal static class GiverResolver
{
    internal enum GiverLookup
    {
        Found,
        Ambiguous,
        NotFound,
    }

    internal readonly record struct GiverResolution(GiverLookup Result, uint ObjectId, string Name);

    internal static GiverResolution Resolve(IReadOnlyList<PluginWorldObject> objects, string giverName)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(giverName);

        string normalizedGiver = Normalize(giverName);
        uint foundId = 0;
        string foundName = string.Empty;
        int count = 0;

        foreach (PluginWorldObject candidate in objects)
        {
            if (!string.Equals(Normalize(candidate.Name), normalizedGiver, StringComparison.OrdinalIgnoreCase))
                continue;

            foundId = candidate.ObjectId;
            foundName = candidate.Name;
            count++;
        }

        return count switch
        {
            0 => new GiverResolution(GiverLookup.NotFound, 0, string.Empty),
            1 => new GiverResolution(GiverLookup.Found, foundId, foundName),
            _ => new GiverResolution(GiverLookup.Ambiguous, 0, string.Empty),
        };
    }

    private static string Normalize(string name) => name.StartsWith('+') ? name[1..] : name;
}
