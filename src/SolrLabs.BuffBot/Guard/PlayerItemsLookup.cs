using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Guard;

/// <summary>Takes <see cref="PluginWorldObject"/> values, not <c>IPluginHost</c>, so a fake list
/// tests this without a client.</summary>
internal static class PlayerItemsLookup
{
    /// <summary>Case-insensitive; no <see cref="PluginObjectClass.Player"/> filter narrows the
    /// search — the operator names who they mean and this trusts them.</summary>
    internal static bool TryFindByName(
        IReadOnlyList<PluginWorldObject> objects, string name, out PluginWorldObject found)
    {
        ArgumentNullException.ThrowIfNull(objects);
        ArgumentNullException.ThrowIfNull(name);

        foreach (PluginWorldObject candidate in objects)
        {
            if (string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                found = candidate;
                return true;
            }
        }

        found = default;
        return false;
    }

    /// <summary>A wielded item arrives as its own entity spawn, in the same batch <see
    /// cref="TryFindByName"/> searched.</summary>
    internal static IReadOnlyList<PluginWorldObject> WieldedBy(
        IReadOnlyList<PluginWorldObject> objects, uint wielderObjectId)
    {
        ArgumentNullException.ThrowIfNull(objects);

        var wielded = new List<PluginWorldObject>();
        foreach (PluginWorldObject candidate in objects)
            if (candidate.WielderObjectId == wielderObjectId)
                wielded.Add(candidate);

        return wielded;
    }
}
