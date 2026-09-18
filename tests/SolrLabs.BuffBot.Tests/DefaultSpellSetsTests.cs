using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

/// <summary>Composition order, dedup across layers, and that every profile resolves
/// against the catalog — except the blocked lines <see cref="NamesAStillBlockedLine"/> names.</summary>
public sealed class DefaultSpellSetsTests
{
    /// <summary>Every keyword profile a request can resolve to today.</summary>
    private static readonly string[] ArchetypeProfiles =
    [
        DefaultSpellSets.Prots,
        DefaultSpellSets.Heavy,
        DefaultSpellSets.Light,
        DefaultSpellSets.Finesse,
        DefaultSpellSets.Missile,
        DefaultSpellSets.Void,
        DefaultSpellSets.Mage,
        DefaultSpellSets.TwoHanded,
        DefaultSpellSets.Dual,
        DefaultSpellSets.Tink,
        DefaultSpellSets.Trades,
    ];

    /// <summary>Every archetype except <see cref="DefaultSpellSets.Prots"/>, <see
    /// cref="DefaultSpellSets.Tink"/> and <see cref="DefaultSpellSets.Trades"/> (narrower, self-contained profiles).</summary>
    private static readonly string[] GenericBasedProfiles =
    [
        DefaultSpellSets.Heavy,
        DefaultSpellSets.Light,
        DefaultSpellSets.Finesse,
        DefaultSpellSets.Missile,
        DefaultSpellSets.Void,
        DefaultSpellSets.Mage,
        DefaultSpellSets.TwoHanded,
        DefaultSpellSets.Dual,
    ];

    public static IEnumerable<object[]> ArchetypeProfileNames() =>
        ArchetypeProfiles.Select(name => new object[] { name });

    public static IEnumerable<object[]> GenericBasedProfileNames() =>
        GenericBasedProfiles.Select(name => new object[] { name });

    /// <summary>A "... Bane" line (castable only at the requester's wielded shield) or
    /// "Impenetrability" (enchants worn armor, invisible to another client's <c>CaptureObjects()</c>).</summary>
    private static bool NamesAStillBlockedLine(string line) =>
        line.EndsWith(" Bane", StringComparison.OrdinalIgnoreCase)
        || string.Equals(line, "Impenetrability", StringComparison.OrdinalIgnoreCase);

    /// <summary>The castable projection of <see cref="DefaultSpellSets.Generic"/>, with every
    /// still-blocked line removed — the shared base every <see cref="GenericBasedProfiles"/> leads with.</summary>
    private static IReadOnlyList<string> GenericCastable() =>
        SpellProfileResolver.Resolve(DefaultSpellSets.Layers, DefaultSpellSets.Generic)
            .Where(line => !NamesAStillBlockedLine(line))
            .ToList();

    /// <summary>Melee's three auras are creature-targeted and reach Table like any other
    /// line, so unlike <see cref="GenericCastable"/> nothing here needs removing.</summary>
    private static IReadOnlyList<string> MeleeCastable() =>
        SpellProfileResolver.Resolve(DefaultSpellSets.Layers, DefaultSpellSets.Melee);

    [Theory]
    [MemberData(nameof(ArchetypeProfileNames))]
    public void EveryArchetypeProfileIsNonEmpty(string profileName)
    {
        Assert.NotEmpty(DefaultSpellSets.Table[profileName]);
    }

    [Theory]
    [MemberData(nameof(ArchetypeProfileNames))]
    public void EveryArchetypeProfileResolvesAgainstACatalogThatKnowsEveryLine(string profileName)
    {
        IReadOnlyList<string> lines = DefaultSpellSets.Table[profileName];
        List<PluginSpellInfo> catalog = CatalogKnowingEveryLineInLayers();

        SpellSelectionResult result = SpellSelector.Resolve(catalog, lines);

        Assert.True(result.IsSuccess, result.Failure?.ToString());
        Assert.Equal(lines, result.Plan.Select(resolved => resolved.Line));
    }

    [Fact]
    public void GenericIsNotItselfAReachableProfile()
    {
        // "_generic" is a composition primitive, not something a player ever asks the bot for
        // directly — every weapon/magic style profile builds on it instead.
        Assert.False(DefaultSpellSets.Table.ContainsKey(DefaultSpellSets.Generic));
    }

    [Fact]
    public void MeleeIsNotItselfAReachableProfile()
    {
        // "_melee" is a composition primitive for weapon-style profiles only; nobody asks the bot
        // for it by name either.
        Assert.False(DefaultSpellSets.Table.ContainsKey(DefaultSpellSets.Melee));
    }

    [Fact]
    public void XpChainIsNotItselfAReachableProfile()
    {
        // "xpchain" is a layer other profiles include (Generic, Tink, Trades), not a profile the
        // research gives its own player-facing keyword.
        Assert.False(DefaultSpellSets.Table.ContainsKey(DefaultSpellSets.XpChain));
    }

    [Fact]
    public void BanesIsNeverAReachableProfile()
    {
        // The seven bane lines it names are reachable instead through BaneLinesFor
        // and the requester's own wielded shield.
        Assert.False(DefaultSpellSets.Table.ContainsKey(DefaultSpellSets.Banes));
    }

    [Fact]
    public void NoCastableProfileEverResolvesToABlockedLine()
    {
        foreach (KeyValuePair<string, IReadOnlyList<string>> profile in DefaultSpellSets.Table)
        {
            foreach (string line in profile.Value)
            {
                Assert.False(
                    NamesAStillBlockedLine(line),
                    $"{profile.Key} resolved to \"{line}\", which unit 8b says still can't reach Table directly.");
            }
        }
    }

    [Fact]
    public void AllItemTargetedLinesIsEveryBlockedLineInTheProfileGraphDeduplicated()
    {
        // Every blocked line the profile graph nests, gathered with no duplicate even
        // though several profiles nest the same bane; an aura is never part of it.
        IReadOnlyList<string> lines = DefaultSpellSets.AllItemTargetedLines();

        Assert.NotEmpty(lines);
        foreach (string line in lines)
            Assert.True(NamesAStillBlockedLine(line), $"\"{line}\" is not shaped like a still-blocked line.");
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Contains("Acid Bane", lines);
        Assert.Contains("Impenetrability", lines);
        Assert.DoesNotContain("Aura of Defender Other", lines);
    }

    [Theory]
    [InlineData(DefaultSpellSets.Heavy)]
    [InlineData(DefaultSpellSets.Light)]
    [InlineData(DefaultSpellSets.Finesse)]
    [InlineData(DefaultSpellSets.TwoHanded)]
    [InlineData(DefaultSpellSets.Dual)]
    public void HeavyLightFinesseTwoHandedAndDualNestBanesInTheRawLayerGraph(string profileName)
    {
        // Checked against the raw layer graph, not Table, since Table filters every
        // bane line out of every profile equally — the divergence lives in the composition.
        IReadOnlyList<string> raw = SpellProfileResolver.Resolve(DefaultSpellSets.Layers, profileName);
        IReadOnlyList<string> baneLines = SpellProfileResolver.Resolve(
            DefaultSpellSets.Layers, DefaultSpellSets.Banes);
        Assert.NotEmpty(baneLines); // sanity: the layer itself is not accidentally empty.

        foreach (string baneLine in baneLines)
            Assert.Contains(baneLine, raw);

        // BaneLinesFor reads this same graph, minus Impenetrability (it stays permanently
        // blocked); it should agree exactly on the seven true bane lines.
        Assert.Equal(
            baneLines.Where(line => !string.Equals(line, "Impenetrability", StringComparison.OrdinalIgnoreCase)),
            DefaultSpellSets.BaneLinesFor(profileName));
    }

    [Theory]
    [InlineData(DefaultSpellSets.Missile)]
    [InlineData(DefaultSpellSets.Void)]
    [InlineData(DefaultSpellSets.Mage)]
    [InlineData(DefaultSpellSets.Tink)]
    [InlineData(DefaultSpellSets.Trades)]
    public void EveryOtherArchetypeNeverNestsBanesInTheRawLayerGraph(string profileName)
    {
        IReadOnlyList<string> raw = SpellProfileResolver.Resolve(DefaultSpellSets.Layers, profileName);
        IReadOnlyList<string> baneLines = SpellProfileResolver.Resolve(
            DefaultSpellSets.Layers, DefaultSpellSets.Banes);

        foreach (string baneLine in baneLines)
            Assert.DoesNotContain(baneLine, raw);
        Assert.Empty(DefaultSpellSets.BaneLinesFor(profileName));
    }

    [Fact]
    public void BaneLinesForAnUnknownProfileNameIsEmpty()
    {
        Assert.Empty(DefaultSpellSets.BaneLinesFor("not-a-real-profile"));
    }

    [Fact]
    public void BaneLinesForNeverIncludesImpenetrability()
    {
        // Impenetrability lives in the same Banes layer but enchants worn armor, not a wielded
        // shield (Part C) — permanently unreachable, unlike the seven true bane lines.
        foreach (string profileName in new[]
                 {
                     DefaultSpellSets.Heavy, DefaultSpellSets.Light, DefaultSpellSets.Finesse,
                     DefaultSpellSets.TwoHanded, DefaultSpellSets.Dual,
                 })
            Assert.DoesNotContain("Impenetrability", DefaultSpellSets.BaneLinesFor(profileName));
    }

    [Theory]
    [MemberData(nameof(GenericBasedProfileNames))]
    [InlineData(DefaultSpellSets.Trades)]
    public void ProfilesBuiltOnSixAttributesLeadWithThemInOrder(string profileName)
    {
        // Heavy/Light/Finesse/2h lead with Melee's three auras ahead of the attributes,
        // so these are the first six non-aura lines rather than literally the first six.
        IReadOnlyList<string> lines = DefaultSpellSets.Table[profileName]
            .Where(line => !line.StartsWith("Aura of ", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(
            [
                "Strength Other",
                "Endurance Other",
                "Quickness Other",
                "Coordination Other",
                "Focus Other",
                "Willpower Other",
            ],
            lines.Take(6));
    }

    [Fact]
    public void TinkLeadsWithOnlyFourAttributesNotSix()
    {
        // Tink casts Strength, Endurance, Coordination and Focus, never Quickness or Willpower —
        // reproduced exactly rather than "completed" to match every other profile.
        IReadOnlyList<string> lines = DefaultSpellSets.Table[DefaultSpellSets.Tink];
        Assert.Equal(
            ["Strength Other", "Endurance Other", "Coordination Other", "Focus Other"],
            lines.Take(4));
        Assert.DoesNotContain("Quickness Other", lines);
        Assert.DoesNotContain("Willpower Other", lines);
    }

    [Fact]
    public void ProtsIsProtectionsOnlyPlusAuraOfDefenderWithNoArmorAndNoAttributes()
    {
        // Armor Other is not here: prots never casts Armor — it is cast once, in _generic.
        IReadOnlyList<string> lines = DefaultSpellSets.Table[DefaultSpellSets.Prots];

        Assert.Equal(
            [
                "Acid Protection Other",
                "Bludgeoning Protection Other",
                "Blade Protection Other",
                "Fire Protection Other",
                "Cold Protection Other",
                "Lightning Protection Other",
                "Piercing Protection Other",
                "Aura of Defender Other",
            ],
            lines);
    }

    [Theory]
    [MemberData(nameof(GenericBasedProfileNames))]
    public void ProtectionsAppearExactlyOnceEvenThoughGenericNestsThemAndTheProfileDoesToo(
        string profileName)
    {
        IReadOnlyList<string> lines = DefaultSpellSets.Table[profileName];
        foreach (string protectionLine in DefaultSpellSets.Table[DefaultSpellSets.Prots])
            Assert.Single(lines, line => line == protectionLine);
    }

    [Fact]
    public void HeavyLeadsWithMeleeAurasThenTheGenericBaseThenItsOwnThreeMasteryLines()
    {
        // Melee nests first in the layer's own composition order: its three auras are
        // creature-targeted and reach this list, unlike a bane.
        IReadOnlyList<string> expected =
        [
            .. MeleeCastable(),
            .. GenericCastable(),
            "Heavy Weapon Mastery Other",
            "Dual Wield Mastery Other",
            "Shield Mastery Other",
        ];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.Heavy]);
    }

    [Fact]
    public void LightLeadsWithMeleeAurasThenTheGenericBaseThenItsOwnThreeMasteryLines()
    {
        IReadOnlyList<string> expected =
        [
            .. MeleeCastable(),
            .. GenericCastable(),
            "Light Weapon Mastery Other",
            "Dual Wield Mastery Other",
            "Shield Mastery Other",
        ];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.Light]);
    }

    [Fact]
    public void FinesseLeadsWithMeleeAurasThenTheGenericBaseThenItsOwnThreeMasteryLines()
    {
        IReadOnlyList<string> expected =
        [
            .. MeleeCastable(),
            .. GenericCastable(),
            "Finesse Weapon Mastery Other",
            "Dual Wield Mastery Other",
            "Shield Mastery Other",
        ];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.Finesse]);
    }

    [Fact]
    public void TwoHandedLeadsWithMeleeAurasThenTheGenericBaseThenOnlyItsOwnMasteryLine()
    {
        IReadOnlyList<string> expected =
            [.. MeleeCastable(), .. GenericCastable(), "Two Handed Combat Mastery Other"];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.TwoHanded]);
    }

    [Fact]
    public void MissileAddsFletchingAlchemyThreeAurasThenItsOwnMasteryAfterTheGenericBase()
    {
        // Unlike Melee's three auras, missile's own three are cast directly by this
        // layer, after Fletching/Alchemy, reaching this castable list in that position.
        IReadOnlyList<string> expected =
        [
            .. GenericCastable(),
            "Fletching Mastery Other",
            "Alchemy Mastery Other",
            "Aura of Heart Seeker Other",
            "Aura of Blood Drinker Other",
            "Aura of Swift Killer Other",
            "Missile Weapon Mastery Other",
        ];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.Missile]);
    }

    [Fact]
    public void VoidAddsSneakAttackAuraOfSpiritDrinkerThenVoidMagicMasteryAfterTheGenericBase()
    {
        // Aura of Spirit Drinker, also cast directly by this layer, is creature-targeted and
        // reaches this castable list between the two masteries, exactly where the layer casts it.
        IReadOnlyList<string> expected =
        [
            .. GenericCastable(),
            "Sneak Attack Mastery Other",
            "Aura of Spirit Drinker Other",
            "Void Magic Mastery Other",
        ];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.Void]);
    }

    [Fact]
    public void MageAddsWarMagicMasteryThenAuraOfSpiritDrinkerAfterTheGenericBase()
    {
        // Aura of Spirit Drinker, also cast directly by this layer (shared with void), is
        // creature-targeted and reaches this castable list trailing War Magic Mastery.
        IReadOnlyList<string> expected =
            [.. GenericCastable(), "War Magic Mastery Other", "Aura of Spirit Drinker Other"];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.Mage]);
    }

    [Fact]
    public void DualStandsOnItsOwnLikeTheMeleeStyleProfiles()
    {
        // Not part of the source profile graph; dual is completed to the same shape minus
        // the weapon-type and shield masteries a dual-wielder does not use.
        IReadOnlyList<string> expected =
            [.. MeleeCastable(), .. GenericCastable(), "Dual Wield Mastery Other"];

        Assert.Equal(expected, DefaultSpellSets.Table[DefaultSpellSets.Dual]);
    }

    [Fact]
    public void TinkCastsFourAttributesAllFourTinkeringExpertisesThenXpChain()
    {
        Assert.Equal(
            [
                "Strength Other",
                "Endurance Other",
                "Coordination Other",
                "Focus Other",
                "Armor Tinkering Expertise Other",
                "Item Tinkering Expertise Other",
                "Magic Item Tinkering Expertise Other",
                "Weapon Tinkering Expertise Other",
                "Leadership Mastery Other",
                "Fealty Other",
            ],
            DefaultSpellSets.Table[DefaultSpellSets.Tink]);
    }

    [Fact]
    public void TradesCastsSixAttributesArcaneEnlightenmentFourTradeMasteriesSprintThenXpChain()
    {
        Assert.Equal(
            [
                "Strength Other",
                "Endurance Other",
                "Quickness Other",
                "Coordination Other",
                "Focus Other",
                "Willpower Other",
                "Arcane Enlightenment Other",
                "Fletching Mastery Other",
                "Alchemy Mastery Other",
                "Cooking Mastery Other",
                "Lockpick Mastery Other",
                "Sprint Other",
                "Leadership Mastery Other",
                "Fealty Other",
            ],
            DefaultSpellSets.Table[DefaultSpellSets.Trades]);
    }

    [Fact]
    public void TheLegacyBuffAndSelfAndManaUpkeepEntriesAreUnchanged()
    {
        // The existing contract other components already consume (Responder, BuffCoordinator,
        // CastStateMachine) must not move under them. Untouched by the profile-fidelity pass.
        Assert.Equal(14, DefaultSpellSets.Table[DefaultSpellSets.Buff].Count);
        Assert.Equal(
            ["Stamina to Mana Self", "Revitalize Self"],
            DefaultSpellSets.Table[DefaultSpellSets.ManaUpkeep]);
    }

    [Fact]
    public void TheCasterOnlyBuffsItselfWithWhatMakesItACaster()
    {
        // Nothing in a town hits the bot, and these eight lines were nearly half of
        // every upkeep cycle's mana and cast time, paid again each time they expired.
        IReadOnlyList<string> self = DefaultSpellSets.Table[DefaultSpellSets.Self];

        Assert.DoesNotContain("Armor Self", self);
        Assert.DoesNotContain(self, line => line.EndsWith("Protection Self", StringComparison.Ordinal));

        // What is left is the case for keeping any of it: skill and mana to cast with, and the
        // three regeneration lines the mana bounce feeds on.
        foreach (string kept in new[]
                 {
                     "Focus Self", "Willpower Self",
                     "Creature Enchantment Mastery Self", "Mana Conversion Mastery Self",
                     "Life Magic Mastery Self", "Item Enchantment Mastery Self",
                     "Mana Renewal Self", "Rejuvenation Self", "Regeneration Self",
                 })
            Assert.Contains(kept, self);
        Assert.Equal(9, self.Count);
    }

    [Fact]
    public void TheDefensiveSelfLinesAreKeptAsideRatherThanDeleted()
    {
        // A bot parked somewhere that can actually hurt it wants these back, and restoring them
        // should be one edit rather than eight remembered spell names. Nothing casts it today.
        IReadOnlyList<string> defence = DefaultSpellSets.Table[DefaultSpellSets.SelfDefence];

        Assert.Equal(8, defence.Count);
        Assert.Contains("Armor Self", defence);

        // Reachable by set name but unreachable by a player: no phrase resolves to it.
        Assert.DoesNotContain("self-defence", DefaultVocabulary.Table.Phrases);
    }

    /// <summary>A synthetic tier-I spell for every line any layer contributes — enough for
    /// <see cref="SpellSelector.Resolve"/> to succeed on every archetype profile.</summary>
    private static List<PluginSpellInfo> CatalogKnowingEveryLineInLayers()
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string layerName in DefaultSpellSets.Layers.Keys)
        {
            foreach (string line in SpellProfileResolver.Resolve(DefaultSpellSets.Layers, layerName))
            {
                if (seen.Add(line))
                    lines.Add(line);
            }
        }

        var catalog = new List<PluginSpellInfo>(lines.Count);
        for (int i = 0; i < lines.Count; i++)
        {
            catalog.Add(new PluginSpellInfo(
                SpellId: (uint)(i + 1),
                Name: $"{lines[i]} I",
                Family: (uint)(1000 + i),
                Tier: 1,
                Difficulty: 0,
                ManaCost: 0,
                DurationSeconds: 120f,
                School: 0,
                Description: string.Empty,
                IsSelfTargeted: false,
                IsBeneficial: true));
        }

        return catalog;
    }
}
