using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Components;

/// <summary>Rows are narrowed to what <see cref="CastingReagents"/> calls a reagent; a carried
/// reagent gets a row too, at <see cref="ComponentUsage.UsedBy"/> 0.</summary>
internal static class ComponentReportBuilder
{
    /// <summary><paramref name="inventoryReadable"/> gates on the item surface being bound — <see
    /// cref="IItemAutomation.IsAvailable"/> is false on the headless host even though reads still work.</summary>
    internal static ComponentReport Build(
        ISpellCatalog catalog, IItemAutomation items, bool inventoryReadable, bool allLearnedTiers = false,
        Func<uint, bool>? usesScarabOnlyFormula = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(items);

        if (!inventoryReadable)
            return ComponentReport.Unavailable;

        IReadOnlyList<PluginSpellInfo> spells = ResolveNeededSpells(catalog.KnownSelfBuffs, allLearnedTiers);

        var usedByComponent = new Dictionary<uint, int>();
        foreach (PluginSpellInfo spell in spells)
        {
            bool useScarabOnly = usesScarabOnlyFormula?.Invoke(spell.School) ?? false;
            foreach (uint componentId in ComponentIdsOf(spell, useScarabOnly))
                usedByComponent[componentId] = usedByComponent.GetValueOrDefault(componentId) + 1;
        }

        // The headless host never binds a magic catalog. Checked before the reagent filter below,
        // so a profile that only needed a filtered-out reagent is never mistaken for this.
        var usedRows = new List<ComponentUsage>(usedByComponent.Count);
        bool anyResolved = false;
        foreach ((uint componentId, int usedBy) in usedByComponent)
        {
            // No catalog entry for this id: skip it, don't guess a name or size.
            if (!catalog.TryGetComponent(componentId, out PluginSpellComponentInfo info))
                continue;

            anyResolved = true;
            usedRows.Add(new ComponentUsage(info.WeenieClassId, info.Name, Stock: 0, usedBy));
        }

        if (usedByComponent.Count > 0 && !anyResolved)
            return ComponentReport.CatalogUnavailable;

        var stockByWeenie = new Dictionary<uint, int>();
        var heldReagentNameByWeenie = new Dictionary<uint, string>();
        foreach (PluginInventoryItem item in items.CaptureOwnedItems())
        {
            stockByWeenie[item.WeenieClassId] = stockByWeenie.GetValueOrDefault(item.WeenieClassId) + item.StackSize;
            if (CastingReagents.IsReagent(item) && !heldReagentNameByWeenie.ContainsKey(item.WeenieClassId))
                heldReagentNameByWeenie[item.WeenieClassId] = item.Name;
        }

        // A resolved Talisman/Herb/Powder/Potion still counts in usedRows above, so
        // CatalogUnavailable isn't wrongly reported, but isn't listed here.
        var rowsByWeenie = new Dictionary<uint, ComponentUsage>();
        foreach (ComponentUsage row in usedRows)
        {
            if (!CastingReagents.IsReagent(row.WeenieClassId, row.Name))
                continue;
            rowsByWeenie[row.WeenieClassId] = row with { Stock = stockByWeenie.GetValueOrDefault(row.WeenieClassId) };
        }

        // Every reagent the bot is carrying gets a row too, even one no spell currently needs;
        // UsedBy 0 marks it as held rather than needed.
        foreach ((uint weenieClassId, string name) in heldReagentNameByWeenie)
        {
            if (rowsByWeenie.ContainsKey(weenieClassId))
                continue;
            rowsByWeenie[weenieClassId] =
                new ComponentUsage(weenieClassId, name, stockByWeenie[weenieClassId], UsedBy: 0);
        }

        if (rowsByWeenie.Count == 0)
            return ComponentReport.Empty;

        var rows = new List<ComponentUsage>(rowsByWeenie.Values);
        rows.Sort(static (left, right) =>
        {
            int byStock = left.Stock.CompareTo(right.Stock);
            return byStock != 0 ? byStock : string.CompareOrdinal(left.Name, right.Name);
        });
        return new ComponentReport(true, rows);
    }

    /// <summary>Every named set in <see cref="DefaultSpellSets.Table"/> except <see
    /// cref="DefaultSpellSets.SelfDefence"/>, deduplicated by spell id.</summary>
    private static IReadOnlyList<PluginSpellInfo> ResolveNeededSpells(
        IReadOnlyList<PluginSpellInfo> catalog, bool allLearnedTiers)
    {
        var seenSpellIds = new HashSet<uint>();
        var spells = new List<PluginSpellInfo>();

        foreach ((string setName, IReadOnlyList<string> lines) in DefaultSpellSets.Table)
        {
            if (string.Equals(setName, DefaultSpellSets.SelfDefence, StringComparison.OrdinalIgnoreCase))
                continue;

            SpellTargetKind kind =
                string.Equals(setName, DefaultSpellSets.Self, StringComparison.OrdinalIgnoreCase)
                || string.Equals(setName, DefaultSpellSets.ManaUpkeep, StringComparison.OrdinalIgnoreCase)
                    ? SpellTargetKind.Self
                    : SpellTargetKind.Other;

            foreach (ResolvedSpell resolved in SpellSelector.ResolveLenient(catalog, lines, kind))
            {
                if (!allLearnedTiers)
                {
                    if (seenSpellIds.Add(resolved.Spell.SpellId))
                        spells.Add(resolved.Spell);
                    continue;
                }

                IReadOnlyList<PluginSpellInfo> tiers =
                    resolved.LearnedTiersDescending.Count > 0
                        ? resolved.LearnedTiersDescending
                        : [resolved.Spell];
                foreach (PluginSpellInfo tier in tiers)
                    if (seenSpellIds.Add(tier.SpellId))
                        spells.Add(tier);
            }
        }

        return spells;
    }

    /// <summary>True routes through <see cref="EffectiveFormula.ScarabOnlyFormula"/> instead of
    /// <see cref="PluginSpellInfo.ComponentSet"/>, which it never touches.</summary>
    internal static IEnumerable<uint> ComponentIdsOf(PluginSpellInfo spell, bool usesScarabOnlyFormula)
    {
        var ids = new HashSet<uint>();

        if (usesScarabOnlyFormula)
        {
            foreach (uint id in EffectiveFormula.ScarabOnlyFormula(spell.FormulaComponentIds))
                if (id != 0u)
                    ids.Add(id);
            return ids;
        }

        foreach (uint id in spell.FormulaComponentIds)
            if (id != 0u)
                ids.Add(id);

        PluginSpellComponentSet set = spell.ComponentSet;
        if (set.Herb != 0u) ids.Add(set.Herb);
        if (set.Powder != 0u) ids.Add(set.Powder);
        if (set.Potion != 0u) ids.Add(set.Potion);
        if (set.Talisman != 0u) ids.Add(set.Talisman);

        return ids;
    }
}
