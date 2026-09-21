using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Spells;

/// <summary>Every learned, targeted, beneficial non-debuff spell, self and other alike.
/// <see cref="ISpellCatalog.KnownSelfBuffs"/> holds only what the caster can target on itself.</summary>
internal static class BeneficialSpellCatalog
{
    internal static IReadOnlyList<PluginSpellInfo> Resolve(ISpellCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        IReadOnlyList<PluginSpellInfo> selfBuffs = catalog.KnownSelfBuffs;
        IReadOnlyList<PluginSpellInfo> all = catalog.All;
        if (all.Count == 0)
            return selfBuffs;

        var seen = new HashSet<uint>(selfBuffs.Count);
        var result = new List<PluginSpellInfo>(selfBuffs.Count);
        foreach (PluginSpellInfo spell in selfBuffs)
            if (seen.Add(spell.SpellId))
                result.Add(spell);

        foreach (PluginSpellInfo spell in all)
        {
            if (!spell.IsBeneficial || spell.IsDebuff || spell.IsUntargeted)
                continue;
            if (!catalog.IsKnown(spell.SpellId))
                continue;
            if (seen.Add(spell.SpellId))
                result.Add(spell);
        }

        // Matches the host's own KnownSelfBuffs ordering: family ascending, strongest tier first.
        result.Sort(static (a, b) =>
            a.Family != b.Family ? a.Family.CompareTo(b.Family) : b.Tier.CompareTo(a.Tier));
        return result;
    }
}
