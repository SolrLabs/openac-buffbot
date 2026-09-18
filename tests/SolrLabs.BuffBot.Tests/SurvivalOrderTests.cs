using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests;

public sealed class SurvivalOrderTests
{
    private static readonly string[] UtilityAndTradeLines =
    [
        "Leadership Mastery Other", "Fealty Other", "Fletching Mastery Other", "Alchemy Mastery Other",
        "Cooking Mastery Other", "Lockpick Mastery Other", "Jumping Mastery Other", "Summoning Mastery Other",
        "Sprint Other", "Arcane Enlightenment Other", "Armor Tinkering Expertise Other",
        "Item Tinkering Expertise Other", "Magic Item Tinkering Expertise Other",
        "Weapon Tinkering Expertise Other",
    ];

    private static readonly string[] OffenceLines =
    [
        "Heavy Weapon Mastery Other", "Light Weapon Mastery Other", "Finesse Weapon Mastery Other",
        "Two Handed Combat Mastery Other", "Missile Weapon Mastery Other", "Dual Wield Mastery Other",
        "Shield Mastery Other", "War Magic Mastery Other", "Void Magic Mastery Other",
        "Sneak Attack Mastery Other", "Aura of Heart Seeker Other", "Aura of Blood Drinker Other",
        "Aura of Swift Killer Other", "Aura of Spirit Drinker Other",
    ];

    private static readonly string[] PersonalSkillsLines =
    [
        "Strength Other", "Endurance Other", "Coordination Other", "Quickness Other", "Focus Other",
        "Willpower Other", "Healing Mastery Other", "Creature Enchantment Mastery Other",
        "Item Enchantment Mastery Other", "Life Magic Mastery Other", "Mana Conversion Mastery Other",
        "Aura of Hermetic Link Other",
    ];

    private static readonly string[] SurvivalLines =
    [
        "Impregnability Other", "Invulnerability Other", "Magic Resistance Other", "Armor Other",
        "Acid Protection Other", "Bludgeoning Protection Other", "Blade Protection Other",
        "Fire Protection Other", "Cold Protection Other", "Lightning Protection Other",
        "Piercing Protection Other", "Aura of Defender Other", "Acid Bane", "Bludgeon Bane", "Blade Bane",
        "Flame Bane", "Frost Bane", "Lightning Bane", "Piercing Bane", "Impenetrability",
        "Regeneration Other", "Rejuvenation Other", "Mana Renewal Other",
    ];

    public static IEnumerable<object[]> UtilityAndTradeLineNames() =>
        UtilityAndTradeLines.Select(line => new object[] { line });

    public static IEnumerable<object[]> OffenceLineNames() =>
        OffenceLines.Select(line => new object[] { line });

    public static IEnumerable<object[]> PersonalSkillsLineNames() =>
        PersonalSkillsLines.Select(line => new object[] { line });

    public static IEnumerable<object[]> SurvivalLineNames() =>
        SurvivalLines.Select(line => new object[] { line });

    [Theory]
    [MemberData(nameof(UtilityAndTradeLineNames))]
    public void UtilityAndTradeLinesBandCorrectly(string line) =>
        Assert.Equal(SurvivalOrder.Band.UtilityAndTrade, SurvivalOrder.BandFor(line));

    [Theory]
    [MemberData(nameof(OffenceLineNames))]
    public void OffenceLinesBandCorrectly(string line) =>
        Assert.Equal(SurvivalOrder.Band.Offence, SurvivalOrder.BandFor(line));

    [Theory]
    [MemberData(nameof(PersonalSkillsLineNames))]
    public void PersonalSkillsLinesBandCorrectly(string line) =>
        Assert.Equal(SurvivalOrder.Band.PersonalSkills, SurvivalOrder.BandFor(line));

    [Theory]
    [MemberData(nameof(SurvivalLineNames))]
    public void SurvivalLinesBandCorrectly(string line) =>
        Assert.Equal(SurvivalOrder.Band.Survival, SurvivalOrder.BandFor(line));

    [Fact]
    public void BandsExpireInTheOrderShaneSet()
    {
        // Utility/trade first (the harmless early warning), then offence, then personal skills,
        // then survival last (what keeps the target alive to use everything above).
        Assert.True(SurvivalOrder.Band.UtilityAndTrade < SurvivalOrder.Band.Offence);
        Assert.True(SurvivalOrder.Band.Offence < SurvivalOrder.Band.PersonalSkills);
        Assert.True(SurvivalOrder.Band.PersonalSkills < SurvivalOrder.Band.Survival);
    }

    [Fact]
    public void ApplyKeepsEachBandsOwnRelativeOrder()
    {
        // Interleaved, so an unstable sort cannot pass this by luck.
        IReadOnlyList<ResolvedSpell> plan =
        [
            Resolved("Armor Other"), // Survival
            Resolved("Heavy Weapon Mastery Other"), // Offence
            Resolved("Acid Protection Other"), // Survival
            Resolved("Dual Wield Mastery Other"), // Offence
            Resolved("Cold Protection Other"), // Survival
        ];

        IReadOnlyList<ResolvedSpell> ordered = SurvivalOrder.Apply(plan);

        Assert.Equal(
            [
                "Heavy Weapon Mastery Other", "Dual Wield Mastery Other",
                "Armor Other", "Acid Protection Other", "Cold Protection Other",
            ],
            ordered.Select(resolved => resolved.Line));
    }

    [Fact]
    public void UnknownLineDefaultsToPersonalSkillsAndTracesOnceNotPerOccurrence()
    {
        var traced = new List<string>();
        IReadOnlyList<ResolvedSpell> plan =
        [
            Resolved("Something Nobody Wrote Other"),
            Resolved("Something Nobody Wrote Other"), // repeated: still traced only once
            Resolved("Strength Other"),
        ];

        IReadOnlyList<ResolvedSpell> ordered = SurvivalOrder.Apply(plan, traced.Add);

        Assert.Equal(
            SurvivalOrder.Band.PersonalSkills, SurvivalOrder.BandFor("Something Nobody Wrote Other"));
        Assert.Single(traced);
        Assert.Contains("Something Nobody Wrote Other", traced[0]);
        // Both unknown lines land in the same band as "Strength Other", so all three keep their
        // original order.
        Assert.Equal(
            ["Something Nobody Wrote Other", "Something Nobody Wrote Other", "Strength Other"],
            ordered.Select(resolved => resolved.Line));
    }

    [Fact]
    public void ACallerWithNoTraceInterestGetsNoCallbackAndNoThrow()
    {
        IReadOnlyList<ResolvedSpell> plan = [Resolved("Not In The Table")];
        IReadOnlyList<ResolvedSpell> ordered = SurvivalOrder.Apply(plan);
        Assert.Single(ordered);
    }

    [Fact]
    public void AnEmptyPlanStaysEmpty()
    {
        Assert.Empty(SurvivalOrder.Apply(Array.Empty<ResolvedSpell>()));
    }

    [Theory]
    [InlineData(DefaultSpellSets.Heavy)]
    [InlineData(DefaultSpellSets.Missile)]
    public void OrderingNeverChangesWhichLinesAreInTheChain(string profileName)
    {
        IReadOnlyList<string> lines = DefaultSpellSets.Table[profileName];
        IReadOnlyList<ResolvedSpell> plan = lines.Select(Resolved).ToList();

        IReadOnlyList<ResolvedSpell> ordered = SurvivalOrder.Apply(plan);

        Assert.Equal(ordered.Count, lines.Count);
        Assert.Equal(
            lines.OrderBy(line => line, StringComparer.OrdinalIgnoreCase),
            ordered.Select(resolved => resolved.Line).OrderBy(line => line, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void HeavyChainOrdersUtilityThenOffenceThenPersonalThenSurvival()
    {
        IReadOnlyList<string> lines = DefaultSpellSets.Table[DefaultSpellSets.Heavy];
        IReadOnlyList<ResolvedSpell> plan = lines.Select(Resolved).ToList();

        IReadOnlyList<ResolvedSpell> ordered = SurvivalOrder.Apply(plan);

        Assert.Equal(
            [
                // Utility and trade (expires first).
                "Arcane Enlightenment Other", "Lockpick Mastery Other", "Summoning Mastery Other",
                "Jumping Mastery Other", "Sprint Other", "Leadership Mastery Other", "Fealty Other",
                // Offence.
                "Aura of Heart Seeker Other", "Aura of Blood Drinker Other", "Aura of Swift Killer Other",
                "Heavy Weapon Mastery Other", "Dual Wield Mastery Other", "Shield Mastery Other",
                // Personal skills.
                "Strength Other", "Endurance Other", "Quickness Other", "Coordination Other",
                "Focus Other", "Willpower Other", "Creature Enchantment Mastery Other",
                "Item Enchantment Mastery Other", "Life Magic Mastery Other", "Mana Conversion Mastery Other",
                "Healing Mastery Other", "Aura of Hermetic Link Other",
                // Survival (expires last).
                "Regeneration Other", "Rejuvenation Other", "Mana Renewal Other", "Impregnability Other",
                "Invulnerability Other", "Magic Resistance Other", "Armor Other", "Acid Protection Other",
                "Bludgeoning Protection Other", "Blade Protection Other", "Fire Protection Other",
                "Cold Protection Other", "Lightning Protection Other", "Piercing Protection Other",
                "Aura of Defender Other",
            ],
            ordered.Select(resolved => resolved.Line));
    }

    [Fact]
    public void MissileChainOrdersUtilityThenOffenceThenPersonalThenSurvival()
    {
        IReadOnlyList<string> lines = DefaultSpellSets.Table[DefaultSpellSets.Missile];
        IReadOnlyList<ResolvedSpell> plan = lines.Select(Resolved).ToList();

        IReadOnlyList<ResolvedSpell> ordered = SurvivalOrder.Apply(plan);

        Assert.Equal(
            [
                // Utility and trade.
                "Arcane Enlightenment Other", "Lockpick Mastery Other", "Summoning Mastery Other",
                "Jumping Mastery Other", "Sprint Other", "Leadership Mastery Other", "Fealty Other",
                "Fletching Mastery Other", "Alchemy Mastery Other",
                // Offence.
                "Aura of Heart Seeker Other", "Aura of Blood Drinker Other", "Aura of Swift Killer Other",
                "Missile Weapon Mastery Other",
                // Personal skills.
                "Strength Other", "Endurance Other", "Quickness Other", "Coordination Other",
                "Focus Other", "Willpower Other", "Creature Enchantment Mastery Other",
                "Item Enchantment Mastery Other", "Life Magic Mastery Other", "Mana Conversion Mastery Other",
                "Healing Mastery Other", "Aura of Hermetic Link Other",
                // Survival.
                "Regeneration Other", "Rejuvenation Other", "Mana Renewal Other", "Impregnability Other",
                "Invulnerability Other", "Magic Resistance Other", "Armor Other", "Acid Protection Other",
                "Bludgeoning Protection Other", "Blade Protection Other", "Fire Protection Other",
                "Cold Protection Other", "Lightning Protection Other", "Piercing Protection Other",
                "Aura of Defender Other",
            ],
            ordered.Select(resolved => resolved.Line));
    }

    private static ResolvedSpell Resolved(string line) =>
        new(line, new PluginSpellInfo(
            SpellId: 1,
            Name: $"{line} I",
            Family: 1,
            Tier: 1,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 120f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: false,
            IsBeneficial: true));
}
