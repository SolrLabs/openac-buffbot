using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary>Host-free coverage of <see cref="ComponentReportBuilder"/>: the join between the
/// resolved profile's own components and what the item surface reports owning.</summary>
public sealed class ComponentReportBuilderTests
{
    // Component id 10 (Lead Scarab, weenie 691) is shared by two distinct learned spells:
    // "Strength Other" (Buff) and "Endurance Other" (also Buff) — so its usedBy is 2, not 1.
    private const uint StrengthOtherId = 101;
    private const uint EnduranceOtherId = 102;

    // Component id 20 (Pyreal Scarab, weenie 690) — learned, needed, but none owned: stock 0.
    private const uint FocusOtherId = 103;

    // Component id 30 (Prismatic Taper, weenie 20631) — the mana-upkeep pair's own line, self-targeted.
    private const uint StaminaToManaSelfId = 104;

    // Item-targeted, so DefaultSpellSets.Table never carries it — learning it must not
    // surface its component even though the catalog knows it perfectly well.
    private const uint AuraOfHeartSeekerOtherId = 105;

    private static readonly List<PluginSpellInfo> FullCatalog =
    [
        Spell(StrengthOtherId, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [10]),
        Spell(EnduranceOtherId, "Endurance Other I", isSelfTargeted: false, formulaComponentIds: [10]),
        Spell(FocusOtherId, "Focus Other I", isSelfTargeted: false, componentSet: new PluginSpellComponentSet(Herb: 20, Powder: 0, Potion: 0, Talisman: 0)),
        Spell(StaminaToManaSelfId, "Stamina to Mana Self I", isSelfTargeted: true, formulaComponentIds: [30]),
        Spell(AuraOfHeartSeekerOtherId, "Aura of Heart Seeker Other I", isSelfTargeted: false, formulaComponentIds: [999]),
    ];

    private static PluginSpellInfo Spell(
        uint spellId, string name, bool isSelfTargeted,
        IReadOnlyList<uint>? formulaComponentIds = null, PluginSpellComponentSet componentSet = default) =>
        new(
            SpellId: spellId,
            Name: name,
            Family: spellId,
            Tier: 1,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 120f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: isSelfTargeted,
            IsBeneficial: true)
        {
            FormulaComponentIds = formulaComponentIds ?? Array.Empty<uint>(),
            ComponentSet = componentSet,
        };

    private static PluginInventoryItem Stack(uint objectId, uint weenieClassId, int stackSize) =>
        new(
            ObjectId: objectId,
            WeenieClassId: weenieClassId,
            Name: "reagent",
            ItemType: 0,
            ContainerObjectId: 0,
            WielderObjectId: 0,
            ValidLocations: 0,
            EquippedLocation: 0,
            Useability: 0,
            TargetType: 0,
            PublicFlags: 0,
            StackSize: stackSize,
            Structure: 1,
            MaximumStructure: 1,
            SpellId: 0,
            PetClass: 0,
            SummoningMastery: 0,
            ProcSpellId: 0,
            ProcSpellSelfTargeted: false,
            ProcSpellRate: 0,
            WeaponSkill: 0,
            DamageType: 0,
            Damage: 0,
            DamageVariance: 0,
            UseRequiresSkill: 0,
            UseRequiresSkillLevel: 0,
            UseRequiresSkillSpecialized: 0);

    private sealed class FakeCatalog : ISpellCatalog
    {
        internal FakeCatalog(IReadOnlyList<PluginSpellInfo> knownSelfBuffs) => KnownSelfBuffs = knownSelfBuffs;

        public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; }

        private static readonly Dictionary<uint, PluginSpellComponentInfo> Components = new()
        {
            [10] = new PluginSpellComponentInfo(10, 691, "Lead Scarab", 0, 0, 0, 0, 0, "", ""),
            [20] = new PluginSpellComponentInfo(20, 690, "Pyreal Scarab", 0, 0, 0, 0, 0, "", ""),
            [30] = new PluginSpellComponentInfo(30, 20631, "Prismatic Taper", 0, 0, 0, 0, 0, "", ""),
            // Deliberately present: the exclusion under test is Table never naming that
            // line, not a missing lookup entry.
            [999] = new PluginSpellComponentInfo(999, 8897, "Platinum Scarab", 0, 0, 0, 0, 0, "", ""),
            // A Talisman resolves from the catalog like a real reagent, but the
            // "components" panel must never name it.
            [40] = new PluginSpellComponentInfo(40, 9000, "Ivory Talisman", 0, 0, 0, 0, 0, "", ""),
            // Real retail ids, unlike every synthetic id above: 1 is Lead's, 0xBC the
            // Prismatic Taper's — the two EffectiveFormula.ScarabOnlyFormula deals in.
            [1u] = new PluginSpellComponentInfo(1u, 691, "Lead Scarab", 0, 0, 0, 0, 0, "", ""),
            [0xBCu] = new PluginSpellComponentInfo(0xBCu, 20631, "Prismatic Taper", 0, 0, 0, 0, 0, "", ""),
        };

        public bool TryGet(uint spellId, out PluginSpellInfo info)
        {
            foreach (PluginSpellInfo spell in KnownSelfBuffs)
            {
                if (spell.SpellId != spellId)
                    continue;
                info = spell;
                return true;
            }
            info = default;
            return false;
        }

        public bool TryGetComponent(uint componentId, out PluginSpellComponentInfo info) =>
            Components.TryGetValue(componentId, out info);
    }

    private sealed class FakeItems : IItemAutomation
    {
        private readonly List<PluginInventoryItem> _items = [];

        internal bool Available { get; set; } = true;

        bool IItemAutomation.IsAvailable => Available;

        internal void Add(PluginInventoryItem item) => _items.Add(item);

        public IReadOnlyList<PluginInventoryItem> CaptureOwnedItems() => _items;
    }

    [Fact]
    public void JoinsSharedComponentsAndSumsStacksLowestStockFirst()
    {
        var catalog = new FakeCatalog(FullCatalog);
        var items = new FakeItems();
        items.Add(Stack(1, weenieClassId: 691, stackSize: 15)); // Lead Scarab
        items.Add(Stack(2, weenieClassId: 691, stackSize: 9)); // two stacks of the same reagent.
        items.Add(Stack(3, weenieClassId: 20631, stackSize: 100)); // Prismatic Taper
        // Nothing owned of weenie 690 (Pyreal Scarab) at all — stock 0, not absent.

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        Assert.Equal(4, report.Items.Count);

        // Lowest stock first, then by name.
        Assert.Equal(8897u, report.Items[0].WeenieClassId);
        Assert.Equal("Platinum Scarab", report.Items[0].Name);
        Assert.Equal(0, report.Items[0].Stock);
        Assert.Equal(1, report.Items[0].UsedBy);

        // Aura of Heart Seeker Other's component (weenie 8897, "Platinum Scarab"): auras target
        // the creature and reach a requester, so the bot needs its component too.
        Assert.Equal(690u, report.Items[1].WeenieClassId);
        Assert.Equal(0, report.Items[1].Stock);

        Assert.Equal(691u, report.Items[2].WeenieClassId);
        Assert.Equal(24, report.Items[2].Stock); // 15 + 9, summed across two stacks.
        Assert.Equal(2, report.Items[2].UsedBy); // Strength Other and Endurance Other both need it.

        Assert.Equal(20631u, report.Items[3].WeenieClassId);
        Assert.Equal(100, report.Items[3].Stock);
        Assert.Equal(1, report.Items[3].UsedBy);
    }

    [Fact]
    public void ATalismanResolvesFromTheCatalogButIsExcludedFromTheReport()
    {
        var catalog = new FakeCatalog(
        [
            Spell(201, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [10]), // Lead Scarab
            Spell(203, "Focus Other I", isSelfTargeted: false, formulaComponentIds: [40]), // Ivory Talisman
        ]);
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        Assert.True(report.CatalogAvailable);
        ComponentUsage row = Assert.Single(report.Items);
        Assert.Equal(691u, row.WeenieClassId);
    }

    [Fact]
    public void UnavailableWhenInventoryCannotBeRead()
    {
        var catalog = new FakeCatalog(FullCatalog);
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: false);

        Assert.False(report.Available);
        Assert.Empty(report.Items);
    }

    /// <summary>The headless host: item use is unbound, so IItemAutomation.IsAvailable is false,
    /// but inventory still reads. Stock must still show there.</summary>
    [Fact]
    public void StillAvailableWhenOnlyItemUseIsUnbound()
    {
        var catalog = new FakeCatalog(FullCatalog);
        var items = new FakeItems { Available = false };

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
    }

    [Fact]
    public void EmptyIsARealAnswerNotUnavailableWhenNothingIsLearned()
    {
        var catalog = new FakeCatalog(Array.Empty<PluginSpellInfo>());
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        Assert.Empty(report.Items);
    }

    /// <summary>One component id among several with no catalog entry is still skipped rather
    /// than guessed at — the other ids that do resolve still produce rows.</summary>
    [Fact]
    public void AComponentIdWithNoCatalogEntryIsSkippedRatherThanGuessed()
    {
        var catalog = new FakeCatalog(
        [
            Spell(201, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [7777]),
            Spell(202, "Endurance Other I", isSelfTargeted: false, formulaComponentIds: [10]),
        ]);
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        Assert.True(report.CatalogAvailable);
        Assert.Single(report.Items);
        Assert.Equal(691u, report.Items[0].WeenieClassId);
    }

    /// <summary>A headless host, which never binds a magic catalog, must read as a distinct
    /// "can't say" rather than <see cref="ComponentReport.Empty"/>'s genuine "nothing needed".</summary>
    [Fact]
    public void CatalogUnavailableWhenComponentIdsExistButNoneResolve()
    {
        var catalog = new FakeCatalog([Spell(201, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [7777])]);
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        Assert.False(report.CatalogAvailable);
        Assert.Empty(report.Items);
    }

    [Fact]
    public void EmptyIsCatalogAvailableSinceThereIsNothingForTheCatalogToMiss()
    {
        var catalog = new FakeCatalog(Array.Empty<PluginSpellInfo>());
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        Assert.True(report.CatalogAvailable);
        Assert.Empty(report.Items);
    }

    // -- every held reagent shows, not only what a resolved spell needs -------------------------

    /// <summary>A Pea is never a spell's own formula component, but the console panel
    /// must show it at 0 UsedBy rather than leaving it off entirely.</summary>
    [Fact]
    public void AHeldPeaShowsEvenThoughNoSpellEverUsesOne()
    {
        var catalog = new FakeCatalog(FullCatalog);
        var items = new FakeItems();
        items.Add(Stack(1, weenieClassId: 691, stackSize: 15)); // Lead Scarab (used)
        items.Add(Stack(2, weenieClassId: 20631, stackSize: 100)); // Prismatic Taper (used)
        items.Add(PeaStack(3, "Lead Pea", stackSize: 40)); // held, never a spell's own reagent

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        ComponentUsage pea = Assert.Single(report.Items, row => row.Name == "Lead Pea");
        Assert.Equal(40, pea.Stock);
        Assert.Equal(0, pea.UsedBy);
    }

    /// <summary>An untrained tier's own scarab colour — held, but nothing the bot has resolved
    /// needs it right now — still gets a row rather than being silently dropped.</summary>
    [Fact]
    public void AHeldReagentNoResolvedSpellNeedsStillShows()
    {
        var catalog = new FakeCatalog(FullCatalog);
        var items = new FakeItems();
        items.Add(Stack(1, weenieClassId: 8897, stackSize: 12)); // Platinum Scarab, also "used" here (id 999)

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        ComponentUsage platinum = Assert.Single(report.Items, row => row.WeenieClassId == 8897u);
        Assert.Equal(12, platinum.Stock);
        Assert.Equal(1, platinum.UsedBy); // still counted as used (Aura of Heart Seeker Other)
    }

    /// <summary>A held item that is not a reagent at all — a Talisman, say — never becomes an
    /// extra row just because the bot happens to be carrying one.</summary>
    [Fact]
    public void AHeldNonReagentItemNeverBecomesARow()
    {
        var catalog = new FakeCatalog(
        [
            Spell(201, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [10]), // Lead Scarab
        ]);
        var items = new FakeItems();
        items.Add(Stack(1, weenieClassId: 691, stackSize: 5)); // Lead Scarab
        items.Add(TalismanStack(2, "Ivory Talisman", stackSize: 1));

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.DoesNotContain(report.Items, row => row.Name == "Ivory Talisman");
        ComponentUsage row = Assert.Single(report.Items);
        Assert.Equal(691u, row.WeenieClassId);
    }

    /// <summary>A held reagent must still surface even when nothing is learned at
    /// all, rather than the report reading as <see cref="ComponentReport.Empty"/>.</summary>
    [Fact]
    public void HeldReagentsShowEvenWhenNothingIsLearned()
    {
        var catalog = new FakeCatalog(Array.Empty<PluginSpellInfo>());
        var items = new FakeItems();
        items.Add(PeaStack(1, "Iron Pea", stackSize: 7));

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.True(report.Available);
        ComponentUsage pea = Assert.Single(report.Items);
        Assert.Equal("Iron Pea", pea.Name);
        Assert.Equal(7, pea.Stock);
        Assert.Equal(0, pea.UsedBy);
    }

    private static PluginInventoryItem PeaStack(uint objectId, string peaName, int stackSize) =>
        Stack(objectId, weenieClassId: 900_000u + objectId, stackSize) with { Name = peaName };

    private static PluginInventoryItem TalismanStack(uint objectId, string talismanName, int stackSize) =>
        Stack(objectId, weenieClassId: 9000u, stackSize) with { Name = talismanName };

    // -- allLearnedTiers (the `contribute` keyword) ----------------------------------------------

    [Fact]
    public void DefaultBehaviourStillReportsOnlyTheTopLearnedTier()
    {
        var catalog = new FakeCatalog(
        [
            Spell(201, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [10]),
            Spell(202, "Strength Other II", isSelfTargeted: false, formulaComponentIds: [20]),
        ]);
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);

        Assert.Single(report.Items);
        Assert.Equal(690u, report.Items[0].WeenieClassId); // tier II's own component (Pyreal Scarab)
    }

    /// <summary>The lower tier's reagent also appears, because <see cref="Casting.CastStateMachine"/>
    /// can step this line down to it when the higher tier's own component runs short.</summary>
    [Fact]
    public void AllLearnedTiersReportsEveryLearnedTiersOwnReagent()
    {
        var catalog = new FakeCatalog(
        [
            Spell(201, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [10]),
            Spell(202, "Strength Other II", isSelfTargeted: false, formulaComponentIds: [20]),
        ]);
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(
            catalog, items, inventoryReadable: true, allLearnedTiers: true);

        Assert.Equal(2, report.Items.Count);
        Assert.Contains(report.Items, row => row.WeenieClassId == 691u); // tier I: Lead Scarab
        Assert.Contains(report.Items, row => row.WeenieClassId == 690u); // tier II: Pyreal Scarab
    }

    // -- retail's own scarab-only formula ----------------------------------------------------------

    /// <summary>Not shown at all for a caster with no opinion (<see langword="null"/>,
    /// every existing caller, unchanged today).</summary>
    [Fact]
    public void FociCasterNeedsThePrismaticTaperTheRawFormulaNeverNamed()
    {
        var catalog = new FakeCatalog(
        [
            Spell(310, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [1u]) with { School = 1u },
        ]);
        var items = new FakeItems();

        ComponentReport withoutFocus = ComponentReportBuilder.Build(catalog, items, inventoryReadable: true);
        ComponentReport withFocus = ComponentReportBuilder.Build(
            catalog, items, inventoryReadable: true, usesScarabOnlyFormula: school => school == 1u);

        Assert.DoesNotContain(withoutFocus.Items, row => row.WeenieClassId == 20631u);

        ComponentUsage scarabRow = Assert.Single(withFocus.Items, row => row.WeenieClassId == 691u);
        Assert.Equal(1, scarabRow.UsedBy);
        ComponentUsage taperRow = Assert.Single(withFocus.Items, row => row.WeenieClassId == 20631u);
        Assert.Equal(1, taperRow.UsedBy);
    }

    [Fact]
    public void ThePredicateIsAskedPerSchoolNotOnceForTheWholeReport()
    {
        var catalog = new FakeCatalog(
        [
            Spell(310, "Strength Other I", isSelfTargeted: false, formulaComponentIds: [1u]) with { School = 1u },
            Spell(311, "Focus Other I", isSelfTargeted: false, componentSet: new PluginSpellComponentSet(Herb: 20, Powder: 0, Potion: 0, Talisman: 0)) with { School = 2u },
        ]);
        var items = new FakeItems();

        ComponentReport report = ComponentReportBuilder.Build(
            catalog, items, inventoryReadable: true, usesScarabOnlyFormula: school => school == 1u);

        Assert.Contains(report.Items, row => row.WeenieClassId == 20631u); // school 1: scarab-only
        Assert.Contains(report.Items, row => row.WeenieClassId == 690u); // school 2: raw formula, unchanged
    }
}
