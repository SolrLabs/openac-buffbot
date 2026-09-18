using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Guard;

/// <summary>Finds the requester's own wielded shield among a captured object batch — the same
/// batch <see cref="PlayerItemsLookup"/> already searches.</summary>
internal static class ShieldFinder
{
    /// <summary>The first object in <paramref name="objects"/> wielded by <paramref
    /// name="wielderObjectId"/> and classified <see cref="PluginObjectClass.Armor"/>.</summary>
    internal static bool TryFindWieldedShield(
        IReadOnlyList<PluginWorldObject> objects, uint wielderObjectId, out PluginWorldObject shield)
    {
        ArgumentNullException.ThrowIfNull(objects);

        foreach (PluginWorldObject candidate in objects)
        {
            // No PluginObjectClass for a shield: it classifies Armor. Only wielded objects reach
            // this batch, never worn armor, so Armor among them is unambiguous as the shield.
            if (candidate.WielderObjectId == wielderObjectId
                && candidate.ObjectClass == PluginObjectClass.Armor)
            {
                shield = candidate;
                return true;
            }
        }

        shield = default;
        return false;
    }
}
