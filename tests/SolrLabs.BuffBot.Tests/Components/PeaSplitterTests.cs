using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary>Host-free coverage of <see cref="PeaSplitter"/>. Small poll bounds throughout
/// (never the class' own defaults), so a bounded wait is proven in a handful of calls.</summary>
public sealed class PeaSplitterTests
{
    private const uint ToolId = ContributionAdvisor.SplittingToolWeenieClassId; // 8283
    private const uint LeadPeaWeenie = 8329u;
    private const uint LeadScarabWeenie = 691u;

    private static PluginInventoryItem Item(uint objectId, uint weenieClassId, string name, int stackSize) =>
        new(
            objectId, weenieClassId, name, ItemType: 0, ContainerObjectId: 1, WielderObjectId: 0,
            ValidLocations: 0, EquippedLocation: 0, Useability: 0, TargetType: 0, PublicFlags: 0,
            StackSize: stackSize, Structure: 0, MaximumStructure: 0, SpellId: 0, PetClass: 0,
            SummoningMastery: 0, ProcSpellId: 0, ProcSpellSelfTargeted: false, ProcSpellRate: 0,
            WeaponSkill: 0, DamageType: 0, Damage: 0, DamageVariance: 0, UseRequiresSkill: 0,
            UseRequiresSkillLevel: 0, UseRequiresSkillSpecialized: 0);

    private sealed class FakeItems : IItemAutomation
    {
        private readonly List<PluginInventoryItem> _items = [];

        internal void Add(PluginInventoryItem item) => _items.Add(item);

        internal void AddStock(uint weenieClassId, string name, int amount)
        {
            int index = _items.FindIndex(i => i.WeenieClassId == weenieClassId);
            if (index >= 0)
                _items[index] = _items[index] with { StackSize = _items[index].StackSize + amount };
            else
                _items.Add(Item(objectId: 500_000u + weenieClassId, weenieClassId, name, amount));
        }

        internal void ShrinkOrRemove(uint objectId, int by)
        {
            int index = _items.FindIndex(i => i.ObjectId == objectId);
            if (index < 0)
                return;
            int remaining = _items[index].StackSize - by;
            if (remaining <= 0)
                _items.RemoveAt(index);
            else
                _items[index] = _items[index] with { StackSize = remaining };
        }

        internal bool BusyOverride { get; set; }
        public bool IsBusy => BusyOverride;

        internal PluginItemCommandResult NextApplyResult { get; set; } = new(PluginItemCommandStatus.Started);
        internal int ApplyCallCount { get; private set; }
        internal uint LastApplyToolObjectId { get; private set; }
        internal uint LastApplyTargetObjectId { get; private set; }

        /// <summary>Simulates the server's own effect of a successful Apply — never assumed
        /// to consume exactly one pea.</summary>
        internal Action<FakeItems>? OnApplyAccepted { get; set; }

        /// <summary>Never set by <see cref="Apply"/> itself; a test drives it directly to
        /// simulate the server's answer landing at whatever tick it chooses.</summary>
        public PluginItemUseCompletion LastCompletion { get; set; }

        public IReadOnlyList<PluginInventoryItem> CaptureOwnedItems() => _items;

        public PluginItemCommandResult Apply(uint objectId, uint targetObjectId)
        {
            ApplyCallCount++;
            LastApplyToolObjectId = objectId;
            LastApplyTargetObjectId = targetObjectId;
            if (NextApplyResult.Accepted)
                OnApplyAccepted?.Invoke(this);
            return NextApplyResult;
        }
    }

    private sealed class FakeCombat : ICombatAutomation
    {
        internal PluginCombatMode Mode { get; set; } = PluginCombatMode.Peace;
        internal PluginCombatCommandResult NextEnterModeResult { get; set; } =
            new(PluginCombatCommandStatus.ModeChangeSent);

        /// <summary>Unlike retail, where an accepted request flips the tracked mode at once,
        /// this lets a test exercise the settle-wait-then-check path without that guarantee.</summary>
        internal bool ModeNeverSettles { get; set; }

        internal int EnterModeCallCount { get; private set; }

        public PluginCombatSnapshot Snapshot =>
            new(0u, Mode, PluginAttackHeight.Medium, 0f, 0f, false, false, false, false);

        public IReadOnlyList<PluginCombatTarget> CaptureHostileTargets(float maximumDistance) =>
            Array.Empty<PluginCombatTarget>();

        public PluginCombatCommandResult EnterDefaultMode() => new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult EnterMode(PluginCombatMode mode)
        {
            EnterModeCallCount++;
            // Retail short-circuits to AlreadyReady, without sending anything, once the
            // tracked mode already equals what was asked for.
            if (Mode == mode)
                return new(PluginCombatCommandStatus.AlreadyReady);

            if (NextEnterModeResult.Accepted && !ModeNeverSettles)
                Mode = mode;
            return NextEnterModeResult;
        }

        public PluginCombatCommandResult BeginPhysicalAttack(
            uint targetObjectId, PluginAttackHeight height, float power) =>
            new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult ReleasePhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
        public PluginCombatCommandResult AbortPhysicalAttack() => new(PluginCombatCommandStatus.Unavailable);
    }

    private static (PeaSplitter Splitter, FakeItems Items, FakeCombat Combat, List<string> Traces,
        List<string> Warnings) Build(
        double stanceSettleSeconds = 3, double busyTimeoutSeconds = 3, double confirmTimeoutSeconds = 3)
    {
        var items = new FakeItems();
        var combat = new FakeCombat();
        var traces = new List<string>();
        var warnings = new List<string>();
        var splitter = new PeaSplitter(
            items, combat, traces.Add, warnings.Add,
            stanceSettleSeconds: stanceSettleSeconds, busyTimeoutSeconds: busyTimeoutSeconds,
            confirmTimeoutSeconds: confirmTimeoutSeconds);
        return (splitter, items, combat, traces, warnings);
    }

    // -- TryStart selection -------------------------------------------------------------------

    [Fact]
    public void NoRecipeForTheComponentRefusesToStart()
    {
        (PeaSplitter splitter, FakeItems items, _, _, _) = Build();
        items.Add(Item(1, ToolId, "Splitting Tool", 1));

        Assert.False(splitter.TryStart(componentWeenieId: 999_999u, "test"));
        Assert.True(splitter.IsIdle);
    }

    [Fact]
    public void NoSplittingToolRefusesAndWarnsOncePerSession()
    {
        (PeaSplitter splitter, FakeItems items, _, _, List<string> warnings) = Build();
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        Assert.False(splitter.TryStart(LeadScarabWeenie, "test"));
        Assert.False(splitter.TryStart(LeadScarabWeenie, "test again"));

        Assert.Single(warnings); // once per session, not once per refused attempt
        Assert.Contains("[split]", warnings[0]);
        Assert.Contains("Splitting Tool", warnings[0]);
    }

    [Fact]
    public void NoHeldPeaRefusesSilently()
    {
        (PeaSplitter splitter, FakeItems items, _, _, List<string> warnings) = Build();
        items.Add(Item(1, ToolId, "Splitting Tool", 1));

        Assert.False(splitter.TryStart(LeadScarabWeenie, "test"));
        Assert.Empty(warnings); // design point 4/5: no pea is the ordinary case, never warned
    }

    [Fact]
    public void ASecondStartIsRefusedWhileOneIsAlreadyInFlight()
    {
        (PeaSplitter splitter, FakeItems items, _, _, _) = Build();
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        Assert.True(splitter.TryStart(LeadScarabWeenie, "first"));
        Assert.False(splitter.IsIdle);
        Assert.False(splitter.TryStart(LeadScarabWeenie, "second"));
    }

    // -- Peace mode first, IsBusy wait, confirm ------------------------------------------------

    [Fact]
    public void ASplitStartedOutOfPeaceRequestsItOnceThenAppliesAfterTheSettleWait()
    {
        (PeaSplitter splitter, FakeItems items, FakeCombat combat, _, _) = Build(stanceSettleSeconds: 2);
        combat.Mode = PluginCombatMode.Magic;
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        Assert.True(splitter.TryStart(LeadScarabWeenie, "test"));
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1));
        Assert.Equal(1, combat.EnterModeCallCount);
        Assert.Equal(PluginCombatMode.Peace, combat.Mode); // the optimistic client-side flip, not proof of settling
        Assert.Equal(0, items.ApplyCallCount); // still spending the settle wait

        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // 2 s reaches the settle bound
        Assert.Equal(1, items.ApplyCallCount); // now applies
        Assert.Equal(1, combat.EnterModeCallCount); // never asked twice for the one request
    }

    /// <summary>A stance other than Magic (Melee, Missile, or an unrecognised Unknown
    /// reading) still leaves peace first, not only a Magic-specific check.</summary>
    [Fact]
    public void ASplitStartedInAnyNonPeaceStanceRequestsPeaceFirst()
    {
        (PeaSplitter splitter, FakeItems items, FakeCombat combat, _, _) = Build(stanceSettleSeconds: 1);
        combat.Mode = PluginCombatMode.Melee;
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(0.5));
        Assert.Equal(1, combat.EnterModeCallCount);
        Assert.Equal(0, items.ApplyCallCount); // no Apply until Peace settles

        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(0.5)); // settle bound reached
        Assert.Equal(1, items.ApplyCallCount);
    }

    /// <summary>A settle wait that runs out fails only this split, never the
    /// session, and never issues the Apply at all.</summary>
    [Fact]
    public void PeaceThatNeverSettlesFailsWithoutApplyingOrDisablingTheSession()
    {
        (PeaSplitter splitter, FakeItems items, FakeCombat combat, _, List<string> warnings) =
            Build(stanceSettleSeconds: 2);
        combat.Mode = PluginCombatMode.Magic;
        combat.ModeNeverSettles = true;
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // 1 s of the settle wait spent
        Assert.Equal(PeaSplitPollResult.Failed, splitter.Poll(1)); // 2 s reaches the bound, still not Peace

        Assert.Equal(0, items.ApplyCallCount);
        Assert.False(splitter.SessionDisabled);
        Assert.Contains(warnings, w => w.Contains("[split]") && w.Contains("did not settle"));

        // Not session-disabled: a fresh request is still accepted once the stance actually settles.
        combat.ModeNeverSettles = false;
        items.Add(Item(3, LeadPeaWeenie, "Lead Pea", 5));
        Assert.True(splitter.TryStart(LeadScarabWeenie, "retry"));
    }

    [Fact]
    public void PeaceIsRequestedExactlyOnceEvenAcrossManySmallFramePolls()
    {
        (PeaSplitter splitter, FakeItems items, FakeCombat combat, _, _) = Build(stanceSettleSeconds: 5);
        combat.Mode = PluginCombatMode.Magic;
        combat.ModeNeverSettles = true;
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        splitter.TryStart(LeadScarabWeenie, "test");

        // 30 host-frame polls at 1/60 s each (0.5 s of real time), well under the 5 s bound.
        for (int i = 0; i < 30; i++)
            splitter.Poll(1d / 60);

        Assert.Equal(1, combat.EnterModeCallCount);
    }

    [Fact]
    public void ApplyWaitsOutIsBusyBeforeIssuingTheRequest()
    {
        (PeaSplitter splitter, FakeItems items, _, _, _) = Build(busyTimeoutSeconds: 3);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));
        items.BusyOverride = true;

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1));
        Assert.Equal(0, items.ApplyCallCount);

        items.BusyOverride = false;
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1));
        Assert.Equal(1, items.ApplyCallCount);
    }

    [Fact]
    public void StayingBusyPastTheBoundFailsTheSplitWithoutEverApplying()
    {
        (PeaSplitter splitter, FakeItems items, _, _, List<string> warnings) = Build(busyTimeoutSeconds: 2);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));
        items.BusyOverride = true;

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1));
        Assert.Equal(PeaSplitPollResult.Failed, splitter.Poll(1));

        Assert.Equal(0, items.ApplyCallCount);
        Assert.True(splitter.SessionDisabled);
        Assert.Contains(warnings, w => w.Contains("[split]") && w.Contains("busy"));
    }

    [Fact]
    public void AConfirmedSplitReportsConfirmed()
    {
        (PeaSplitter splitter, FakeItems items, _, List<string> traces, _) = Build();
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));
        items.OnApplyAccepted = fake =>
        {
            fake.ShrinkOrRemove(2, by: 1);
            fake.AddStock(LeadScarabWeenie, "Lead Scarab", 20);
        };

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // applies this poll
        Assert.Equal(PeaSplitPollResult.Confirmed, splitter.Poll(1)); // sees the pea drop, scarabs rise

        Assert.True(splitter.IsIdle);
        Assert.False(splitter.SessionDisabled);
        Assert.Contains(traces, t => t.Contains("confirmed"));
    }

    [Fact]
    public void AnUnconfirmedSplitDisablesTheSessionAndLogsOnce()
    {
        (PeaSplitter splitter, FakeItems items, _, _, List<string> warnings) =
            Build(confirmTimeoutSeconds: 2);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));
        // No OnApplyAccepted wired up: Apply "succeeds" but nothing in inventory ever moves.

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // applies
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // 1 s of confirm budget spent
        Assert.Equal(PeaSplitPollResult.Failed, splitter.Poll(1)); // 2 s, bound reached

        Assert.True(splitter.SessionDisabled);
        Assert.Single(warnings);
        Assert.Contains("[split]", warnings[0]);
        Assert.Contains("could not confirm", warnings[0]);

        // Disabled for the session: a fresh, otherwise-perfectly-eligible request is refused too.
        items.Add(Item(3, LeadPeaWeenie, "Lead Pea", 5));
        Assert.False(splitter.TryStart(LeadScarabWeenie, "retry"));
    }

    // -- Apply's own server answer ------------------------------------------------------------

    /// <summary>WeenieError 0x043A is "must be at rest in peace mode to do trade skills",
    /// even though the tracked mode already read Peace.</summary>
    [Fact]
    public void AStanceRefusalAfterApplyRetriesPeaceOnceThenSucceeds()
    {
        (PeaSplitter splitter, FakeItems items, FakeCombat combat, List<string> traces, _) =
            Build(stanceSettleSeconds: 1);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // already Peace -> applies this poll
        Assert.Equal(1, items.ApplyCallCount);

        items.LastCompletion = new PluginItemUseCompletion(
            Revision: 1, SourceObjectId: items.LastApplyToolObjectId,
            TargetObjectId: items.LastApplyTargetObjectId, WeenieError: 0x043Au);
        items.OnApplyAccepted = fake =>
        {
            fake.ShrinkOrRemove(2, by: 1);
            fake.AddStock(LeadScarabWeenie, "Lead Scarab", 20);
        };

        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(0.5)); // sees the refusal, starts the retry wait
        Assert.Equal(1, items.ApplyCallCount); // not retried yet
        Assert.Equal(1, combat.EnterModeCallCount); // never resent — a desynced client already reads Peace

        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // settle bound reached, retries Apply
        Assert.Equal(2, items.ApplyCallCount);

        Assert.Equal(PeaSplitPollResult.Confirmed, splitter.Poll(1)); // inventory already reflects the retry

        Assert.False(splitter.SessionDisabled);
        Assert.Equal(1, combat.EnterModeCallCount);
        Assert.Contains(traces, t => t.Contains("retrying peace"));
    }

    [Fact]
    public void ASecondStanceRefusalFailsWithoutDisablingTheSession()
    {
        (PeaSplitter splitter, FakeItems items, FakeCombat combat, _, List<string> warnings) =
            Build(stanceSettleSeconds: 1);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        splitter.TryStart(LeadScarabWeenie, "test");
        splitter.Poll(1); // applies
        items.LastCompletion = new PluginItemUseCompletion(
            Revision: 1, SourceObjectId: items.LastApplyToolObjectId,
            TargetObjectId: items.LastApplyTargetObjectId, WeenieError: 0x043Au);
        splitter.Poll(0.5); // detects the refusal, starts the retry wait
        splitter.Poll(1); // settle bound reached, retries Apply
        Assert.Equal(2, items.ApplyCallCount);

        // The retry is refused the exact same way.
        items.LastCompletion = new PluginItemUseCompletion(
            Revision: 2, SourceObjectId: items.LastApplyToolObjectId,
            TargetObjectId: items.LastApplyTargetObjectId, WeenieError: 0x043Au);

        Assert.Equal(PeaSplitPollResult.Failed, splitter.Poll(1)); // immediate, no 10 s confirm wait spent

        Assert.False(splitter.SessionDisabled);
        Assert.Equal(1, combat.EnterModeCallCount); // still just the one original request
        Assert.Contains(warnings, w => w.Contains("[split]") && w.Contains("0x043A"));
    }

    [Fact]
    public void AnyOtherWeenieErrorFailsImmediatelyWithoutRetryOrSessionDisable()
    {
        (PeaSplitter splitter, FakeItems items, _, _, List<string> warnings) =
            Build(confirmTimeoutSeconds: 10);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1)); // applies

        items.LastCompletion = new PluginItemUseCompletion(
            Revision: 1, SourceObjectId: items.LastApplyToolObjectId,
            TargetObjectId: items.LastApplyTargetObjectId, WeenieError: 0x0001u);

        Assert.Equal(PeaSplitPollResult.Failed, splitter.Poll(0.1)); // immediate, well under the 10 s bound

        Assert.Equal(1, items.ApplyCallCount); // never retried — only the stance error gets one
        Assert.False(splitter.SessionDisabled);
        Assert.Contains(warnings, w => w.Contains("[split]") && w.Contains("0x0001"));
    }

    /// <summary>60 host-frame polls at 1/60 s each is about one real second — a
    /// poll-count bound of 10 would fail the split before confirmation could land.</summary>
    [Fact]
    public void ConfirmationLandingAfterSixtySmallFramePollsStillConfirms()
    {
        (PeaSplitter splitter, FakeItems items, _, _, _) = Build(confirmTimeoutSeconds: 5);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1d / 60)); // applies this poll

        for (int i = 0; i < 59; i++)
            Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1d / 60));

        // The server's own effect of the Apply lands now, about a real second in.
        items.ShrinkOrRemove(2, by: 1);
        items.AddStock(LeadScarabWeenie, "Lead Scarab", 20);

        PeaSplitPollResult result = PeaSplitPollResult.Pending;
        for (int i = 0; i < 20 && result == PeaSplitPollResult.Pending; i++)
            result = splitter.Poll(1d / 60);

        Assert.Equal(PeaSplitPollResult.Confirmed, result);
    }

    [Fact]
    public void ConfirmBudgetExpiringFailsTheSplitEvenAtSmallFrameDeltas()
    {
        (PeaSplitter splitter, FakeItems items, _, _, List<string> warnings) =
            Build(confirmTimeoutSeconds: 1);
        items.Add(Item(1, ToolId, "Splitting Tool", 1));
        items.Add(Item(2, LeadPeaWeenie, "Lead Pea", 5));
        // No inventory mutation ever wired up: the split can never confirm.

        splitter.TryStart(LeadScarabWeenie, "test");
        Assert.Equal(PeaSplitPollResult.Pending, splitter.Poll(1d / 60)); // applies this poll

        PeaSplitPollResult result = PeaSplitPollResult.Pending;
        for (int i = 0; i < 120 && result == PeaSplitPollResult.Pending; i++) // well past 1 s at 60 fps
            result = splitter.Poll(1d / 60);

        Assert.Equal(PeaSplitPollResult.Failed, result);
        Assert.True(splitter.SessionDisabled);
        Assert.Single(warnings);
    }

    // -- Top-up candidate selection -------------------------------------------------------------

    [Fact]
    public void SelectTopUpCandidatePicksTheLowestStockInScopeReagentBelowTheMark()
    {
        var report = new ComponentReport(true,
        [
            new ComponentUsage(LeadScarabWeenie, "Lead Scarab", Stock: 10, UsedBy: 2),
            new ComponentUsage(687u, "Gold Scarab", Stock: 3, UsedBy: 1), // lower stock, has a recipe
            new ComponentUsage(20631u, "Prismatic Taper", Stock: 999, UsedBy: 1), // well stocked
        ]);

        Assert.Equal(687u, PeaSplitter.SelectTopUpCandidate(report, lowStockThreshold: 25));
    }

    /// <summary>A foci caster's Prismatic Taper (<see cref="Components.ComponentReportBuilder.Build"/>'s
    /// <c>usesScarabOnlyFormula</c>) is picked exactly like any other in-scope reagent.</summary>
    [Fact]
    public void SelectTopUpCandidatePicksThePrismaticTaperWhenItIsLowInStock()
    {
        var report = new ComponentReport(true,
        [
            new ComponentUsage(20631u, "Prismatic Taper", Stock: 1, UsedBy: 3),
        ]);

        Assert.Equal(20631u, PeaSplitter.SelectTopUpCandidate(report, lowStockThreshold: 25));
    }

    [Fact]
    public void SelectTopUpCandidateIgnoresARowNoResolvedSpellNeeds()
    {
        var report = new ComponentReport(true,
        [
            new ComponentUsage(LeadScarabWeenie, "Lead Scarab", Stock: 1, UsedBy: 0), // held, not needed
        ]);

        Assert.Null(PeaSplitter.SelectTopUpCandidate(report, lowStockThreshold: 25));
    }

    [Fact]
    public void SelectTopUpCandidateIgnoresAComponentWithNoRecipe()
    {
        var report = new ComponentReport(true,
        [
            new ComponentUsage(8897u, "Platinum Scarab", Stock: 1, UsedBy: 1), // no pea exists
        ]);

        Assert.Null(PeaSplitter.SelectTopUpCandidate(report, lowStockThreshold: 25));
    }

    [Fact]
    public void SelectTopUpCandidateReturnsNullOnceEverythingIsAtOrAboveTheMark()
    {
        var report = new ComponentReport(true,
        [
            new ComponentUsage(LeadScarabWeenie, "Lead Scarab", Stock: 25, UsedBy: 2),
        ]);

        Assert.Null(PeaSplitter.SelectTopUpCandidate(report, lowStockThreshold: 25));
    }

    [Fact]
    public void SelectTopUpCandidateReturnsNullWhenTheReportCannotAnswer() =>
        Assert.Null(PeaSplitter.SelectTopUpCandidate(ComponentReport.Unavailable, lowStockThreshold: 25));
}
