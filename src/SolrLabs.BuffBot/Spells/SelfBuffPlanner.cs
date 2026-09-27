using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Spells;

/// <summary>Due means missing, under the five-minute mark (or half the duration, whichever is
/// shorter), or out-tiered against the tier the caster would cast right now.</summary>
internal static class SelfBuffPlanner
{
    internal const double DueThresholdSeconds = 300d;

    /// <summary>Only the individually due lines.</summary>
    internal static IReadOnlyList<ResolvedSpell> PlanDue(
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<string> selfLines,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments,
        int? componentCeilingRung = null,
        IReadOnlyDictionary<uint, int>? lastLandedTierByFamily = null,
        IReadOnlyCollection<uint>? refusedFamilies = null) =>
        PlanDue(
            SpellSelector.BuildIndex(catalog), selfLines, activeEnchantments, componentCeilingRung,
            lastLandedTierByFamily, refusedFamilies);

    /// <summary>Same as the catalog overload, against an index the caller already built.</summary>
    internal static IReadOnlyList<ResolvedSpell> PlanDue(
        SpellSelector.SpellLineIndex index,
        IReadOnlyList<string> selfLines,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments,
        int? componentCeilingRung = null,
        IReadOnlyDictionary<uint, int>? lastLandedTierByFamily = null,
        IReadOnlyCollection<uint>? refusedFamilies = null) =>
        Evaluate(index, selfLines, activeEnchantments, componentCeilingRung, lastLandedTierByFamily, refusedFamilies).Due;

    /// <summary>Every learned line at its top tier, but only when at least one is due.</summary>
    internal static IReadOnlyList<ResolvedSpell> PlanFullRebuff(
        IReadOnlyList<PluginSpellInfo> catalog,
        IReadOnlyList<string> selfLines,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments,
        int? componentCeilingRung = null,
        IReadOnlyDictionary<uint, int>? lastLandedTierByFamily = null,
        IReadOnlyCollection<uint>? refusedFamilies = null) =>
        PlanFullRebuff(
            SpellSelector.BuildIndex(catalog), selfLines, activeEnchantments, componentCeilingRung,
            lastLandedTierByFamily, refusedFamilies);

    /// <summary>Same as the catalog overload, against an already-built index.</summary>
    internal static IReadOnlyList<ResolvedSpell> PlanFullRebuff(
        SpellSelector.SpellLineIndex index,
        IReadOnlyList<string> selfLines,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments,
        int? componentCeilingRung = null,
        IReadOnlyDictionary<uint, int>? lastLandedTierByFamily = null,
        IReadOnlyCollection<uint>? refusedFamilies = null)
    {
        (IReadOnlyList<ResolvedSpell> learned, IReadOnlyList<ResolvedSpell> due) = Evaluate(
            index, selfLines, activeEnchantments, componentCeilingRung, lastLandedTierByFamily, refusedFamilies);
        return due.Count > 0 ? learned : Array.Empty<ResolvedSpell>();
    }

    private static (IReadOnlyList<ResolvedSpell> Learned, IReadOnlyList<ResolvedSpell> Due) Evaluate(
        SpellSelector.SpellLineIndex index,
        IReadOnlyList<string> selfLines,
        IReadOnlyList<PluginActiveEnchantment> activeEnchantments,
        int? componentCeilingRung,
        IReadOnlyDictionary<uint, int>? lastLandedTierByFamily,
        IReadOnlyCollection<uint>? refusedFamilies)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(selfLines);
        ArgumentNullException.ThrowIfNull(activeEnchantments);

        // The self set always resolves top learned tier; a ceiling or a remembered step-down only
        // moves the target used for the due comparison below.
        IReadOnlyList<ResolvedSpell> learned = SpellSelector.ResolveLenient(index, selfLines, SpellTargetKind.Self);
        if (learned.Count == 0)
            return (learned, learned);

        var activeByFamily = new Dictionary<uint, PluginActiveEnchantment>();
        foreach (PluginActiveEnchantment enchantment in activeEnchantments)
        {
            if (!activeByFamily.TryGetValue(enchantment.Family, out PluginActiveEnchantment existing)
                || enchantment.Tier > existing.Tier
                || (enchantment.Tier == existing.Tier && enchantment.SecondsRemaining > existing.SecondsRemaining))
                activeByFamily[enchantment.Family] = enchantment;
        }

        var due = new List<ResolvedSpell>(learned.Count);
        foreach (ResolvedSpell spell in learned)
        {
            if (!activeByFamily.TryGetValue(spell.Spell.Family, out PluginActiveEnchantment active))
            {
                // A remembered refusal doesn't retrigger on its own; it still rides along whenever
                // some other line is due.
                if (refusedFamilies is null || !refusedFamilies.Contains(spell.Spell.Family))
                    due.Add(spell);
                continue;
            }

            PluginSpellInfo target = EffectiveTarget(spell, componentCeilingRung);
            double threshold = Math.Min(DueThresholdSeconds, target.DurationSeconds / 2d);
            bool expiringSoon = active.SecondsRemaining < threshold;

            bool outTiered = active.Tier < target.Tier;
            if (outTiered
                && lastLandedTierByFamily is not null
                && lastLandedTierByFamily.TryGetValue(spell.Spell.Family, out int rememberedTier)
                && rememberedTier == active.Tier)
                outTiered = false; // the tier a step-down deliberately landed at last time.

            if (expiringSoon || outTiered)
                due.Add(spell);
        }

        return (learned, due);
    }

    /// <summary>Top learned, floored by a persisted component ceiling rung.</summary>
    private static PluginSpellInfo EffectiveTarget(ResolvedSpell spell, int? componentCeilingRung)
    {
        if (componentCeilingRung is not { } rung || rung <= 0)
            return spell.Spell;

        IReadOnlyList<PluginSpellInfo> ladder = spell.LearnedTiersDescending;
        if (ladder.Count == 0)
            return spell.Spell;

        return ladder[Math.Min(rung, ladder.Count - 1)];
    }
}
