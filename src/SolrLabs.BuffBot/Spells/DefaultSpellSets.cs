namespace SolrLabs.BuffBot.Spells;

/// <summary>Every literal spell name lives here (minus tier suffix, e.g. "Strength Other I"),
/// never in <see cref="SpellProfileResolver"/>.</summary>
internal static class DefaultSpellSets
{
    /// <summary>The one flat set: unlike the others, not composed via <see cref="Layers"/>.</summary>
    internal const string Buff = "buff";

    internal const string Self = "self";

    /// <summary>Stamina to Mana Self trades stamina for mana; Revitalize Self repays it. Cast on
    /// demand by <see cref="Casting.CastStateMachine"/> when a plan needs more mana, not on a schedule.</summary>
    internal const string ManaUpkeep = "mana-upkeep";

    /// <summary>No player phrase resolves to it; <see cref="SelfBuffPlanner"/> never plans it
    /// automatically.</summary>
    internal const string SelfDefence = "self-defence";

    internal const string Prots = "prots";

    /// <summary>Nested only by the weapon-style profiles; absent from <see cref="Table"/> on its
    /// own.</summary>
    internal const string Melee = "_melee";

    internal const string XpChain = "xpchain";

    /// <summary>Nests <see cref="XpChain"/> and <see cref="Prots"/> last.</summary>
    internal const string Generic = "_generic";

    internal const string Heavy = "heavy";
    internal const string Light = "light";
    internal const string Finesse = "finesse";
    internal const string Missile = "missile";
    internal const string Void = "void";
    internal const string Mage = "mage";
    internal const string TwoHanded = "2h";

    /// <summary>The single-weapon melee shape minus the weapon-type and shield masteries a
    /// dual-wielder does not use.</summary>
    internal const string Dual = "dual";

    internal const string Tink = "tink";

    /// <summary>Does not nest <see cref="Prots"/> or <see cref="Melee"/>.</summary>
    internal const string Trades = "trades";

    /// <summary>Impenetrability enchants worn armor, not a carried shield like the seven bane
    /// lines, and stays blocked while they are castable (<see cref="BaneLinesFor"/>).</summary>
    internal const string Banes = "banes";

    private static bool IsBlockedByItemTargeting(string line) =>
        line.EndsWith(" Bane", StringComparison.OrdinalIgnoreCase)
        || string.Equals(line, "Impenetrability", StringComparison.OrdinalIgnoreCase);

    private static IReadOnlyList<string> Castable(string layerName) =>
        SpellProfileResolver.Resolve(Layers, layerName)
            .Where(line => !IsBlockedByItemTargeting(line))
            .ToList();

    /// <summary>Empty if <paramref name="profileName"/> does not nest <see cref="Banes"/> at all.
    /// Excludes Impenetrability — it targets worn armor, not the wielded shield.</summary>
    internal static IReadOnlyList<string> BaneLinesFor(string profileName)
    {
        if (!Layers.ContainsKey(profileName))
            return Array.Empty<string>();

        var lines = new List<string>();
        foreach (string line in SpellProfileResolver.Resolve(Layers, profileName))
            if (line.EndsWith(" Bane", StringComparison.OrdinalIgnoreCase))
                lines.Add(line);
        return lines;
    }

    /// <summary>Read only by the console-only <c>/buffbot items</c> diagnostic; never reachable
    /// from a player request.</summary>
    internal static IReadOnlyList<string> AllItemTargetedLines()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = new List<string>();
        foreach (string layerName in Layers.Keys)
            foreach (string line in SpellProfileResolver.Resolve(Layers, layerName))
                if (IsBlockedByItemTargeting(line) && seen.Add(line))
                    lines.Add(line);
        return lines;
    }

    /// <summary>Flattened by <see cref="SpellProfileResolver"/> into <see cref="Table"/>'s entries.</summary>
    internal static IReadOnlyDictionary<string, SpellLayer> Layers { get; } =
        new Dictionary<string, SpellLayer>(StringComparer.OrdinalIgnoreCase)
        {
            [Prots] = new SpellLayer(Prots,
            [
                SpellLayerElement.Cast("Acid Protection Other"),
                SpellLayerElement.Cast("Bludgeoning Protection Other"),
                SpellLayerElement.Cast("Blade Protection Other"),
                SpellLayerElement.Cast("Fire Protection Other"),
                SpellLayerElement.Cast("Cold Protection Other"),
                SpellLayerElement.Cast("Lightning Protection Other"),
                SpellLayerElement.Cast("Piercing Protection Other"),
                SpellLayerElement.Cast("Aura of Defender Other"), // creature-targeted; reaches the requester.
            ]),
            [Melee] = new SpellLayer(Melee,
            [
                SpellLayerElement.Cast("Aura of Heart Seeker Other"), // creature-targeted; reaches the requester.
                SpellLayerElement.Cast("Aura of Blood Drinker Other"), // creature-targeted; reaches the requester.
                SpellLayerElement.Cast("Aura of Swift Killer Other"), // creature-targeted; reaches the requester.
            ]),
            [XpChain] = new SpellLayer(XpChain,
            [
                SpellLayerElement.Cast("Leadership Mastery Other"),
                SpellLayerElement.Cast("Fealty Other"),
            ]),
            [Generic] = new SpellLayer(Generic,
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Cast("Endurance Other"),
                SpellLayerElement.Cast("Quickness Other"),
                SpellLayerElement.Cast("Coordination Other"),
                SpellLayerElement.Cast("Focus Other"),
                SpellLayerElement.Cast("Willpower Other"),
                SpellLayerElement.Cast("Creature Enchantment Mastery Other"),
                SpellLayerElement.Cast("Item Enchantment Mastery Other"),
                SpellLayerElement.Cast("Life Magic Mastery Other"),
                SpellLayerElement.Cast("Mana Conversion Mastery Other"),
                SpellLayerElement.Cast("Regeneration Other"),
                SpellLayerElement.Cast("Rejuvenation Other"),
                SpellLayerElement.Cast("Mana Renewal Other"),
                SpellLayerElement.Cast("Arcane Enlightenment Other"),
                SpellLayerElement.Cast("Lockpick Mastery Other"),
                SpellLayerElement.Cast("Summoning Mastery Other"),
                SpellLayerElement.Cast("Jumping Mastery Other"),
                SpellLayerElement.Cast("Healing Mastery Other"),
                SpellLayerElement.Cast("Sprint Other"),
                SpellLayerElement.Cast("Impregnability Other"),
                SpellLayerElement.Cast("Invulnerability Other"),
                SpellLayerElement.Cast("Magic Resistance Other"),
                SpellLayerElement.Cast("Armor Other"),
                SpellLayerElement.Cast("Aura of Hermetic Link Other"), // creature-targeted; reaches the requester.
                // No "Arcanum Salvaging Other" exists in the catalog, so it is left out here.
                SpellLayerElement.Layer(XpChain),
                SpellLayerElement.Layer(Prots),
            ]),
            // Weapon/school masteries raise the target's own skill; naming a playstyle is how a
            // requester opts into that skill.
            [Heavy] = new SpellLayer(Heavy,
            [
                SpellLayerElement.Layer(Melee),
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("Heavy Weapon Mastery Other"),
                SpellLayerElement.Cast("Dual Wield Mastery Other"),
                SpellLayerElement.Cast("Shield Mastery Other"),
                SpellLayerElement.Layer(Banes),
            ]),
            [Light] = new SpellLayer(Light,
            [
                SpellLayerElement.Layer(Melee),
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("Light Weapon Mastery Other"),
                SpellLayerElement.Cast("Dual Wield Mastery Other"),
                SpellLayerElement.Cast("Shield Mastery Other"),
                SpellLayerElement.Layer(Banes),
            ]),
            [Finesse] = new SpellLayer(Finesse,
            [
                SpellLayerElement.Layer(Melee),
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("Finesse Weapon Mastery Other"),
                SpellLayerElement.Cast("Dual Wield Mastery Other"),
                SpellLayerElement.Cast("Shield Mastery Other"),
                SpellLayerElement.Layer(Banes),
            ]),
            [TwoHanded] = new SpellLayer(TwoHanded,
            [
                SpellLayerElement.Layer(Melee),
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("Two Handed Combat Mastery Other"),
                SpellLayerElement.Layer(Banes),
            ]),
            [Missile] = new SpellLayer(Missile,
            [
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("Fletching Mastery Other"),
                SpellLayerElement.Cast("Alchemy Mastery Other"),
                SpellLayerElement.Cast("Aura of Heart Seeker Other"), // creature-targeted; reaches the requester.
                SpellLayerElement.Cast("Aura of Blood Drinker Other"), // creature-targeted; reaches the requester.
                SpellLayerElement.Cast("Aura of Swift Killer Other"), // creature-targeted; reaches the requester.
                SpellLayerElement.Cast("Missile Weapon Mastery Other"),
            ]),
            [Void] = new SpellLayer(Void,
            [
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("Sneak Attack Mastery Other"),
                SpellLayerElement.Cast("Aura of Spirit Drinker Other"), // creature-targeted; reaches the requester.
                SpellLayerElement.Cast("Void Magic Mastery Other"),
            ]),
            [Mage] = new SpellLayer(Mage,
            [
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("War Magic Mastery Other"),
                SpellLayerElement.Cast("Aura of Spirit Drinker Other"), // creature-targeted; reaches the requester.
            ]),
            [Tink] = new SpellLayer(Tink,
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Cast("Endurance Other"),
                SpellLayerElement.Cast("Coordination Other"),
                SpellLayerElement.Cast("Focus Other"),
                SpellLayerElement.Cast("Armor Tinkering Expertise Other"),
                SpellLayerElement.Cast("Item Tinkering Expertise Other"),
                SpellLayerElement.Cast("Magic Item Tinkering Expertise Other"),
                SpellLayerElement.Cast("Weapon Tinkering Expertise Other"),
                SpellLayerElement.Layer(XpChain),
            ]),
            [Trades] = new SpellLayer(Trades,
            [
                SpellLayerElement.Cast("Strength Other"),
                SpellLayerElement.Cast("Endurance Other"),
                SpellLayerElement.Cast("Quickness Other"),
                SpellLayerElement.Cast("Coordination Other"),
                SpellLayerElement.Cast("Focus Other"),
                SpellLayerElement.Cast("Willpower Other"),
                SpellLayerElement.Cast("Arcane Enlightenment Other"),
                SpellLayerElement.Cast("Fletching Mastery Other"),
                SpellLayerElement.Cast("Alchemy Mastery Other"),
                SpellLayerElement.Cast("Cooking Mastery Other"),
                SpellLayerElement.Cast("Lockpick Mastery Other"),
                SpellLayerElement.Cast("Sprint Other"),
                SpellLayerElement.Layer(XpChain),
            ]),
            [Dual] = new SpellLayer(Dual,
            [
                SpellLayerElement.Layer(Melee),
                SpellLayerElement.Layer(Generic),
                SpellLayerElement.Cast("Dual Wield Mastery Other"),
                SpellLayerElement.Layer(Banes),
            ]),
            [Banes] = new SpellLayer(Banes,
            [
                SpellLayerElement.Cast("Acid Bane"),
                SpellLayerElement.Cast("Bludgeon Bane"),
                SpellLayerElement.Cast("Blade Bane"),
                SpellLayerElement.Cast("Flame Bane"),
                SpellLayerElement.Cast("Frost Bane"),
                SpellLayerElement.Cast("Lightning Bane"),
                SpellLayerElement.Cast("Piercing Bane"),
                SpellLayerElement.Cast("Impenetrability"),
            ]),
        };

    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Table { get; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [Buff] =
            [
                "Strength Other",
                "Endurance Other",
                "Coordination Other",
                "Quickness Other",
                "Focus Other",
                "Willpower Other",
                "Armor Other",
                "Acid Protection Other",
                "Bludgeoning Protection Other",
                "Blade Protection Other",
                "Fire Protection Other",
                "Cold Protection Other",
                "Lightning Protection Other",
                "Piercing Protection Other",
            ],
            [Self] =
            [
                "Focus Self",
                "Willpower Self",
                "Creature Enchantment Mastery Self",
                "Mana Conversion Mastery Self",
                "Life Magic Mastery Self",
                "Item Enchantment Mastery Self",
                "Mana Renewal Self",
                "Rejuvenation Self",
                "Regeneration Self",
            ],
            [SelfDefence] =
            [
                "Armor Self",
                "Acid Protection Self",
                "Bludgeoning Protection Self",
                "Blade Protection Self",
                "Cold Protection Self",
                "Fire Protection Self",
                "Lightning Protection Self",
                "Piercing Protection Self",
            ],
            [Prots] = Castable(Prots),
            [Heavy] = Castable(Heavy),
            [Light] = Castable(Light),
            [Finesse] = Castable(Finesse),
            [Missile] = Castable(Missile),
            [Void] = Castable(Void),
            [Mage] = Castable(Mage),
            [TwoHanded] = Castable(TwoHanded),
            [Dual] = Castable(Dual),
            [Tink] = Castable(Tink),
            [Trades] = Castable(Trades),
            // Generic, Melee, XpChain and Banes are composition primitives, not profiles a request
            // resolves to directly.
            [ManaUpkeep] =
            [
                "Stamina to Mana Self",
                "Revitalize Self",
            ],
        };
}
