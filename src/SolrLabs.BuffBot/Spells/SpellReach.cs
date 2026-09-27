using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Spells;

/// <summary>How far a targeted beneficial spell reaches before the server refuses it, less a
/// one-metre margin. Host-free and pure.</summary>
internal static class SpellReach
{
    internal const double CapMetres = 75d;
    internal const double SafetyMarginMetres = 1d;

    // The largest reach a real spell could have; used when ranks or range metadata are unknown.
    internal const double UnknownReachMetres = CapMetres - SafetyMarginMetres;

    // ranks null means the skill could not be read; a real zero is not treated as unknown.
    internal static double Metres(PluginSpellInfo spell, uint? ranks)
    {
        if (ranks is null || (spell.BaseRangeConstant == 0f && spell.BaseRangeModifier == 0f))
            return UnknownReachMetres;

        double raw = spell.BaseRangeConstant + (spell.BaseRangeModifier * ranks.Value);
        return Math.Max(0d, Math.Min(raw, CapMetres) - SafetyMarginMetres);
    }

    // PluginSpellInfo.School doubles as the skill id; 0 means unmapped.
    internal static double ForSpell(PluginSpellInfo spell, Func<uint, uint?> ranksForSkill) =>
        Metres(spell, spell.School == 0u ? null : ranksForSkill(spell.School));
}
