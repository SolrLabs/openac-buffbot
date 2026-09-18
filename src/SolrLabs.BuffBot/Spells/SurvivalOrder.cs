namespace SolrLabs.BuffBot.Spells;

/// <summary>Equal-tier spells expire in cast order (retail behaviour), so a stable sort keeps
/// every line's position relative to others in its own band.</summary>
internal static class SurvivalOrder
{
    /// <summary>The four bands, in the order they expire — lowest first.</summary>
    internal enum Band
    {
        /// <summary>Expires first: the harmless early warning that buffs are running out, before
        /// anything that matters in a fight lapses.</summary>
        UtilityAndTrade = 0,

        /// <summary>What lands the hit: weapon-style and casting-school masteries, Sneak Attack
        /// Mastery, and the weapon auras.</summary>
        Offence = 1,

        /// <summary>The six attributes and the magic/trade masteries that shape the caster
        /// generally rather than any one fight.</summary>
        PersonalSkills = 2,

        /// <summary>Expires last: what keeps the target alive to use everything above — armor,
        /// the damage protections, the banes, and the vital-regeneration lines.</summary>
        Survival = 3,
    }

    /// <summary>Keyed on the line's own name, minus tier suffix.</summary>
    private static readonly IReadOnlyDictionary<string, Band> BandByLine =
        new Dictionary<string, Band>(StringComparer.OrdinalIgnoreCase)
        {
            // Utility and trade.
            ["Leadership Mastery Other"] = Band.UtilityAndTrade,
            ["Fealty Other"] = Band.UtilityAndTrade,
            ["Fletching Mastery Other"] = Band.UtilityAndTrade,
            ["Alchemy Mastery Other"] = Band.UtilityAndTrade,
            ["Cooking Mastery Other"] = Band.UtilityAndTrade,
            ["Lockpick Mastery Other"] = Band.UtilityAndTrade,
            ["Jumping Mastery Other"] = Band.UtilityAndTrade,
            ["Summoning Mastery Other"] = Band.UtilityAndTrade,
            ["Sprint Other"] = Band.UtilityAndTrade,
            ["Arcane Enlightenment Other"] = Band.UtilityAndTrade,
            ["Armor Tinkering Expertise Other"] = Band.UtilityAndTrade,
            ["Item Tinkering Expertise Other"] = Band.UtilityAndTrade,
            ["Magic Item Tinkering Expertise Other"] = Band.UtilityAndTrade,
            ["Weapon Tinkering Expertise Other"] = Band.UtilityAndTrade,

            // Offence.
            ["Heavy Weapon Mastery Other"] = Band.Offence,
            ["Light Weapon Mastery Other"] = Band.Offence,
            ["Finesse Weapon Mastery Other"] = Band.Offence,
            ["Two Handed Combat Mastery Other"] = Band.Offence,
            ["Missile Weapon Mastery Other"] = Band.Offence,
            ["Dual Wield Mastery Other"] = Band.Offence,
            ["Shield Mastery Other"] = Band.Offence,
            ["War Magic Mastery Other"] = Band.Offence,
            ["Void Magic Mastery Other"] = Band.Offence,
            ["Sneak Attack Mastery Other"] = Band.Offence,
            ["Aura of Heart Seeker Other"] = Band.Offence,
            ["Aura of Blood Drinker Other"] = Band.Offence,
            ["Aura of Swift Killer Other"] = Band.Offence,
            ["Aura of Spirit Drinker Other"] = Band.Offence,

            // Personal skills.
            ["Strength Other"] = Band.PersonalSkills,
            ["Endurance Other"] = Band.PersonalSkills,
            ["Coordination Other"] = Band.PersonalSkills,
            ["Quickness Other"] = Band.PersonalSkills,
            ["Focus Other"] = Band.PersonalSkills,
            ["Willpower Other"] = Band.PersonalSkills,
            ["Healing Mastery Other"] = Band.PersonalSkills,
            ["Creature Enchantment Mastery Other"] = Band.PersonalSkills,
            ["Item Enchantment Mastery Other"] = Band.PersonalSkills,
            ["Life Magic Mastery Other"] = Band.PersonalSkills,
            ["Mana Conversion Mastery Other"] = Band.PersonalSkills,
            ["Aura of Hermetic Link Other"] = Band.PersonalSkills,

            // Survival.
            ["Impregnability Other"] = Band.Survival,
            ["Invulnerability Other"] = Band.Survival,
            ["Magic Resistance Other"] = Band.Survival,
            ["Armor Other"] = Band.Survival,
            ["Acid Protection Other"] = Band.Survival,
            ["Bludgeoning Protection Other"] = Band.Survival,
            ["Blade Protection Other"] = Band.Survival,
            ["Fire Protection Other"] = Band.Survival,
            ["Cold Protection Other"] = Band.Survival,
            ["Lightning Protection Other"] = Band.Survival,
            ["Piercing Protection Other"] = Band.Survival,
            ["Aura of Defender Other"] = Band.Survival,
            ["Acid Bane"] = Band.Survival,
            ["Bludgeon Bane"] = Band.Survival,
            ["Blade Bane"] = Band.Survival,
            ["Flame Bane"] = Band.Survival,
            ["Frost Bane"] = Band.Survival,
            ["Lightning Bane"] = Band.Survival,
            ["Piercing Bane"] = Band.Survival,
            ["Impenetrability"] = Band.Survival,
            ["Regeneration Other"] = Band.Survival,
            ["Rejuvenation Other"] = Band.Survival,
            ["Mana Renewal Other"] = Band.Survival,
        };

    /// <summary>The band <paramref name="line"/> belongs to, or <see cref="Band.PersonalSkills"/>
    /// if <see cref="BandByLine"/> does not name it.</summary>
    internal static Band BandFor(string line) =>
        BandByLine.TryGetValue(line, out Band band) ? band : Band.PersonalSkills;

    /// <summary><paramref name="trace"/>, if given, is called once per distinct unlisted
    /// line.</summary>
    internal static IReadOnlyList<ResolvedSpell> Apply(
        IReadOnlyList<ResolvedSpell> plan, Action<string>? trace = null)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (plan.Count == 0)
            return plan;

        HashSet<string>? tracedLines = trace is null
            ? null
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var indexed = new (int Index, Band Band, ResolvedSpell Spell)[plan.Count];
        for (int i = 0; i < plan.Count; i++)
        {
            string line = plan[i].Line;
            if (!BandByLine.ContainsKey(line) && tracedLines is not null && tracedLines.Add(line))
                trace!($"survival order: \"{line}\" has no band entry, defaulting to personal skills");

            indexed[i] = (i, BandFor(line), plan[i]);
        }

        // OrderBy is stable; the explicit index tie-break makes that a property of this method's
        // own contract.
        return indexed
            .OrderBy(entry => entry.Band)
            .ThenBy(entry => entry.Index)
            .Select(entry => entry.Spell)
            .ToList();
    }
}
