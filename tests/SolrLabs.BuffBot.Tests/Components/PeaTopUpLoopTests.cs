using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary>Host-free coverage of <see cref="PeaTopUpLoop"/>: a stale report must never restart
/// a split, and the fresh-sample gate applies the same way to a failed split as a confirmed one.</summary>
public sealed class PeaTopUpLoopTests
{
    private const uint LeadScarabWeenie = 691u; // has a recipe (PeaSplitTable)
    private const uint LeadPeaWeenie = 8329u;
    private const uint GoldScarabWeenie = 687u; // has a recipe, distinct reagent for the cap test
    private const uint ToolId = ContributionAdvisor.SplittingToolWeenieClassId;

    private sealed class FakePeaSplitCoordinator : IPeaSplitCoordinator
    {
        internal bool NextTryStartResult { get; set; } = true;
        internal Queue<PeaSplitPollResult> PollResults { get; } = new();
        internal int TryStartCallCount { get; private set; }
        internal uint? LastComponentWeenieId { get; private set; }

        public bool TryStart(uint componentWeenieId, string reason)
        {
            TryStartCallCount++;
            LastComponentWeenieId = componentWeenieId;
            return NextTryStartResult;
        }

        public PeaSplitPollResult Poll(double deltaSeconds) =>
            PollResults.Count > 0 ? PollResults.Dequeue() : PeaSplitPollResult.Idle;
    }

    private static ComponentReport Report(uint weenieClassId, string name, int stock, int usedBy = 1) =>
        new(Available: true, Items: [new ComponentUsage(weenieClassId, name, stock, usedBy)]);

    private static (PeaTopUpLoop Loop, FakePeaSplitCoordinator Splitter, List<string> Warnings,
        int ResampleRequests) Build(int maxTopUpsPerReagent = 3, double windowSeconds = 600)
    {
        var splitter = new FakePeaSplitCoordinator();
        var warnings = new List<string>();
        int resampleRequests = 0;
        var loop = new PeaTopUpLoop(
            splitter, warnings.Add, () => resampleRequests++, maxTopUpsPerReagent, windowSeconds);
        return (loop, splitter, warnings, resampleRequests);
    }

    // -- A stale report must not restart a split -------------------------------------------------

    [Fact]
    public void AConfirmedSplitFromAStaleReportDoesNotStartAnotherUntilAFreshSampleArrives()
    {
        (PeaTopUpLoop loop, FakePeaSplitCoordinator splitter, List<string> warnings, _) = Build();
        ComponentReport stale = Report(LeadScarabWeenie, "Lead Scarab", stock: 1, usedBy: 1);

        // Tick 1: idle, low stock -> starts.
        loop.Pump(deltaSeconds: 1, stale, componentsGeneration: 1, lowStockThreshold: 25);
        Assert.Equal(1, splitter.TryStartCallCount);

        // Tick 2: the split resolves (Confirmed), still generation 1 -- the same tick's report is
        // the one that was already stale enough to let this split start in the first place.
        splitter.PollResults.Enqueue(PeaSplitPollResult.Confirmed);
        loop.Pump(deltaSeconds: 1, stale, componentsGeneration: 1, lowStockThreshold: 25);
        Assert.Equal(1, splitter.TryStartCallCount); // no start attempted on the resolving tick

        // Tick 3: splitter reports idle again, but the report handed in is still generation 1 —
        // the exact stale-report shape that split ten times live. Must not start again.
        loop.Pump(deltaSeconds: 1, stale, componentsGeneration: 1, lowStockThreshold: 25);
        Assert.Equal(1, splitter.TryStartCallCount);

        // Tick 4: a genuinely fresh sample (generation 2) shows the split actually worked and
        // stock is now well above the mark -- nothing left to top up, still no further start.
        ComponentReport fresh = Report(LeadScarabWeenie, "Lead Scarab", stock: 51, usedBy: 1);
        loop.Pump(deltaSeconds: 1, fresh, componentsGeneration: 2, lowStockThreshold: 25);
        Assert.Equal(1, splitter.TryStartCallCount);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AFreshSampleStillBelowTheMarkStartsExactlyOneMoreSplit()
    {
        (PeaTopUpLoop loop, FakePeaSplitCoordinator splitter, _, _) = Build();
        // Scarab case: yield 20, mark 25, stock 10 -> 30 after one split lands.
        ComponentReport stale = Report(LeadScarabWeenie, "Lead Scarab", stock: 10, usedBy: 1);

        loop.Pump(deltaSeconds: 1, stale, componentsGeneration: 1, lowStockThreshold: 25);
        Assert.Equal(1, splitter.TryStartCallCount);

        splitter.PollResults.Enqueue(PeaSplitPollResult.Confirmed);
        loop.Pump(deltaSeconds: 1, stale, componentsGeneration: 1, lowStockThreshold: 25);
        loop.Pump(deltaSeconds: 1, stale, componentsGeneration: 1, lowStockThreshold: 25); // still stale
        Assert.Equal(1, splitter.TryStartCallCount);

        ComponentReport fresh = Report(LeadScarabWeenie, "Lead Scarab", stock: 30, usedBy: 1);
        loop.Pump(deltaSeconds: 1, fresh, componentsGeneration: 2, lowStockThreshold: 25);
        Assert.Equal(1, splitter.TryStartCallCount); // 30 >= 25: nothing left to top up
    }

    // -- Belt-and-braces session cap -------------------------------------------------------------

    /// <summary>Three starts inside the window, then a single warning and no
    /// more, until the window itself expires.</summary>
    [Fact]
    public void ARepeatedlyStaleReagentStopsAtTheCapAndWarnsOnce()
    {
        (PeaTopUpLoop loop, FakePeaSplitCoordinator splitter, List<string> warnings, _) =
            Build(maxTopUpsPerReagent: 3, windowSeconds: 100);
        ComponentReport neverRises = Report(LeadScarabWeenie, "Lead Scarab", stock: 1, usedBy: 1);

        // Three start/resolve pairs, each handed a fresh-enough generation to clear the loop's own
        // stale-report gate — proving the cap, not the gate, is what stops the fourth.
        for (int generation = 1; generation <= 3; generation++)
        {
            loop.Pump(deltaSeconds: 1, neverRises, generation, lowStockThreshold: 25);
            splitter.PollResults.Enqueue(PeaSplitPollResult.Confirmed);
            loop.Pump(deltaSeconds: 1, neverRises, generation, lowStockThreshold: 25);
        }
        Assert.Equal(3, splitter.TryStartCallCount);

        // Several more fresh-generation ticks at the cap: still no further starts, one warning.
        for (int generation = 4; generation < 9; generation++)
            loop.Pump(deltaSeconds: 1, neverRises, generation, lowStockThreshold: 25);

        Assert.Equal(3, splitter.TryStartCallCount);
        Assert.Single(warnings);
        Assert.Contains("[split]", warnings[0]);
        Assert.Contains("cap", warnings[0]);

        // The window expiring (a lot of accumulated tick time, no wall clock) frees it back up.
        loop.Pump(deltaSeconds: 1000, neverRises, componentsGeneration: 9, lowStockThreshold: 25);
        Assert.Equal(4, splitter.TryStartCallCount);
    }

    /// <summary>The cap is per reagent: a second reagent below its own mark keeps topping up even
    /// while the first sits capped for the rest of the window.</summary>
    [Fact]
    public void TheCapNeverBlocksADifferentReagent()
    {
        (PeaTopUpLoop loop, FakePeaSplitCoordinator splitter, _, _) =
            Build(maxTopUpsPerReagent: 1, windowSeconds: 100);
        ComponentReport bothLow = new(
            Available: true,
            Items:
            [
                new ComponentUsage(LeadScarabWeenie, "Lead Scarab", Stock: 1, UsedBy: 1),
                new ComponentUsage(GoldScarabWeenie, "Gold Scarab", Stock: 5, UsedBy: 1),
            ]);

        // Lead is worse-stocked, so it is picked and immediately caps out (max 1).
        loop.Pump(deltaSeconds: 1, bothLow, componentsGeneration: 1, lowStockThreshold: 25);
        Assert.Equal(LeadScarabWeenie, splitter.LastComponentWeenieId);
        Assert.Equal(1, splitter.TryStartCallCount);

        splitter.PollResults.Enqueue(PeaSplitPollResult.Confirmed);
        loop.Pump(deltaSeconds: 1, bothLow, componentsGeneration: 1, lowStockThreshold: 25);
        Assert.Equal(1, splitter.TryStartCallCount);

        // Fresh sample: Lead is now excluded (capped) -- Gold, still below its own mark, is
        // picked instead.
        loop.Pump(deltaSeconds: 1, bothLow, componentsGeneration: 2, lowStockThreshold: 25);
        Assert.Equal(2, splitter.TryStartCallCount);
        Assert.Equal(GoldScarabWeenie, splitter.LastComponentWeenieId);
    }

    // -- Failed resolution: the same fresh-sample gate, plus the real splitter's own disable ----

    [Fact]
    public void AFailedResolutionWaitsForAFreshSampleAndTheSplittersOwnSessionDisableStillHolds()
    {
        var items = new FakeItems();
        items.Add(500_001u, ToolId, "Splitting Tool", 1);
        items.Add(500_002u, LeadPeaWeenie, "Lead Pea", 5);
        items.BusyOverride = true; // never clears -> the busy bound fails the split

        var combat = new FakeCombat();
        var warnings = new List<string>();
        var splitter = new PeaSplitter(
            items, combat, trace: _ => { }, warn: warnings.Add, busyTimeoutSeconds: 2);
        int resampleRequests = 0;
        var loop = new PeaTopUpLoop(splitter, warnings.Add, () => resampleRequests++);

        ComponentReport report = Report(LeadScarabWeenie, "Lead Scarab", stock: 1, usedBy: 1);

        loop.Pump(deltaSeconds: 1, report, componentsGeneration: 1, lowStockThreshold: 25); // starts
        Assert.False(splitter.IsIdle);

        loop.Pump(deltaSeconds: 1, report, componentsGeneration: 1, lowStockThreshold: 25); // 1 s busy
        Assert.False(splitter.SessionDisabled);

        loop.Pump(deltaSeconds: 1, report, componentsGeneration: 1, lowStockThreshold: 25); // 2 s: fails
        Assert.True(splitter.SessionDisabled);
        Assert.Equal(1, resampleRequests);

        // Same stale generation again: refused before ever reaching the splitter's own disable
        // check, exactly like the Confirmed case.
        loop.Pump(deltaSeconds: 1, report, componentsGeneration: 1, lowStockThreshold: 25);

        // A fresh sample arrives (generation 2): the loop's own gate now lets a start attempt
        // through, but the real splitter refuses it anyway -- SessionDisabled outlives any report.
        loop.Pump(deltaSeconds: 1, report, componentsGeneration: 2, lowStockThreshold: 25);
        Assert.True(splitter.SessionDisabled);
        Assert.Equal(0, items.ApplyCallCount); // never once applied -- failed on the busy bound
    }

    private sealed class FakeItems : IItemAutomation
    {
        private readonly List<PluginInventoryItem> _items = [];

        internal void Add(uint objectId, uint weenieClassId, string name, int stackSize) =>
            _items.Add(new PluginInventoryItem(
                objectId, weenieClassId, name, ItemType: 0, ContainerObjectId: 1, WielderObjectId: 0,
                ValidLocations: 0, EquippedLocation: 0, Useability: 0, TargetType: 0, PublicFlags: 0,
                StackSize: stackSize, Structure: 0, MaximumStructure: 0, SpellId: 0, PetClass: 0,
                SummoningMastery: 0, ProcSpellId: 0, ProcSpellSelfTargeted: false, ProcSpellRate: 0,
                WeaponSkill: 0, DamageType: 0, Damage: 0, DamageVariance: 0, UseRequiresSkill: 0,
                UseRequiresSkillLevel: 0, UseRequiresSkillSpecialized: 0));

        internal bool BusyOverride { get; set; }
        public bool IsBusy => BusyOverride;

        internal int ApplyCallCount { get; private set; }

        public IReadOnlyList<PluginInventoryItem> CaptureOwnedItems() => _items;

        public PluginItemCommandResult Apply(uint objectId, uint targetObjectId)
        {
            ApplyCallCount++;
            return new PluginItemCommandResult(PluginItemCommandStatus.Started);
        }
    }

    private sealed class FakeCombat : ICombatAutomation
    {
        public PluginCombatSnapshot Snapshot =>
            new(0u, PluginCombatMode.Peace, PluginAttackHeight.Medium, 0f, 0f, false, false, false, false);

        public IReadOnlyList<PluginCombatTarget> CaptureHostileTargets(float maximumDistance) =>
            Array.Empty<PluginCombatTarget>();

        public PluginCombatCommandResult EnterDefaultMode() => new(PluginCombatCommandStatus.Unavailable);

        // Snapshot.Mode above is always Peace, so a request for it is always the
        // client-side no-op AlreadyReady, never a settle wait PeaSplitter doesn't need.
        public PluginCombatCommandResult EnterMode(PluginCombatMode mode) =>
            mode == PluginCombatMode.Peace
                ? new(PluginCombatCommandStatus.AlreadyReady)
                : new(PluginCombatCommandStatus.ModeChangeSent);

        public PluginCombatCommandResult BeginPhysicalAttack(
            uint targetObjectId, PluginAttackHeight height, float power) =>
            new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult ReleasePhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
        public PluginCombatCommandResult AbortPhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
    }
}
