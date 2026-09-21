using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Spells;

/// <summary><see cref="ISpellCatalog.KnownSelfBuffs"/> only promises "beneficial spells the
/// character can cast on themselves" — since OpenAC 8b2c5147, that excludes every "... Other"
/// line a requester's own profile needs. This rebuilds the wider set <c>KnownSelfBuffs</c> held
/// before that change: every learned spell that is beneficial, not a debuff, and not untargeted,
/// self and other alike, sourced from <see cref="ISpellCatalog.All"/> and confirmed learned
/// through <see cref="ISpellCatalog.IsKnown"/>. Unioned with <c>KnownSelfBuffs</c> itself,
/// deduplicated by <see cref="PluginSpellInfo.SpellId"/>, so nothing either list reports is
/// lost.</summary>
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
