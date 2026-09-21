using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Portals;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests.Portals;

/// <summary>Host-free coverage of <see cref="PortalRun"/>'s own phase machine, driven directly
/// against a real <see cref="CastStateMachine"/> the way <c>BuffCoordinator</c> shares one.</summary>
public sealed class PortalRunTests
{
    private const uint RequesterId = 900;
    private const string RequesterName = "Archer";
    private const uint WandObjectId = 4242;
    private const uint TopSpellId = 1637;
    private const uint TopFamily = 501;
    private const uint LowerSpellId = 158;
    private const uint LowerFamily = 502;
    private const uint ManaSpellId = 159;
    private const uint ManaFamily = 503;

    [Fact]
    public void IdleBotSummonsAtOnce()
    {
        var facing = new FakeFacing { CurrentHeading = 45f };
        var (run, magic, tells, said, trace, _) = NewRun(facing, PortalDirection.Right);

        Assert.Null(run.Advance(0, []));
        Assert.Equal([135f], facing.FacedDegrees); // recorded (45) + Right's 90 offset

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, [])); // completion observed, no landing line to look for

        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, [])); // the grace period clears with no refusal

        Assert.Equal([TopSpellId], magic.SentSpellIds);
        string announcement = DefaultReplies.SummoningPortal("the Holtburg lifestone");
        Assert.Equal([announcement], tells);
        Assert.Equal([announcement], said);
        Assert.Equal([135f, 45f], facing.FacedDegrees); // turned back to the recorded heading
        Assert.Contains("no refusal seen", string.Join(' ', trace));
    }

    [Fact]
    public void NoHeadingAvailableStillSummons()
    {
        var facing = new FakeFacing { CurrentHeading = null };
        var (run, magic, _, _, _, _) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, []));

        Assert.Equal([TopSpellId], magic.SentSpellIds);
        Assert.Empty(facing.FacedDegrees); // never turns without a heading to record
    }

    [Fact]
    public void NotTiedErrorTellsAndRestoresHeading()
    {
        var facing = new FakeFacing { CurrentHeading = 10f };
        var (run, magic, tells, _, _, warns) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, []));

        // The refusal arrives as its own chat line, ahead of a completion that otherwise reads clean.
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        PluginChatMessage refusal = MagicLine(WeenieErrorReplies.PortalNotTiedServerText);
        PortalRunOutcome? outcome = run.Advance(0.1, [refusal]);

        Assert.Equal(PortalRunOutcome.Failed, outcome);
        Assert.Equal(
            [DefaultReplies.SummoningPortal("the Holtburg lifestone"), DefaultReplies.NotTied(PortalTieSlot.Primary)],
            tells);
        Assert.Equal([10f, 10f], facing.FacedDegrees); // faces, then restores — no offset for Front
        Assert.Contains(WeenieErrorReplies.PortalNotTiedServerText, string.Join(' ', warns));
    }

    [Theory]
    [InlineData(WeenieErrorReplies.PortalCannotSummonServerText)]
    [InlineData(WeenieErrorReplies.PortalSummonFailedServerText)]
    public void OtherPortalRefusalsMapToCouldNotSummon(string serverText)
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var (run, magic, tells, _, _, warns) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, []));

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        PortalRunOutcome? outcome = run.Advance(0.1, [MagicLine(serverText)]);

        Assert.Equal(PortalRunOutcome.Failed, outcome);
        Assert.Equal(
            [DefaultReplies.SummoningPortal("the Holtburg lifestone"), DefaultReplies.CouldNotSummon], tells);
        Assert.Contains(serverText, string.Join(' ', warns));
    }

    [Fact]
    public void AnUnrecognisedMagicClassLineDuringTheWaitIsIgnored()
    {
        // The use-requirement case: ACE sends its own chat line, never a code this bot maps by
        // name — and per the controller's ruling, an unmapped line is no longer a refusal either.
        const string useRequirementText = "You must complete a quest to interact with that portal.";
        var facing = new FakeFacing { CurrentHeading = 0f };
        var (run, _, _, _, trace, _) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, []));
        Assert.Null(run.Advance(0.1, [MagicLine(useRequirementText)])); // ignored, not a refusal

        Assert.Contains(useRequirementText, string.Join(' ', trace)); // still traced, never acted on
    }

    [Fact]
    public void FizzleDuringTheWaitIsNotMistakenForARefusal()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var (run, magic, _, _, _, _) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, [])); // send #1
        Assert.Null(run.Advance(0.1, [MagicLine(CastStateMachine.FizzleText)])); // fizzles, retries
        Assert.Equal([TopSpellId, TopSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, []));
    }

    [Fact]
    public void AComponentConsumedLineAheadOfACleanCompletionStillSummons()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var (run, magic, _, _, _, _) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, []));

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        PluginChatMessage componentLine =
            MagicLine("The spell consumed the following components: Prismatic Taper, Prismatic Taper");
        Assert.Null(run.Advance(0.1, [componentLine])); // ignored; the completion is still observed
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, []));
    }

    [Fact]
    public void AComponentConsumedLineAheadOfNotTiedStillMapsToNotTied()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var (run, _, tells, _, _, _) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, []));

        PluginChatMessage componentLine =
            MagicLine("The spell consumed the following components: Prismatic Taper, Prismatic Taper");
        PluginChatMessage refusal = MagicLine(WeenieErrorReplies.PortalNotTiedServerText);
        // The component line arrives one message before the refusal, the way ACE sends it live.
        PortalRunOutcome? outcome = run.Advance(0.1, [componentLine, refusal]);

        Assert.Equal(PortalRunOutcome.Failed, outcome);
        Assert.Equal(
            [DefaultReplies.SummoningPortal("the Holtburg lifestone"), DefaultReplies.NotTied(PortalTieSlot.Primary)],
            tells);
    }

    [Fact]
    public void FizzleThenAStaleManaUpkeepConfirmationStillSummons()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var (run, magic, _, _, _, _) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, [])); // send #1
        Assert.Null(run.Advance(0.1, [MagicLine(CastStateMachine.FizzleText)])); // fizzles, retries
        Assert.Equal([TopSpellId, TopSpellId], magic.SentSpellIds);

        // A leftover confirmation from the mana upkeep the chain ran before this cut-in.
        PluginChatMessage upkeepConfirm =
            MagicLine("You cast Stamina to Mana Self on yourself and lose 50 mana and also gain 45 mana.");
        Assert.Null(run.Advance(0.1, [upkeepConfirm])); // ignored

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, []));
    }

    [Fact]
    public void AnExpiringEnchantmentDuringTheGraceStillSummons()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var (run, magic, _, _, _, _) = NewRun(facing, PortalDirection.Front);

        Assert.Null(run.Advance(0, []));

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));

        PluginChatMessage expiry = MagicLine("Strength Other has expired.");
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, [expiry])); // ignored; the grace still clears
    }

    [Fact]
    public void HeadingBecomingNullDuringTurnBackSkipsTheBound()
    {
        var facing = new SlowFacing { CurrentHeading = 0f };
        var (run, magic, _, _, _, _) = NewRun(facing, PortalDirection.Right); // target: 90

        Assert.Null(run.Advance(1, [])); // still turning to face
        facing.CurrentHeading = 90f; // the turn physically completes
        Assert.Null(run.Advance(0.1, [])); // now it casts
        Assert.Equal([TopSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));

        facing.CurrentHeading = null; // the heading becomes unreadable right as the summon concludes
        PortalRunOutcome? outcome = run.Advance(1.5, []); // grace clears; the turn-back must not wait out its bound
        Assert.Equal(PortalRunOutcome.Success, outcome);
    }

    [Fact]
    public void SkillRefusalStepsDownALevel()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var magic = new FakeMagic();
        magic.RefuseGateFor.Add(TopSpellId);
        var (run, _, tells, _, _, _) = NewRun(facing, PortalDirection.Front, magic);

        Assert.Null(run.Advance(0, [])); // top tier refused, steps down and sends the lower tier
        Assert.Equal([LowerSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: LowerSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, []));
        Assert.Equal(
            [DefaultReplies.SummoningPortal("the Holtburg lifestone")], tells); // no failure reply
    }

    [Fact]
    public void TimeoutRestoresHeadingAndResumes()
    {
        var facing = new FakeFacing { CurrentHeading = 200f };
        var (run, magic, tells, _, _, _) = NewRun(facing, PortalDirection.Behind);

        Assert.Null(run.Advance(0, [])); // sends the cast
        Assert.Equal([TopSpellId], magic.SentSpellIds);

        // No completion ever arrives; the caster's own 12s confirmation window lapses along the
        // way, but that alone must not end the run, or the 20s budget below could never be reached.
        Assert.Null(run.Advance(12, []));
        Assert.Null(run.Advance(7, [])); // 19s since the cast was sent - still under the budget

        PortalRunOutcome? outcome = run.Advance(1, []); // 20s since the cast was sent

        Assert.Equal(PortalRunOutcome.Failed, outcome);
        Assert.Equal(
            [DefaultReplies.SummoningPortal("the Holtburg lifestone"), DefaultReplies.PortalTimedOut], tells);
        Assert.Equal([20f, 200f], facing.FacedDegrees); // recorded (200) + Behind's 180 offset, then back
    }

    [Fact]
    public void LowManaBouncesBeforeSummoning()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment();
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        var character = new FakeCharacter { CurrentMana = 20 };
        var caster = new CastStateMachine(magic, enchantments, items, equipment, combat, character);

        IReadOnlyList<PluginSpellInfo> spells =
        [
            new(
                SpellId: TopSpellId, Name: "Summon Primary Portal III", Family: TopFamily, Tier: 3,
                Difficulty: 0, ManaCost: 150, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];
        IReadOnlyList<ResolvedSpell> manaUpkeepPlan =
        [
            new(
                "Stamina to Mana Self",
                new PluginSpellInfo(
                    SpellId: ManaSpellId, Name: "Stamina to Mana Self I", Family: ManaFamily, Tier: 1,
                    Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                    IsSelfTargeted: true, IsBeneficial: true)),
        ];

        var tie = new PortalTie("the Holtburg lifestone", PortalDirection.Front);
        var tells = new List<string>();
        var run = new PortalRun(
            new PortalRequest(RequesterId, RequesterName, PortalTieSlot.Primary), tie, spells, caster, facing,
            static _ => { }, (_, _, text) => tells.Add(text), manaUpkeepPlan: manaUpkeepPlan);

        Assert.Null(run.Advance(0, [])); // too little mana for the portal spell - bounces first
        Assert.Equal([ManaSpellId], magic.SentSpellIds);

        character.CurrentMana = 200;
        PluginChatMessage bounceConfirm = SelfConfirm("Stamina to Mana Self I");
        Assert.Null(run.Advance(0.1, [bounceConfirm])); // the bounce lands, then the portal spell itself is sent
        Assert.Equal([ManaSpellId, TopSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1.5, []));
    }

    [Fact]
    public void FatalFailureAfterALandedTopUpIsNotReadAsSuccess()
    {
        var facing = new FakeFacing { CurrentHeading = 0f };
        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment();
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        // Mana never actually rises past what the bounce lands with - every bounce is unproductive.
        var character = new FakeCharacter { CurrentMana = 20 };
        var caster = new CastStateMachine(magic, enchantments, items, equipment, combat, character);

        IReadOnlyList<PluginSpellInfo> spells =
        [
            new(
                SpellId: TopSpellId, Name: "Summon Primary Portal III", Family: TopFamily, Tier: 3,
                Difficulty: 0, ManaCost: 150, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];
        IReadOnlyList<ResolvedSpell> manaUpkeepPlan =
        [
            new(
                "Stamina to Mana Self",
                new PluginSpellInfo(
                    SpellId: ManaSpellId, Name: "Stamina to Mana Self I", Family: ManaFamily, Tier: 1,
                    Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                    IsSelfTargeted: true, IsBeneficial: true)),
        ];

        var tie = new PortalTie("the Holtburg lifestone", PortalDirection.Front);
        var tells = new List<string>();
        var run = new PortalRun(
            new PortalRequest(RequesterId, RequesterName, PortalTieSlot.Primary), tie, spells, caster, facing,
            static _ => { }, (_, _, text) => tells.Add(text), manaUpkeepPlan: manaUpkeepPlan);

        PluginChatMessage bounceConfirm = SelfConfirm("Stamina to Mana Self I");
        Assert.Null(run.Advance(0, [])); // bounce #1 sent - mana still short of the portal's 150
        Assert.Null(run.Advance(0.1, [bounceConfirm])); // bounce #1 lands (unproductively), bounce #2 sent
        Assert.Equal([ManaSpellId, ManaSpellId], magic.SentSpellIds);

        // Bounce #2 lands, still unproductive - the caster gives up on the portal spell itself,
        // never having sent it, with the last landed step belonging to the mana spell, not it.
        PortalRunOutcome? outcome = run.Advance(0.1, [bounceConfirm]);

        Assert.Equal(PortalRunOutcome.Failed, outcome);
        Assert.DoesNotContain(TopSpellId, magic.SentSpellIds);
        Assert.Equal(
            [DefaultReplies.SummoningPortal("the Holtburg lifestone"), DefaultReplies.CouldNotSummon], tells);
    }

    [Fact]
    public void TurningWaitsForTheHeadingBeforeCastingAndBeforeReportingDone()
    {
        var facing = new SlowFacing { CurrentHeading = 0f };
        var (run, magic, _, _, _, _) = NewRun(facing, PortalDirection.Right); // target: 90

        Assert.Null(run.Advance(1, [])); // still turning: 1s under the 3s bound, heading hasn't caught up
        Assert.Empty(magic.SentSpellIds);

        facing.CurrentHeading = 90f; // the turn physically completes
        Assert.Null(run.Advance(0.1, [])); // now it casts
        Assert.Equal([TopSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: TopSpellId, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(run.Advance(0.1, []));

        // The grace period clears the cast, but the physical turn back never catches up this time.
        Assert.Null(run.Advance(1.5, []));
        Assert.Equal([90f, 0f], facing.FacedDegrees); // turned to face, then asked to turn back

        Assert.Null(run.Advance(1, [])); // 2.5s into the turn-back's own 3s bound
        Assert.Equal(PortalRunOutcome.Success, run.Advance(1, [])); // the bound lapses; reports done anyway
    }

    private static (PortalRun Run, FakeMagic Magic, List<string> Tells, List<string> Said, List<string> Trace, List<string> Warn) NewRun(
        IPortalFacing facing, PortalDirection direction, FakeMagic? magic = null, double confirmationTimeoutSeconds = 12)
    {
        magic ??= new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment();
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        var character = new FakeCharacter();

        var caster = new CastStateMachine(
            magic, enchantments, items, equipment, combat, character,
            confirmationTimeoutSeconds: confirmationTimeoutSeconds);

        IReadOnlyList<PluginSpellInfo> spells =
        [
            new(
                SpellId: TopSpellId, Name: "Summon Primary Portal III", Family: TopFamily, Tier: 3,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
            new(
                SpellId: LowerSpellId, Name: "Summon Primary Portal II", Family: LowerFamily, Tier: 2,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        var tie = new PortalTie("the Holtburg lifestone", direction);
        var tells = new List<string>();
        var said = new List<string>();
        var trace = new List<string>();
        var warn = new List<string>();
        var run = new PortalRun(
            new PortalRequest(RequesterId, RequesterName, PortalTieSlot.Primary), tie, spells, caster, facing,
            said.Add, (_, _, text) => tells.Add(text), trace.Add, warn.Add);
        return (run, magic, tells, said, trace, warn);
    }

    private static PluginChatMessage MagicLine(string text) =>
        new(1, 0, (int)ChatKind.System, "", text, "") { LogTextType = LogTextTypes.Magic };

    private static PluginChatMessage SelfConfirm(string spellName) =>
        new(1, 0, (int)ChatKind.System, "", $"You cast {spellName} on yourself", "") { LogTextType = LogTextTypes.Magic };

    private static PluginInventoryItem Wand(uint objectId) =>
        new(
            ObjectId: objectId,
            WeenieClassId: 1,
            Name: "Staff of the Mhoire Forge",
            ItemType: 0,
            ContainerObjectId: 0,
            WielderObjectId: 1u,
            ValidLocations: 0,
            EquippedLocation: 1u,
            Useability: 0,
            TargetType: 0,
            PublicFlags: 0,
            StackSize: 1,
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
            UseRequiresSkillSpecialized: 0)
        {
            ObjectClass = PluginObjectClass.WandStaffOrb,
        };

    private sealed class FakeFacing : IPortalFacing
    {
        internal List<float> FacedDegrees { get; } = [];

        public float? CurrentHeading { get; set; }

        public bool Face(float degrees)
        {
            FacedDegrees.Add(degrees);
            CurrentHeading = degrees; // turns instantly, unlike SlowFacing below
            return true;
        }
    }

    /// <summary>Records what it was asked to face without ever turning on its own; a test settles
    /// <see cref="CurrentHeading"/> itself, to exercise the bounded wait.</summary>
    private sealed class SlowFacing : IPortalFacing
    {
        internal List<float> FacedDegrees { get; } = [];

        public float? CurrentHeading { get; set; }

        public bool Face(float degrees)
        {
            FacedDegrees.Add(degrees);
            return true;
        }
    }

    private sealed class FakeMagic : IMagicCommands
    {
        private readonly List<uint> _sent = [];

        internal IReadOnlyList<uint> SentSpellIds => _sent;

        internal HashSet<uint> RefuseGateFor { get; } = [];

        public bool IsCasting { get; set; }
        public PluginCastCompletion LastCompletion { get; set; }

        public PluginCastGate EvaluateGate(uint spellId) =>
            RefuseGateFor.Contains(spellId) ? PluginCastGate.NotKnown : PluginCastGate.Ready;

        public bool Cast(uint spellId) => false;

        public PluginCastGate EvaluateGate(uint spellId, uint targetObjectId) =>
            RefuseGateFor.Contains(spellId) ? PluginCastGate.NotKnown : PluginCastGate.Ready;

        public bool Cast(uint spellId, uint targetObjectId) => false;

        public PluginCastRequestResult RequestCast(uint spellId)
        {
            _sent.Add(spellId);
            IsCasting = true;
            return PluginCastRequestResult.Sent;
        }

        public PluginCastRequestResult RequestCast(uint spellId, uint targetObjectId)
        {
            _sent.Add(spellId);
            IsCasting = true;
            return PluginCastRequestResult.Sent;
        }
    }

    private sealed class FakeEnchantments : IEnchantmentAutomation
    {
        public IReadOnlyList<PluginTrackedEnchantment> Capture(uint targetObjectId) =>
            Array.Empty<PluginTrackedEnchantment>();

        public bool ReportCast(uint targetObjectId, uint spellId, double durationSeconds) => true;
    }

    private sealed class FakeCharacter : ICharacterInfo
    {
        public bool IsInWorld => true;
        public uint ObjectId => 1;
        public uint CurrentHealth => 100;
        public uint MaxHealth => 100;
        public uint CurrentStamina => 100;
        public uint MaxStamina => 100;
        public uint CurrentMana { get; set; } = 200;
        public uint MaxMana => 200;
        public IReadOnlyList<PluginSkillInfo> Skills => Array.Empty<PluginSkillInfo>();
        public IReadOnlyList<PluginAttributeInfo> Attributes => Array.Empty<PluginAttributeInfo>();
        public IReadOnlyList<PluginActiveEnchantment> ActiveEnchantments => Array.Empty<PluginActiveEnchantment>();

        public bool TryGetSkill(uint skillId, out PluginSkillInfo skill)
        {
            skill = default;
            return false;
        }
    }

    private sealed class FakeItems : IItemAutomation
    {
        private readonly List<PluginInventoryItem> _items = [];

        internal void Add(PluginInventoryItem item) => _items.Add(item);

        public IReadOnlyList<PluginInventoryItem> CaptureOwnedItems() => _items;
    }

    private sealed class FakeEquipment : IEquipmentAutomation
    {
        public PluginEquipmentCommandResult Equip(uint objectId, uint requestedLocation = 0u) =>
            new(PluginEquipmentCommandStatus.Started);
    }

    private sealed class FakeCombat : ICombatAutomation
    {
        internal PluginCombatMode Mode { get; set; } = PluginCombatMode.Peace;

        public PluginCombatSnapshot Snapshot => new(
            0u, Mode, PluginAttackHeight.Medium, 0f, 0f, false, false, false, false);

        public IReadOnlyList<PluginCombatTarget> CaptureHostileTargets(float maximumDistance) =>
            Array.Empty<PluginCombatTarget>();

        public PluginCombatCommandResult EnterDefaultMode() => new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult EnterMode(PluginCombatMode mode)
        {
            Mode = mode;
            return new(PluginCombatCommandStatus.ModeChangeSent);
        }

        public PluginCombatCommandResult BeginPhysicalAttack(
            uint targetObjectId, PluginAttackHeight height, float power) =>
            new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult ReleasePhysicalAttack() =>
            new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult AbortPhysicalAttack() =>
            new(PluginCombatCommandStatus.Unavailable);
    }
}
