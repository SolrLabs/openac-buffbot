using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Components;

/// <summary>Retail's scarab-only formula. Keyed by magic skill id, not school number.</summary>
internal static class CasterFormulaMode
{
    private static readonly IReadOnlyDictionary<uint, uint> FocusWeenieClassIdBySchool = new Dictionary<uint, uint>
    {
        [34u] = 15271u, // Foci of Strife (War Magic)
        [33u] = 15270u, // Foci of Verdancy (Life Magic)
        [32u] = 15269u, // Foci of Artifice (Item Enchantment)
        [31u] = 15268u, // Foci of Enchantment (Creature Enchantment)
        [43u] = 43173u, // Foci of Shadow (Void Magic)
    };

    private static readonly IReadOnlyDictionary<uint, uint> InfusionPropertyIdBySchool = new Dictionary<uint, uint>
    {
        [34u] = 0x129u, // AugmentationInfusedWarMagic
        [33u] = 0x128u, // AugmentationInfusedLifeMagic
        [32u] = 0x127u, // AugmentationInfusedItemMagic
        [31u] = 0x126u, // AugmentationInfusedCreatureMagic
        [43u] = 0x148u, // AugmentationInfusedVoidMagic
    };

    /// <summary><paramref name="ownedItems"/> is main-pack items only, as retail counts it.</summary>
    internal static bool UsesScarabOnlyFormula(
        uint school,
        IReadOnlyList<PluginInventoryItem> ownedItems,
        PluginItemProperties? selfProperties)
    {
        ArgumentNullException.ThrowIfNull(ownedItems);

        if (FocusWeenieClassIdBySchool.TryGetValue(school, out uint focusWeenieClassId))
            foreach (PluginInventoryItem item in ownedItems)
                if (item.WeenieClassId == focusWeenieClassId)
                    return true;

        return selfProperties is { } properties
            && InfusionPropertyIdBySchool.TryGetValue(school, out uint propertyId)
            && properties.Ints.TryGetValue(propertyId, out int level)
            && level > 0;
    }
}
