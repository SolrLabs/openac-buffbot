namespace SolrLabs.BuffBot.Spells;

/// <summary>Operates in the spell-components table's own id space — a scarab or the Prismatic
/// Taper here is a component id, not the weenie class id a caller joins it to.</summary>
internal static class EffectiveFormula
{
    /// <summary>The Prismatic Taper's formula component id, distinct from its weenie class id
    /// (<see cref="Components.ContributionAdvisor.PrismaticTaperWeenieId"/>).</summary>
    private const uint PrismaticTaperComponentId = 0xBCu;

    /// <summary>A power component's strength, 1-10, or 0 otherwise. Ids 1-6 are the six scarab
    /// colours; 0x6E/0x70/0xC0/0xC1 are Diamond, Platinum, Dark and Mana.</summary>
    internal static uint DeterminePowerLevelOfComponent(uint componentId) => componentId switch
    {
        >= 1u and <= 6u => componentId,
        0x6Eu => 7u,
        0x70u => 8u,
        0xC0u => 9u,
        0xC1u => 10u,
        _ => 0u,
    };

    /// <summary>Ids <see cref="ScarabOnlyFormula"/> keeps: the six scarabs, Diamond/Platinum/Dark/
    /// Mana, and Chorizite (0x6F), which carries no power level of its own.</summary>
    private static bool IsPowerComponent(uint componentId) => componentId is
        >= 1u and <= 6u or 0x6Eu or 0x6Fu or 0x70u or 0xC0u or 0xC1u;

    /// <summary>A 0 entry among <paramref name="rawComponentIds"/>'s first 8 ends the formula
    /// early, as in the raw one.</summary>
    internal static IReadOnlyList<uint> ScarabOnlyFormula(IReadOnlyList<uint> rawComponentIds)
    {
        ArgumentNullException.ThrowIfNull(rawComponentIds);

        var result = new List<uint>(8);
        uint strongestPower = 0u;
        for (int i = 0; i < rawComponentIds.Count && i < 8; i++)
        {
            uint component = rawComponentIds[i];
            if (component == 0u)
                break;
            if (!IsPowerComponent(component))
                continue;

            result.Add(component);
            strongestPower = Math.Max(strongestPower, DeterminePowerLevelOfComponent(component));
        }

        int taperCount = strongestPower switch
        {
            1u => 1,
            2u => 2,
            3u or 4u or 7u => 3,
            5u or 6u or 8u or 9u or 10u => 4,
            _ => 0,
        };
        while (taperCount-- > 0 && result.Count < 8)
            result.Add(PrismaticTaperComponentId);
        return result;
    }
}
