using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Spells;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

/// <summary>Proves sequencing above <see cref="CastStateMachine"/> and below
/// <c>BuffBotPlugin</c>, the only ring allowed to touch <c>IPluginHost</c>.</summary>
public sealed class BuffCoordinatorTests
{
    private const uint RequesterId = 555;
    private const string RequesterName = "Archer";
    private const uint WandObjectId = 4242;
    private const uint SelfFamily = 77;
    private const uint SelfSpellId = 99;
    private const uint OtherFamily = 10;
    private const uint OtherSpellId = 42;

    [Fact]
    public void ARequestRunCastsDueSelfSpellsBeforeTheRequesterSpell()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);

        // Tick 1: dequeues the request, wields/enters magic mode in the same tick (a
        // CastStateMachine mechanic already proven elsewhere), and sends the self cast first.
        Pump(coordinator, [], enabled: true, replies);
        Assert.Equal([SelfSpellId], magic.SentSpellIds);
        Assert.True(magic.LastRequestWasSelfTargeted);

        // Tick 2: the self confirmation lands, which both closes the self step and sends the
        // requester's own spell in the same Advance call.
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(coordinator, [SelfConfirm("Focus Self VI")], enabled: true, replies);
        Assert.Equal([SelfSpellId, OtherSpellId], magic.SentSpellIds);
        Assert.False(magic.LastRequestWasSelfTargeted);

        // Tick 3: the requester's own confirmation lands, finishing the run.
        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump(coordinator, [Confirm("Strength Other I", RequesterName)], enabled: true, replies);

        string closing = Assert.Single(replies);
        Assert.Equal("All set: cast 2 buffs.", closing);
    }

    // -- donations pause the run ----------------------------------------------------------------

    [Fact]
    public void WhileATradeIsOpenNoNewRunStartsAndTheQueueSurvives()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);

        BuffBotStatus status = Pump(coordinator, [], enabled: true, replies, tradeOpen: true);

        Assert.Empty(magic.SentSpellIds);
        Assert.Equal(BotActivity.Idle, status.Activity);
        Assert.Single(status.Waiting);
        Assert.Empty(replies);
    }

    [Fact]
    public void ARequestQueuedDuringATradeStartsOnceTheTradeCloses()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);

        Pump(coordinator, [], enabled: true, replies, tradeOpen: true);
        Assert.Empty(magic.SentSpellIds);

        Pump(coordinator, [], enabled: true, replies, tradeOpen: false);
        Assert.Equal([SelfSpellId], magic.SentSpellIds);
    }

    /// <summary>Item 2's own defence: a trade can only open while the bot is in peace, and a run
    /// already in flight is left to finish even if one opens anyway.</summary>
    [Fact]
    public void ARunAlreadyInFlightContinuesEvenIfATradeOpensMidRun()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);

        Pump(coordinator, [], enabled: true, replies);
        Assert.Equal([SelfSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(coordinator, [SelfConfirm("Focus Self VI")], enabled: true, replies, tradeOpen: true);

        Assert.Equal([SelfSpellId, OtherSpellId], magic.SentSpellIds);
    }

    [Fact]
    public void ARequesterWhoLeavesRangeMidRunGetsTheOutOfRangeReplyAndTheRunStopsThere()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);

        double distance = 10d;

        // Tick 1: dequeues the request while in range and sends the due self spell first, exactly
        // as the sequencing test above.
        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: _ => distance,
            sendReply: (_, _, text) => replies.Add(text));
        Assert.Equal([SelfSpellId], magic.SentSpellIds);

        // The requester walks off before the self spell's confirmation lands and the run reaches
        // the requester's own spell: the range check applies during a run, not just at the start.
        distance = 999d;

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [SelfConfirm("Focus Self VI")], selfBuffingEnabled: true,
            distanceToRequester: _ => distance,
            sendReply: (_, _, text) => replies.Add(text));

        // Strength Other was never attempted, and the requester heard the same out-of-range text
        // a fresh request past range gets, rather than silence or a distinct message.
        Assert.Equal([SelfSpellId], magic.SentSpellIds);
        Assert.Equal([DefaultReplies.OutOfRange], replies);
    }

    // -- a settings-driven refusal range ---------------------------------------------------------

    [Fact]
    public void ANarrowerRefusalRangeRefusesADistanceTheDefaultWouldHaveAllowed()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);
        coordinator.RefusalRangeMeters = 40d;

        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: _ => 50d, // within the 67.5 m default, past the 40 m setting
            sendReply: (_, _, text) => replies.Add(text));

        Assert.Empty(magic.SentSpellIds);
        Assert.Equal([DefaultReplies.OutOfRange], replies);
    }

    // -- mana bounce watermarks and vitals -------------------------------------------------------

    [Fact]
    public void ManaBounceFractionsPassThroughToTheCaster()
    {
        var (coordinator, _, _) = NewCoordinator(new RequestQueue(5));

        coordinator.ManaBounceLowWaterFraction = 0.3;
        coordinator.ManaBounceHighWaterFraction = 0.9;

        Assert.Equal(0.3, coordinator.ManaBounceLowWaterFraction);
        Assert.Equal(0.9, coordinator.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void StatusCarriesHealthAndStaminaFromTheCharacter()
    {
        var character = new FakeCharacter();
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, character: character);

        BuffBotStatus status = coordinator.Pump(
            0, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, _) => { });

        Assert.Equal(character.CurrentHealth, status.CurrentHealth);
        Assert.Equal(character.MaxHealth, status.MaxHealth);
        Assert.Equal(character.CurrentStamina, status.CurrentStamina);
        Assert.Equal(character.MaxStamina, status.MaxStamina);
    }

    [Fact]
    public void DefaultRefusalRangeMatchesNinetyPercentOfCastRange() =>
        Assert.Equal(
            SolrLabs.BuffBot.Policy.RangePolicy.MaxCastRangeMeters * 0.9,
            NewCoordinator(new RequestQueue(5)).Coordinator.RefusalRangeMeters);

    // -- session statistics -----------------------------------------------------------------------

    [Fact]
    public void AnOutOfRangeRefusalIsRecordedOnTheSharedStats()
    {
        var stats = new BotStats();
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, _, replies) = NewCoordinator(queue, stats: stats);

        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 999d,
            sendReply: (_, _, text) => replies.Add(text));

        Assert.Equal(1, stats.Snapshot().Session.Refusals.OutOfRange);
        RecentEvent entry = Assert.Single(stats.Snapshot().Recent);
        Assert.Equal(RecentEventKind.Refused, entry.Kind);
        Assert.Equal(RequesterName, entry.Who);
    }

    [Fact]
    public void ARequestServedFromStartToFinishFeedsLinesAndPlayersServed()
    {
        var stats = new BotStats();
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, stats: stats);

        Pump(coordinator, [], enabled: true, replies);
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(coordinator, [SelfConfirm("Focus Self VI")], enabled: true, replies);
        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump(coordinator, [Confirm("Strength Other I", RequesterName)], enabled: true, replies);

        IReadOnlyList<SpellLineStats> lines = stats.Snapshot().Session.Lines;
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Line == "Strength Other" && l.Landed == 1);
        Assert.Equal(1, stats.Snapshot().Session.PlayersServed);
    }

    // -- young-bot leniency (unit 1b) -----------------------------------------------------------

    [Fact]
    public void AYoungBotCastsWhatItKnowsAndNamesTheLineItHasNotLearned()
    {
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = DefaultSpellSets.Table[DefaultSpellSets.Self],
            [DefaultSpellSets.Buff] = ["Strength Other", "Endurance Other"],
        };
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, spellSets);

        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));
        Assert.Equal([SelfSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [SelfConfirm("Focus Self VI")], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));
        Assert.Equal([SelfSpellId, OtherSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [Confirm("Strength Other I", RequesterName)], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        string closing = Assert.Single(replies);
        Assert.Equal(
            "I cast what I could: buffed 2, 0 already up. I haven't learned Endurance Other yet.",
            closing);
    }

    [Fact]
    public void AYoungBotIsRefusedOnlyWhenNothingInTheProfileIsLearned()
    {
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = DefaultSpellSets.Table[DefaultSpellSets.Self],
            [DefaultSpellSets.Buff] = ["Endurance Other", "Willpower Other"],
        };
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, spellSets);

        coordinator.Pump(
            0, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Assert.Empty(magic.SentSpellIds);
        Assert.Equal(["I haven't learned anything for buff yet."], replies);
    }

    [Fact]
    public void TargetTierPassedToTheCoordinatorAppliesOnlyToTheRequesterSOwnLine()
    {
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = DefaultSpellSets.Table[DefaultSpellSets.Self],
            [DefaultSpellSets.Buff] = ["Strength Other"],
        };
        PluginSpellInfo[] catalog =
        [
            Catalog[0], // Focus Self VI, unaffected by the target -- self upkeep keeps top learned
            new(
                SpellId: 100, Name: "Strength Other I", Family: OtherFamily, Tier: 1, Difficulty: 0,
                ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: 101, Name: "Strength Other VI", Family: OtherFamily, Tier: 6, Difficulty: 0,
                ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
        ];
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, spellSets, targetTier: 1);

        coordinator.Pump(
            0, catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));
        Assert.Equal([SelfSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [SelfConfirm("Focus Self VI")], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        // Target tier 1 -> Strength Other I, not the top learned VI.
        Assert.Equal([SelfSpellId, 100u], magic.SentSpellIds);
    }

    [Fact]
    public void WritingTargetTierAfterARequestIsDequeuedAppliesOnlyToTheNextOne()
    {
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = DefaultSpellSets.Table[DefaultSpellSets.Self],
            [DefaultSpellSets.Buff] = ["Strength Other"],
        };
        PluginSpellInfo[] catalog =
        [
            Catalog[0], // Focus Self VI
            new(
                SpellId: 100, Name: "Strength Other I", Family: OtherFamily, Tier: 1, Difficulty: 0,
                ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: 101, Name: "Strength Other VI", Family: OtherFamily, Tier: 6, Difficulty: 0,
                ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
        ];
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, spellSets); // TargetTier starts null

        // Request A dequeues and resolves its plan now, at the default (top learned).
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));
        Assert.Equal([SelfSpellId], magic.SentSpellIds);

        // A console write lands mid-run — after A's plan was already built.
        coordinator.TargetTier = 1;

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [SelfConfirm("Focus Self VI")], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        // A still gets the top learned VI: the write did not reach its already-resolved plan.
        Assert.Equal([SelfSpellId, 101u], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: 101, TargetObjectId: RequesterId, WeenieError: 0);
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [Confirm("Strength Other VI", RequesterName)], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        const uint SecondRequesterId = 556;
        const string SecondRequesterName = "Probe";
        magic.IsCasting = false; // the server-side cast A's run ended on has since resolved
        queue.TryEnqueue(new BuffRequest(SecondRequesterId, SecondRequesterName, DefaultSpellSets.Buff), out _);
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Assert.Equal([SelfSpellId, 101u, 100u], magic.SentSpellIds);
    }

    [Fact]
    public void IdleTimerNeverFiresWhileDisabledEvenPastTheInterval()
    {
        var (coordinator, magic, replies) = NewCoordinator(new RequestQueue(5));

        Pump(coordinator, [], enabled: false, replies, deltaSeconds: 90);

        Assert.Empty(magic.SentSpellIds);
    }

    [Fact]
    public void IdleTimerDoesNotFireBeforeSixtySecondsOfIdleTimeElapse()
    {
        var (coordinator, magic, replies) = NewCoordinator(new RequestQueue(5));

        Pump(coordinator, [], enabled: true, replies, deltaSeconds: 59);

        Assert.Empty(magic.SentSpellIds);
    }

    [Fact]
    public void IdleTimerFiresOnceEnabledAndSixtySecondsOfIdleTimeHaveElapsed()
    {
        var (coordinator, magic, replies) = NewCoordinator(new RequestQueue(5));

        Pump(coordinator, [], enabled: true, replies, deltaSeconds: 59);
        Assert.Empty(magic.SentSpellIds);

        // Crossing the interval begins the self-only run, and this same tick's Advance call
        // sends it (the same fall-through shape a freshly dequeued request uses).
        Pump(coordinator, [], enabled: true, replies, deltaSeconds: 2);

        Assert.Equal([SelfSpellId], magic.SentSpellIds);
        Assert.True(magic.LastRequestWasSelfTargeted);

        // No requester was involved — nothing is sent as a reply.
        Assert.Empty(replies);
    }

    [Fact]
    public void IdleSelfBuffRunNeverStartsWhileARequestIsInFlight()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);

        Pump(coordinator, [], enabled: true, replies, deltaSeconds: 59);
        Assert.Equal([SelfSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(coordinator, [SelfConfirm("Focus Self VI")], enabled: true, replies, deltaSeconds: 59);
        Assert.Equal([SelfSpellId, OtherSpellId], magic.SentSpellIds);

        // Only ever one self cast was ever sent, even though 118 s of "enabled" ticking passed —
        // it was all absorbed by the in-flight request, never by the idle timer.
        Assert.Equal(1, magic.SentSpellIds.Count(id => id == SelfSpellId));
    }

    [Fact]
    public void ARequestBouncesManaThroughTheCatalogResolvedUpkeepPairBeforeCastingOnTheRequester()
    {
        const uint ManaSpellFamily = 900;
        const uint ManaSpellId = 901;
        const uint RevitalizeFamily = 901;
        const uint RevitalizeSpellId = 902;

        var character = new FakeCharacter { CurrentMana = 0 };
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);

        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();

        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other"],
            [DefaultSpellSets.ManaUpkeep] = DefaultSpellSets.Table[DefaultSpellSets.ManaUpkeep],
        };

        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, spellSets, character: character);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: OtherSpellId, Name: "Strength Other I", Family: OtherFamily, Tier: 1,
                Difficulty: 0, ManaCost: 50, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: ManaSpellId, Name: "Stamina to Mana Self I", Family: ManaSpellFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
            new(
                SpellId: RevitalizeSpellId, Name: "Revitalize Self I", Family: RevitalizeFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        List<string> replies = [];
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        // Strength Other costs more mana than the caster has: the coordinator's own catalog
        // lookup for DefaultSpellSets.ManaUpkeep is what sent the bounce, not Strength Other.
        Assert.Equal([ManaSpellId], magic.SentSpellIds);
        Assert.True(magic.LastRequestWasSelfTargeted);
    }

    [Fact]
    public void ASpellRefusedMidRunIsSkippedAndTheClosingReplyIsPartial()
    {
        // One per-spell failure must not cost the rest of the profile, and the requester must hear
        // the truth (neither Done nor Stopped) about it.
        const uint SecondSpellId = 43;
        const uint SecondFamily = 11;

        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);

        var magic = new FakeMagic();
        magic.RefuseGateFor.Add(OtherSpellId); // Strength Other's gate is refused every time
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();

        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other", "Endurance Other"],
        };
        var coordinator = new BuffCoordinator(queue, magic, enchantments, items, equipment, combat, spellSets);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: OtherSpellId, Name: "Strength Other I", Family: OtherFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: SecondSpellId, Name: "Endurance Other I", Family: SecondFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
        ];

        List<string> replies = [];
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        // Strength Other's refused gate is skipped; Endurance Other is sent in the same tick.
        Assert.Equal([SecondSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SecondSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [Confirm("Endurance Other I", RequesterName)], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        string closing = Assert.Single(replies);
        Assert.StartsWith("I cast what I could: buffed 1, 0 already up, but missed 1: Strength Other", closing);
    }

    [Fact]
    public void ManaIsToppedUpBetweenRequestersBeforeTheNextChainsOwnSpellIsSent()
    {
        // Mana bounces back up to full before the next requester's own spell is sent.
        const uint SecondRequesterId = 777;
        const string SecondRequesterName = "Rogue";
        const uint ManaSpellFamily = 900;
        const uint ManaSpellId = 901;
        const uint RevitalizeFamily = 901;
        const uint RevitalizeSpellId = 902;

        var character = new FakeCharacter { CurrentMana = 200 }; // full - this FakeCharacter's MaxMana
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(SecondRequesterId, SecondRequesterName, DefaultSpellSets.Buff), out _);

        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();

        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other"],
            [DefaultSpellSets.ManaUpkeep] = DefaultSpellSets.Table[DefaultSpellSets.ManaUpkeep],
        };
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, spellSets, character: character);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: OtherSpellId, Name: "Strength Other I", Family: OtherFamily, Tier: 1,
                Difficulty: 0, ManaCost: 50, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: ManaSpellId, Name: "Stamina to Mana Self I", Family: ManaSpellFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
            new(
                SpellId: RevitalizeSpellId, Name: "Revitalize Self I", Family: RevitalizeFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        List<string> replies = [];
        void Pump(IReadOnlyList<PluginChatMessage> messages) => coordinator.Pump(
            0, catalog, activeEnchantments: [], messages, selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Pump([]); // dequeues Archer (the first ever request - no top-up gates this one)
        Assert.Equal([OtherSpellId], magic.SentSpellIds);

        character.CurrentMana = 60;
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump([Confirm("Strength Other I", RequesterName)]);

        string closing = Assert.Single(replies);
        // "All set:" since the voice pass (DefaultReplies.ClosingSuccess) -- the top-up is silent
        // and must not change what the requester is told.
        Assert.StartsWith("All set:", closing);
        Assert.Equal([OtherSpellId], magic.SentSpellIds); // the top-up hasn't cast anything yet this tick

        magic.IsCasting = false; // the server's own use-done for Archer's just-confirmed cast
        Pump([]); // now the top-up itself begins - self-targeted, not Rogue's Strength Other
        Assert.Equal([OtherSpellId, ManaSpellId], magic.SentSpellIds);
        Assert.True(magic.LastRequestWasSelfTargeted);

        character.CurrentMana = 170; // clears the high mark in one cycle
        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: ManaSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump([SelfConfirm("Stamina to Mana Self I")]); // sends Revitalize Self
        Assert.Equal([OtherSpellId, ManaSpellId, RevitalizeSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 4, SpellId: RevitalizeSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump([SelfConfirm("Revitalize Self I")]); // the top-up closes; Rogue's own plan begins but
                                                   // has not cast anything yet this same tick
        Assert.Equal([OtherSpellId, ManaSpellId, RevitalizeSpellId], magic.SentSpellIds);

        Pump([]); // only now does Rogue's own Strength Other go out
        Assert.Equal([OtherSpellId, ManaSpellId, RevitalizeSpellId, OtherSpellId], magic.SentSpellIds);
        Assert.False(magic.LastRequestWasSelfTargeted);
    }

    [Fact]
    public void TheFirstRequestEverServedIsNeverDelayedByATopUp()
    {
        var character = new FakeCharacter { CurrentMana = 60 }; // below the 160 high mark, above the 40 low mark
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);

        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other"],
        };
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, spellSets, character: character);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: OtherSpellId, Name: "Strength Other I", Family: OtherFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
        ];

        List<string> replies = [];
        coordinator.Pump(
            0, catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Assert.Equal([OtherSpellId], magic.SentSpellIds); // sent directly, no top-up cast first
        Assert.False(magic.LastRequestWasSelfTargeted);
    }

    // -- BuffBotStatus (operator-panel-unit-1) -------------------------------------------------

    [Fact]
    public void StatusIsIdleWhenNothingIsQueuedOrDue()
    {
        var (coordinator, _, replies) = NewCoordinator(new RequestQueue(5));

        BuffBotStatus status = Pump(coordinator, [], enabled: true, replies, deltaSeconds: 1);

        Assert.True(status.Enabled);
        Assert.Equal(BotActivity.Idle, status.Activity);
        Assert.Null(status.CurrentRequesterName);
        Assert.Null(status.CurrentSpellLine);
        Assert.Equal(0u, status.CurrentSpellId);
        Assert.Equal(0, status.StepIndex);
        Assert.Equal(0, status.StepCount);
        Assert.Empty(status.Waiting);
    }

    [Fact]
    public void StatusIsServingTheCurrentRequesterAndTracksStepProgress()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, _, replies) = NewCoordinator(queue);

        // Dequeued and its due self spell sent in this same tick (self-buff milestone) -- already
        // "Serving", not "Idle", even though the requester's own spell hasn't been reached yet.
        BuffBotStatus status = Pump(coordinator, [], enabled: true, replies);

        Assert.Equal(BotActivity.Serving, status.Activity);
        Assert.Equal(RequesterName, status.CurrentRequesterName);
        Assert.Equal("Focus Self", status.CurrentSpellLine);
        Assert.Equal(SelfSpellId, status.CurrentSpellId);
        Assert.Equal(0, status.StepIndex);
        Assert.Equal(2, status.StepCount); // the due self line, then the requester's own
        Assert.Empty(status.Waiting); // dequeued -- no longer "waiting", per RequestQueue.Waiting
    }

    [Fact]
    public void StatusReportsWhoIsStillWaitingBehindTheOneBeingServed()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(999, "Second", DefaultSpellSets.Buff), out _);
        var (coordinator, _, replies) = NewCoordinator(queue);

        BuffBotStatus status = Pump(coordinator, [], enabled: true, replies);

        QueueEntry waiting = Assert.Single(status.Waiting);
        Assert.Equal(999u, waiting.ObjectId);
        Assert.Equal("Second", waiting.Name);
        Assert.Equal(DefaultSpellSets.Buff, waiting.Archetype);
    }

    [Fact]
    public void StatusIsSelfBuffingDuringTheIdleTimersOwnRun()
    {
        var (coordinator, _, replies) = NewCoordinator(new RequestQueue(5));

        Pump(coordinator, [], enabled: true, replies, deltaSeconds: 59);
        BuffBotStatus status = Pump(coordinator, [], enabled: true, replies, deltaSeconds: 2);

        Assert.Equal(BotActivity.SelfBuffing, status.Activity);
        Assert.Null(status.CurrentRequesterName);
        Assert.Equal(SelfSpellId, status.CurrentSpellId);
    }

    [Fact]
    public void StatusIsToppingUpBeforeTheNextRequestersOwnPlanBegins()
    {
        const uint SecondRequesterId = 777;
        const string SecondRequesterName = "Rogue";
        const uint ManaSpellFamily = 900;
        const uint ManaSpellId = 901;
        const uint RevitalizeFamily = 901;
        const uint RevitalizeSpellId = 902;

        var character = new FakeCharacter { CurrentMana = 200 };
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(SecondRequesterId, SecondRequesterName, DefaultSpellSets.Buff), out _);

        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();

        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other"],
            [DefaultSpellSets.ManaUpkeep] = DefaultSpellSets.Table[DefaultSpellSets.ManaUpkeep],
        };
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, spellSets, character: character);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: OtherSpellId, Name: "Strength Other I", Family: OtherFamily, Tier: 1,
                Difficulty: 0, ManaCost: 50, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: ManaSpellId, Name: "Stamina to Mana Self I", Family: ManaSpellFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
            new(
                SpellId: RevitalizeSpellId, Name: "Revitalize Self I", Family: RevitalizeFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        List<string> replies = [];
        BuffBotStatus RunPump(IReadOnlyList<PluginChatMessage> messages) => coordinator.Pump(
            0, catalog, activeEnchantments: [], messages, selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        RunPump([]); // dequeues and serves Archer, the first ever request
        character.CurrentMana = 60; // below the high mark once Archer's chain has paid for a spell
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        RunPump([Confirm("Strength Other I", RequesterName)]); // closes Archer's chain

        magic.IsCasting = false; // the server's own use-done for Archer's just-confirmed cast
        BuffBotStatus status = RunPump([]); // the top-up begins now, ahead of Rogue's own plan

        Assert.Equal(BotActivity.ToppingUp, status.Activity);
        Assert.Equal(SecondRequesterName, status.CurrentRequesterName);
    }

    [Fact]
    public void CountersAccumulateAcrossMultipleRunsRatherThanResettingEachChain()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);

        BuffBotStatus PumpWith(IReadOnlyList<PluginChatMessage> messages, int tellsAnswered) =>
            coordinator.Pump(
                0, Catalog, activeEnchantments: [], messages, selfBuffingEnabled: true,
                distanceToRequester: static _ => 10d,
                sendReply: (_, _, text) => replies.Add(text),
                tellsAnswered: tellsAnswered);

        PumpWith([], tellsAnswered: 3);
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        PumpWith([SelfConfirm("Focus Self VI")], tellsAnswered: 4);
        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        BuffBotStatus afterFirstChain = PumpWith([Confirm("Strength Other I", RequesterName)], tellsAnswered: 5);

        Assert.Equal(2, afterFirstChain.Counters.CastsLanded); // the self line, then the requester's
        Assert.Equal(5, afterFirstChain.Counters.TellsAnswered);

        magic.IsCasting = false; // the server's own use-done for the just-confirmed cast above
        queue.TryEnqueue(new BuffRequest(999, "Second", DefaultSpellSets.Buff), out _);

        PumpWith([], tellsAnswered: 5);
        magic.LastCompletion = new PluginCastCompletion(Revision: 4, SpellId: OtherSpellId, TargetObjectId: 999, WeenieError: 0);
        BuffBotStatus afterSecondChain = PumpWith([Confirm("Strength Other I", "Second")], tellsAnswered: 6);

        // The second chain's cast lands on top of the first chain's, not in place of it -- this
        // is a running total for the life of the coordinator, not a per-run count.
        Assert.Equal(3, afterSecondChain.Counters.CastsLanded);
        Assert.Equal(6, afterSecondChain.Counters.TellsAnswered);
    }

    // -- bane-on-shield (plan-console-live unit 8b) ----------------------------------------------

    private const uint ShieldObjectId = 888;
    private const string ShieldName = "Round Shield";
    private const uint AcidBaneSpellId = 71;
    private const uint AcidBaneFamily = 71;

    private static PluginWorldObject ShieldObject(uint wielderObjectId) =>
        new(ShieldObjectId, WeenieClassId: 1, ShieldName, PluginObjectClass.Armor, ItemType: 0, ContainerObjectId: 0, wielderObjectId);

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> HeavySpellSets() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = DefaultSpellSets.Table[DefaultSpellSets.Self],
            [DefaultSpellSets.Heavy] = ["Strength Other"],
        };

    [Fact]
    public void ARequesterWithAWieldedShieldGetsItsLearnedBaneCastAtTheShieldsOwnObjectId()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Heavy), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, HeavySpellSets());

        // Tick 1: self line first, as always.
        Pump(coordinator, [], enabled: true, replies, catalog: CatalogWithABane,
            capturedObjects: [ShieldObject(RequesterId)]);
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);

        // Tick 2: the requester's own "Strength Other" line.
        Pump(coordinator, [SelfConfirm("Focus Self VI")], enabled: true, replies, catalog: CatalogWithABane,
            capturedObjects: [ShieldObject(RequesterId)]);
        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);

        // Tick 3: Strength Other confirms, and the next step -- the learned bane -- is sent
        // straight at the shield's own object id, never the requester's.
        Pump(coordinator, [Confirm("Strength Other I", RequesterName)], enabled: true, replies, catalog: CatalogWithABane,
            capturedObjects: [ShieldObject(RequesterId)]);

        Assert.Equal(AcidBaneSpellId, Assert.Single(magic.SentSpellIds, id => id == AcidBaneSpellId));
        Assert.Equal(ShieldObjectId, magic.LastRequestTargetObjectId);
    }

    [Fact]
    public void ARequesterWithNoShieldEquippedHasItsBanesSkippedWithOneSharedReason()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Heavy), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, HeavySpellSets());

        // No captured objects at all -- ShieldFinder finds nothing wielded by the requester.
        Pump(coordinator, [], enabled: true, replies, catalog: CatalogWithABane);
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(coordinator, [SelfConfirm("Focus Self VI")], enabled: true, replies, catalog: CatalogWithABane);
        magic.LastCompletion = new PluginCastCompletion(Revision: 3, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump(coordinator, [Confirm("Strength Other I", RequesterName)], enabled: true, replies, catalog: CatalogWithABane);

        Assert.DoesNotContain(AcidBaneSpellId, magic.SentSpellIds);
        string closing = Assert.Single(replies);
        Assert.Contains("No shield equipped", closing);
        // The reason is named once even though seven bane lines were skipped, not once per bane.
        Assert.Equal(1, closing.Split("No shield equipped").Length - 1);
    }

    // -- stop-after-current -----------------------------------------------------------------------

    private const uint EnduranceSpellId = 43;
    private const uint EnduranceFamily = 11;

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> NoSelfSpellSets() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other"],
        };

    /// <summary>Two learned target lines, so a stop after the first cast still has
    /// a second one ahead of it to prove was never attempted.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> TwoLineSpellSets() =>
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other", "Endurance Other"],
        };

    private static List<PluginSpellInfo> TwoLineCatalog() =>
        [
            .. Catalog,
            new(
                SpellId: EnduranceSpellId, Name: "Endurance Other I", Family: EnduranceFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
        ];

    [Fact]
    public void TryStopActiveRunDoesNothingForAStrangerOrAStillWaitingRequester()
    {
        const uint OtherId = 111;
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(OtherId, "Other", DefaultSpellSets.Buff), out _);
        var (coordinator, _, replies) = NewCoordinator(queue, NoSelfSpellSets());
        Pump(coordinator, [], enabled: true, replies); // dequeues and serves Archer only

        Assert.False(coordinator.TryStopActiveRun(999, RunStopReason.RequesterCancelled)); // never asked
        Assert.False(coordinator.TryStopActiveRun(OtherId, RunStopReason.RequesterCancelled)); // waiting
    }

    /// <summary>The cast already in the air still lands, the second line is never attempted, and
    /// the bot moves on to the next in line once the stopped run closes.</summary>
    [Fact]
    public void TryStopActiveRunStopsAfterTheCurrentCastAndMovesOnToTheNextRequester()
    {
        const uint SecondRequesterId = 777;
        const string SecondRequesterName = "Rogue";

        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(SecondRequesterId, SecondRequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, TwoLineSpellSets());
        List<PluginSpellInfo> catalog = TwoLineCatalog();

        Pump(coordinator, [], enabled: true, replies, catalog: catalog); // sends Strength Other
        Assert.Equal([OtherSpellId], magic.SentSpellIds);

        Assert.True(coordinator.TryStopActiveRun(RequesterId, RunStopReason.RequesterCancelled));

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump(coordinator, [Confirm("Strength Other I", RequesterName)], enabled: true, replies, catalog: catalog);

        // Stopped before Endurance Other was ever attempted.
        Assert.Equal([OtherSpellId], magic.SentSpellIds);
        Assert.Equal([DefaultReplies.ClosingStopped(RunStopReason.RequesterCancelled)], replies);

        magic.IsCasting = false; // the server's own use-done for Archer's just-confirmed cast
        Pump(coordinator, [], enabled: true, replies, catalog: catalog); // moves on to Rogue

        Assert.Equal([OtherSpellId, OtherSpellId], magic.SentSpellIds);
    }

    [Fact]
    public void RequestStopForDisableTellsEachWaitingRequesterOnceAndNeverTouchesTheRunInFlight()
    {
        const uint SecondRequesterId = 888;
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        queue.TryEnqueue(new BuffRequest(SecondRequesterId, "Waiting", DefaultSpellSets.Buff), out _);
        var (coordinator, _, replies) = NewCoordinator(queue, NoSelfSpellSets());
        Pump(coordinator, [], enabled: true, replies); // dequeues and serves Archer only

        IReadOnlyList<BuffRequest> drained = coordinator.RequestStopForDisable();

        Assert.Equal([SecondRequesterId], drained.Select(r => r.RequesterObjectId));
        Assert.True(coordinator.HasActiveRequester); // Archer's own run is still in flight
    }

    [Fact]
    public void RequestStopForDisableStopsTheActiveRunAfterItsCurrentCast()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue, TwoLineSpellSets());
        List<PluginSpellInfo> catalog = TwoLineCatalog();
        Pump(coordinator, [], enabled: true, replies, catalog: catalog);
        Assert.Equal([OtherSpellId], magic.SentSpellIds);

        coordinator.RequestStopForDisable();

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump(coordinator, [Confirm("Strength Other I", RequesterName)], enabled: true, replies, catalog: catalog);

        // Stopped before Endurance Other was ever attempted.
        Assert.Equal([OtherSpellId], magic.SentSpellIds);
        Assert.Equal([DefaultReplies.ClosingStopped(RunStopReason.BotDisabling)], replies);
        Assert.False(coordinator.HasActiveRequester);
    }

    [Fact]
    public void HasActiveRequesterIsFalseWhenNothingIsQueuedOrRunning() =>
        Assert.False(NewCoordinator(new RequestQueue(5)).Coordinator.HasActiveRequester);

    // -- WouldFindNothingLearned ------------------------------------------------------------------

    [Fact]
    public void WouldFindNothingLearnedIsTrueForAProfileWithNoLinesLearned()
    {
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Endurance Other"],
        };
        var (coordinator, _, _) = NewCoordinator(new RequestQueue(5), spellSets);

        Assert.True(coordinator.WouldFindNothingLearned(DefaultSpellSets.Buff, Catalog));
    }

    [Fact]
    public void WouldFindNothingLearnedIsFalseWhenAtLeastOneLineIsLearned()
    {
        var (coordinator, _, _) = NewCoordinator(new RequestQueue(5));

        Assert.False(coordinator.WouldFindNothingLearned(DefaultSpellSets.Buff, Catalog));
    }

    // -- OpenAC 8b2c5147: KnownSelfBuffs narrowed to self-targetable spells only ----------------

    [Fact]
    public void ADev2StyleCatalogFindsNothingLearnedFromKnownSelfBuffsAloneButResolvesThroughTheUnion()
    {
        var (coordinator, _, _) = NewCoordinator(new RequestQueue(5));
        var dev2Catalog = new Dev2StyleCatalog();

        // What BuffBotPlugin read before this fix: a self-only KnownSelfBuffs finds nothing for
        // a profile made entirely of "... Other" lines.
        Assert.True(coordinator.WouldFindNothingLearned(DefaultSpellSets.Buff, dev2Catalog.KnownSelfBuffs));

        // BeneficialSpellCatalog.Resolve finds "Strength Other" through All + IsKnown even though
        // it never made KnownSelfBuffs.
        Assert.False(coordinator.WouldFindNothingLearned(
            DefaultSpellSets.Buff, BeneficialSpellCatalog.Resolve(dev2Catalog)));
    }

    [Fact]
    public void ADev2StyleCatalogStillCastsTheBuffProfileThroughTheUnionCatalog()
    {
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        var (coordinator, magic, replies) = NewCoordinator(queue);
        var dev2Catalog = new Dev2StyleCatalog();

        Pump(coordinator, [], enabled: true, replies, catalog: BeneficialSpellCatalog.Resolve(dev2Catalog));
        Assert.Equal([SelfSpellId], magic.SentSpellIds);

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(
            coordinator, [SelfConfirm("Focus Self VI")], enabled: true, replies,
            catalog: BeneficialSpellCatalog.Resolve(dev2Catalog));
        Assert.Equal([SelfSpellId, OtherSpellId], magic.SentSpellIds);
    }

    // -- idle mana top-up once the queue empties ---------------------------------------------------

    [Fact]
    public void QueueEmptyingTopsUpManaToTheHighMarkInsteadOfIdlingPartFull()
    {
        const uint ManaSpellFamily = 900;
        const uint ManaSpellId = 901;

        var character = new FakeCharacter { CurrentMana = 60 }; // below the 160 (of 200) high mark
        var queue = new RequestQueue(5);
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);

        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other"],
            [DefaultSpellSets.ManaUpkeep] = DefaultSpellSets.Table[DefaultSpellSets.ManaUpkeep],
        };
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, spellSets, character: character);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: OtherSpellId, Name: "Strength Other I", Family: OtherFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: ManaSpellId, Name: "Stamina to Mana Self I", Family: ManaSpellFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        List<string> replies = [];
        void Pump(IReadOnlyList<PluginChatMessage> messages) => coordinator.Pump(
            0, catalog, activeEnchantments: [], messages, selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Pump([]); // dequeues and serves Archer, the only request
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump([Confirm("Strength Other I", RequesterName)]); // closes Archer's chain; queue empties

        magic.IsCasting = false; // the server's own use-done for Archer's just-confirmed cast
        BuffBotStatus status = coordinator.Pump(
            0, catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Assert.Equal([OtherSpellId, ManaSpellId], magic.SentSpellIds);
        Assert.True(magic.LastRequestWasSelfTargeted);
        Assert.Equal(BotActivity.ToppingUp, status.Activity);
    }

    /// <summary>Bounded, by design: a caster that can never reach the high mark (nothing learned to
    /// bounce with here) does not retrigger the top-up every idle tick forever.</summary>
    [Fact]
    public void QueueEmptyingNeverRetriggersTheIdleTopUpOnceOneAttemptHasBeenMade()
    {
        var character = new FakeCharacter { CurrentMana = 10 }; // below the mark, nothing learned to raise it
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, NoSelfSpellSets(), character: character);
        List<string> replies = [];

        void Pump() => coordinator.Pump(
            0, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Pump(); // nothing queued, nothing due, mana below the mark but nothing learned to bounce with
        int sentAfterFirstIdleTick = magic.SentSpellIds.Count;
        Pump(); // latched: does not try again every tick
        Pump();

        Assert.Equal(sentAfterFirstIdleTick, magic.SentSpellIds.Count);
    }

    // -- missing-components backoff: trace lines (`cast <line> -> <result>`) tell a genuine
    // retry apart from a backoff that declined to start one. --

    [Fact]
    public void AnIdleSelfBuffRunEndingMissingComponentsDoesNotRetryOnLaterIdleTicks()
    {
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        magic.MissingComponentsFor.Add(SelfSpellId);
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        List<string> traceLines = [];
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, trace: traceLines.Add);

        void Pump(double deltaSeconds) => coordinator.Pump(
            deltaSeconds, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, _) => { });

        Pump(59);
        Pump(2); // crosses the 60 s interval - attempts Focus Self, which fails MissingComponents

        Assert.Empty(magic.SentSpellIds);
        Assert.Contains(traceLines, l => l.Contains("cast Focus Self -> MissingComponents"));
        Assert.Contains(traceLines, l => l.Contains("idle backoff: missing components"));

        int attemptsAfterFirstFailure = traceLines.Count(l => l.StartsWith("cast Focus Self"));

        // Two more idle intervals pass. Before the fix, each one retried (and failed) the same
        // way, flipping into magic mode and back every time; the backoff must refuse to even try.
        Pump(60);
        Pump(60);

        Assert.Equal(attemptsAfterFirstFailure, traceLines.Count(l => l.StartsWith("cast Focus Self")));
    }

    [Fact]
    public void TheBackoffClearsOnceTheReagentStockReportedHasChanged()
    {
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        magic.MissingComponentsFor.Add(SelfSpellId);
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        List<string> traceLines = [];
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, trace: traceLines.Add);

        var emptyStock = new ComponentReport(
            true, [new ComponentUsage(WeenieClassId: 1, Name: "Sulfurous Ash", Stock: 0, UsedBy: 1)]);
        var refilledStock = new ComponentReport(
            true, [new ComponentUsage(WeenieClassId: 1, Name: "Sulfurous Ash", Stock: 5, UsedBy: 1)]);

        void Pump(double deltaSeconds, ComponentReport? report) => coordinator.Pump(
            deltaSeconds, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, _) => { },
            components: report);

        Pump(59, emptyStock);
        Pump(2, emptyStock); // crosses the interval, fails MissingComponents, backoff starts at empty stock

        Assert.Empty(magic.SentSpellIds);

        magic.MissingComponentsFor.Remove(SelfSpellId); // as if the reagent were bought back

        Pump(60, emptyStock); // same stock as the backoff's own baseline - must not retry yet
        Assert.Empty(magic.SentSpellIds);

        Pump(60, refilledStock); // the stock differs now - the backoff clears and this same idle
                                  // tick's own interval crossing is free to begin a run again
        Assert.Contains(traceLines, l => l.Contains("idle backoff cleared: the reagent stock changed"));

        Pump(0, refilledStock); // magic mode's own request/confirm split (unrelated to the
                                 // backoff) needs a second tick before the cast itself goes out
        Assert.Equal([SelfSpellId], magic.SentSpellIds);
    }

    [Fact]
    public void TheBackoffClearsWhenThisCharacterIsReenabled()
    {
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        magic.MissingComponentsFor.Add(SelfSpellId);
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        List<string> traceLines = [];
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, trace: traceLines.Add);

        void Pump(double deltaSeconds) => coordinator.Pump(
            deltaSeconds, Catalog, activeEnchantments: [], [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, _) => { });

        Pump(59);
        Pump(2); // backoff starts

        magic.MissingComponentsFor.Remove(SelfSpellId); // as if a fresh reagent stack were bought
        coordinator.ClearComponentBackoffForReenable(); // BuffBotPlugin calls this on /buffbot on
        Assert.Contains(traceLines, l => l.Contains("idle backoff cleared: this character was re-enabled"));

        Pump(60); // the next idle interval is free to begin a run again
        Pump(0); // magic mode's own request/confirm split needs a second tick before the cast
        Assert.Equal([SelfSpellId], magic.SentSpellIds);
    }

    [Fact]
    public void TheBackoffClearsOnceARealRequestLandsACastRegardlessOfTheBackoff()
    {
        const uint ManaSpellFamily = 900;
        const uint ManaSpellId = 901;

        var character = new FakeCharacter { CurrentMana = 60 };
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        magic.MissingComponentsFor.Add(ManaSpellId);
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        var spellSets = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = Array.Empty<string>(),
            [DefaultSpellSets.Buff] = ["Strength Other"],
            [DefaultSpellSets.ManaUpkeep] = DefaultSpellSets.Table[DefaultSpellSets.ManaUpkeep],
        };
        List<string> traceLines = [];
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, spellSets, trace: traceLines.Add,
            character: character);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: OtherSpellId, Name: "Strength Other I", Family: OtherFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: true),
            new(
                SpellId: ManaSpellId, Name: "Stamina to Mana Self I", Family: ManaSpellFamily, Tier: 1,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        List<string> replies = [];
        void Pump(IReadOnlyList<PluginChatMessage> messages) => coordinator.Pump(
            0, catalog, activeEnchantments: [], messages, selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text));

        Pump([]); // nothing queued - idle top-up attempts, fails MissingComponents, backoff starts
        Assert.Empty(magic.SentSpellIds);
        Assert.Contains(traceLines, l => l.Contains("idle backoff: missing components"));

        // A real request arrives and lands its own cast, unaffected by the backoff (first ever
        // request, so no top-up-before-next delays it either).
        queue.TryEnqueue(new BuffRequest(RequesterId, RequesterName, DefaultSpellSets.Buff), out _);
        Pump([]);
        Assert.Equal([OtherSpellId], magic.SentSpellIds);

        magic.LastCompletion =
            new PluginCastCompletion(Revision: 2, SpellId: OtherSpellId, TargetObjectId: RequesterId, WeenieError: 0);
        Pump([Confirm("Strength Other I", RequesterName)]); // lands the cast and closes the run

        Assert.Contains(traceLines, l => l.Contains("idle backoff cleared: a request landed a cast"));

        magic.IsCasting = false; // the server's own use-done for the just-confirmed cast
        int attemptsBeforeRetry = traceLines.Count(l => l.StartsWith("cast Stamina to Mana Self"));
        Pump([]);
        Assert.True(traceLines.Count(l => l.StartsWith("cast Stamina to Mana Self")) > attemptsBeforeRetry);
    }

    // -- component ceiling ---------------------------------------------------------------------

    [Fact]
    public void TheComponentCeilingPersistsAcrossRunsAndClearsWhenTheStockReportChanges()
    {
        const uint TopSelfSpellId = 98;
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        magic.MissingComponentsFor.Add(TopSelfSpellId);
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        List<string> traceLines = [];
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, trace: traceLines.Add);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: TopSelfSpellId, Name: "Focus Self VII", Family: SelfFamily, Tier: 7,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 10f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
            new(
                SpellId: SelfSpellId, Name: "Focus Self VI", Family: SelfFamily, Tier: 6,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 10f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        var emptyStock = new ComponentReport(
            true, [new ComponentUsage(WeenieClassId: 1, Name: "Sulfurous Ash", Stock: 0, UsedBy: 1)]);
        var refilledStock = new ComponentReport(
            true, [new ComponentUsage(WeenieClassId: 1, Name: "Sulfurous Ash", Stock: 5, UsedBy: 1)]);

        void Pump(double deltaSeconds, IReadOnlyList<PluginChatMessage> messages, ComponentReport? report) =>
            coordinator.Pump(
                deltaSeconds, catalog, activeEnchantments: [], messages, selfBuffingEnabled: true,
                distanceToRequester: static _ => 10d,
                sendReply: (_, _, _) => { },
                components: report);

        // Run 1: the idle self-buff reaches the top tier, is refused for components, retries the
        // next learned tier down at no cost, and lands it.
        Pump(59, [], emptyStock);
        Pump(2, [], emptyStock);
        Assert.Equal([SelfSpellId], magic.SentSpellIds);
        Assert.Contains(traceLines, l => l.Contains("cast Focus Self -> MissingComponents"));

        magic.LastCompletion =
            new PluginCastCompletion(Revision: 1, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(0.1, [SelfConfirm("Focus Self VI")], emptyStock);
        magic.IsCasting = false; // the server's own use-done for the just-confirmed cast

        int missingComponentAttemptsSoFar =
            traceLines.Count(l => l.Contains("cast Focus Self -> MissingComponents"));

        // Run 2, a fresh idle interval: starts already capped by the ceiling — the top tier is
        // never even attempted again.
        Pump(60, [], emptyStock);
        Assert.Equal([SelfSpellId, SelfSpellId], magic.SentSpellIds);
        Assert.Equal(
            missingComponentAttemptsSoFar,
            traceLines.Count(l => l.Contains("cast Focus Self -> MissingComponents")));

        magic.LastCompletion =
            new PluginCastCompletion(Revision: 2, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(0.1, [SelfConfirm("Focus Self VI")], emptyStock);
        magic.IsCasting = false; // the server's own use-done for the just-confirmed cast

        // The reagent stock changes: the ceiling clears, and the top tier is willing to try again.
        magic.MissingComponentsFor.Remove(TopSelfSpellId); // as if the reagent were bought back
        Pump(60, [], refilledStock);

        Assert.Contains(traceLines, l => l.Contains("component ceiling cleared: the reagent stock changed"));
        Assert.Equal([SelfSpellId, SelfSpellId, TopSelfSpellId], magic.SentSpellIds);
    }

    [Fact]
    public void TheComponentCeilingClearsWhenThisCharacterIsReenabled()
    {
        const uint TopSelfSpellId = 98;
        var queue = new RequestQueue(5);
        var magic = new FakeMagic();
        magic.MissingComponentsFor.Add(TopSelfSpellId);
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();
        List<string> traceLines = [];
        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, trace: traceLines.Add);

        List<PluginSpellInfo> catalog =
        [
            new(
                SpellId: TopSelfSpellId, Name: "Focus Self VII", Family: SelfFamily, Tier: 7,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 10f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
            new(
                SpellId: SelfSpellId, Name: "Focus Self VI", Family: SelfFamily, Tier: 6,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 10f, School: 0, Description: string.Empty,
                IsSelfTargeted: true, IsBeneficial: true),
        ];

        void Pump(double deltaSeconds, IReadOnlyList<PluginChatMessage>? messages = null) => coordinator.Pump(
            deltaSeconds, catalog, activeEnchantments: [], messages ?? [], selfBuffingEnabled: true,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, _) => { });

        Pump(59);
        Pump(2); // top tier refused, retried and landed one rung down - the ceiling is set

        magic.MissingComponentsFor.Remove(TopSelfSpellId); // as if a fresh reagent stack were bought
        coordinator.ClearComponentBackoffForReenable(); // BuffBotPlugin calls this on /buffbot on
        Assert.Contains(traceLines, l => l.Contains("component ceiling cleared: this character was re-enabled"));

        magic.LastCompletion =
            new PluginCastCompletion(Revision: 1, SpellId: SelfSpellId, TargetObjectId: 0, WeenieError: 0);
        Pump(0.1, [SelfConfirm("Focus Self VI")]); // lands the run already in flight
        magic.IsCasting = false;

        Pump(60); // the next idle interval is free to try the top tier again
        Assert.Equal([SelfSpellId, TopSelfSpellId], magic.SentSpellIds);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static (BuffCoordinator Coordinator, FakeMagic Magic, List<string> Replies) NewCoordinator(
        RequestQueue queue,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? spellSets = null,
        int? targetTier = null,
        BotStats? stats = null)
    {
        var magic = new FakeMagic();
        var items = new FakeItems();
        items.Add(Wand(WandObjectId));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var enchantments = new FakeEnchantments();

        spellSets ??= new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            [DefaultSpellSets.Self] = DefaultSpellSets.Table[DefaultSpellSets.Self],
            [DefaultSpellSets.Buff] = ["Strength Other"],
        };

        var coordinator = new BuffCoordinator(
            queue, magic, enchantments, items, equipment, combat, spellSets, targetTier: targetTier,
            stats: stats);
        return (coordinator, magic, []);
    }

    private static BuffBotStatus Pump(
        BuffCoordinator coordinator,
        IReadOnlyList<PluginChatMessage> chatMessages,
        bool enabled,
        List<string> replies,
        double deltaSeconds = 0,
        IReadOnlyList<PluginSpellInfo>? catalog = null,
        IReadOnlyList<PluginWorldObject>? capturedObjects = null,
        bool tradeOpen = false) =>
        coordinator.Pump(
            deltaSeconds,
            catalog ?? Catalog,
            activeEnchantments: [],
            chatMessages,
            enabled,
            distanceToRequester: static _ => 10d,
            sendReply: (_, _, text) => replies.Add(text),
            capturedObjects: capturedObjects,
            tradeOpen: tradeOpen);

    private static readonly PluginSpellInfo[] Catalog =
    [
        new(
            SpellId: SelfSpellId,
            Name: "Focus Self VI",
            Family: SelfFamily,
            Tier: 6,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 1200f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: true,
            IsBeneficial: true),
        new(
            SpellId: OtherSpellId,
            Name: "Strength Other I",
            Family: OtherFamily,
            Tier: 1,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 120f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: false,
            IsBeneficial: true),
    ];

    private static readonly PluginSpellInfo[] CatalogWithABane =
    [
        .. Catalog,
        new(
            SpellId: AcidBaneSpellId,
            Name: "Acid Bane I",
            Family: AcidBaneFamily,
            Tier: 1,
            Difficulty: 0,
            ManaCost: 0,
            DurationSeconds: 120f,
            School: 0,
            Description: string.Empty,
            IsSelfTargeted: false,
            IsBeneficial: true),
    ];

    private static PluginChatMessage Confirm(string spellName, string targetName) =>
        new(1, 0, (int)ChatKind.System, "", $"You cast {spellName} on {targetName}", "");

    private static PluginChatMessage SelfConfirm(string spellName) =>
        new(1, 0, (int)ChatKind.System, "", $"You cast {spellName} on yourself", "");

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

    private sealed class FakeMagic : IMagicCommands
    {
        private readonly List<uint> _sent = [];

        internal IReadOnlyList<uint> SentSpellIds => _sent;
        internal bool LastRequestWasSelfTargeted { get; private set; }
        internal uint LastRequestTargetObjectId { get; private set; }

        internal HashSet<uint> RefuseGateFor { get; } = [];

        internal HashSet<uint> MissingComponentsFor { get; } = [];

        public bool IsCasting { get; set; }
        public PluginCastCompletion LastCompletion { get; set; }

        public PluginCastGate EvaluateGate(uint spellId) => PluginCastGate.Ready;

        public bool Cast(uint spellId) => false;

        public PluginCastGate EvaluateGate(uint spellId, uint targetObjectId) =>
            RefuseGateFor.Contains(spellId) ? PluginCastGate.NotKnown : PluginCastGate.Ready;

        public bool Cast(uint spellId, uint targetObjectId) => false;

        public PluginCastRequestResult RequestCast(uint spellId)
        {
            if (MissingComponentsFor.Contains(spellId))
                return PluginCastRequestResult.MissingComponents;

            LastRequestWasSelfTargeted = true;
            _sent.Add(spellId);
            IsCasting = true;
            return PluginCastRequestResult.Sent;
        }

        public PluginCastRequestResult RequestCast(uint spellId, uint targetObjectId)
        {
            if (MissingComponentsFor.Contains(spellId))
                return PluginCastRequestResult.MissingComponents;

            LastRequestWasSelfTargeted = false;
            LastRequestTargetObjectId = targetObjectId;
            _sent.Add(spellId);
            IsCasting = true;
            return PluginCastRequestResult.Sent;
        }
    }

    /// <summary>Mirrors OpenAC dev.2 after 8b2c5147 (<c>RuntimeAutomationSurface.RebuildSpellbook</c>'s
    /// added <c>CanTargetSelf</c> check): <see cref="ISpellCatalog.KnownSelfBuffs"/> holds only the
    /// self-targeted spell in <see cref="Catalog"/>; the requester's own "Strength Other" line is
    /// just as genuinely learned, but only <see cref="ISpellCatalog.All"/> plus <see
    /// cref="ISpellCatalog.IsKnown"/> say so.</summary>
    private sealed class Dev2StyleCatalog : ISpellCatalog
    {
        public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; } = [Catalog[0]];

        public IReadOnlyList<PluginSpellInfo> All => Catalog;

        public bool IsKnown(uint spellId) => spellId == SelfSpellId || spellId == OtherSpellId;

        public bool TryGet(uint spellId, out PluginSpellInfo info)
        {
            foreach (PluginSpellInfo spell in Catalog)
            {
                if (spell.SpellId != spellId)
                    continue;
                info = spell;
                return true;
            }
            info = default;
            return false;
        }
    }

    private sealed class FakeEnchantments : IEnchantmentAutomation
    {
        internal uint LastTarget { get; private set; }

        public IReadOnlyList<PluginTrackedEnchantment> Capture(uint targetObjectId) =>
            Array.Empty<PluginTrackedEnchantment>();

        public bool ReportCast(uint targetObjectId, uint spellId, double durationSeconds)
        {
            LastTarget = targetObjectId;
            return true;
        }
    }

    /// <summary>Mana-upkeep milestone: mutable mana, unlike the interface's server-fed default.</summary>
    private sealed class FakeCharacter : ICharacterInfo
    {
        public bool IsInWorld => true;
        public uint ObjectId => 1;
        public uint CurrentHealth => 100;
        public uint MaxHealth => 100;
        public uint CurrentStamina { get; set; } = 100;
        public uint MaxStamina => 100;
        public uint CurrentMana { get; set; }
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
        private readonly FakeItems _items;

        internal FakeEquipment(FakeItems items) => _items = items;

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
