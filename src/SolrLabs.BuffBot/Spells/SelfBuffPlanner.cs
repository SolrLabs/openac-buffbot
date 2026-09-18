using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Spells;

/// <summary>Due means missing from <see cref="ICharacterInfo.ActiveEnchantments"/> or under the
/// five-minute mark.</summary>
internal static class SelfBuffPlanner
{
    internal const double DueThresholdSeconds = 300d;

    internal static IReadOnlyList<ResolvedSpell> PlanDue(
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<string> selfLines,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(selfLines);
        ArgumentNullException.ThrowIfNull(activeEnchantments);

        IReadOnlyList<ResolvedSpell> learned =
            SpellSelector.ResolveLenient(catalog, selfLines, SpellTargetKind.Self);
        if (learned.Count == 0)
            return learned;

        var remainingByFamily = new Dictionary<uint, double>();
        foreach (PluginActiveEnchantment enchantment in activeEnchantments)
            remainingByFamily[enchantment.Family] = enchantment.SecondsRemaining;

        var due = new List<ResolvedSpell>(learned.Count);
        foreach (ResolvedSpell spell in learned)
        {
            bool stillUp = remainingByFamily.TryGetValue(spell.Spell.Family, out double remaining)
                && remaining >= DueThresholdSeconds;
            if (!stillUp)
                due.Add(spell);
        }

        return due;
    }
}
