using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Components;
using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests;

public sealed class CastStateMachineTests
{
    private const uint Target = 555;
    private const string TargetName = "Archer";
    private const uint WandObjectId = 4242;

    [Fact]
    public void UnpreparedRunWieldsAWandEntersMagicModeThenCastsAndReportsOnConfirmation()
    {
        var items = new FakeItems();
        items.Add(Wand(WandObjectId, wielded: false));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat(); // starts in Peace
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var enchantments = new FakeEnchantments();
        var machine = new CastStateMachine(magic, enchantments, items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 10, untilSent: magic);

        Assert.Null(result); // still waiting on confirmation once the cast is sent
        Assert.True(ticks < 10);
        Assert.Equal(WandObjectId, equipment.LastEquipped);
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode);
        Assert.Equal(PluginCombatMode.Magic, combat.Mode);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);

        magic.IsCasting = true;
        Assert.Null(machine.Advance(0.1, []));
        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);

        IReadOnlyList<PluginChatMessage> confirmation =
            [Confirm("Strength Other I", TargetName)];
        CastRunResult? done = machine.Advance(0.1, confirmation);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(CastOutcome.Cast, step.Outcome);
        Assert.True(enchantments.Reported);
        Assert.Equal((Target, 42u, 120d), (enchantments.LastTarget, enchantments.LastSpellId, enchantments.LastDuration));
    }

    // -- session statistics ------------------------------------------------------------

    [Fact]
    public void ALandedCastFeedsTheLineTallyAndAnAlreadyUpSkipFeedsOnlyTheRing()
    {
        var stats = new SolrLabs.BuffBot.Stats.BotStats();
        var (machine, magic, _, _) = PreparedMachine(stats: stats);
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42, durationSeconds: 1800f)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        SolrLabs.BuffBot.Stats.SpellLineStats line =
            Assert.Single(stats.Snapshot().Session.Lines);
        Assert.Equal(1, line.Landed);

        // Still up: a second Begin for the same spell inside its duration skips rather than re-casting; a skip still reaches the Recent ring.
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42, durationSeconds: 1800f)], Target, TargetName);
        machine.Advance(0, []);

        Assert.Equal(1, stats.Snapshot().Session.Lines.Single().Attempts); // unchanged by the skip
        SolrLabs.BuffBot.Stats.RecentEvent newest = stats.Snapshot().Recent[0];
        Assert.Equal(SolrLabs.BuffBot.Stats.RecentEventKind.Skipped, newest.Kind);
        Assert.Equal("already up", newest.Reason);
    }

    [Fact]
    public void AFizzleFeedsTheLineSFizzleCountAndTheRing()
    {
        var stats = new SolrLabs.BuffBot.Stats.BotStats();
        var (machine, _, _, _) = PreparedMachine(stats: stats);
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));
        Assert.Null(machine.Advance(0.1, [Fizzle()]));

        SolrLabs.BuffBot.Stats.SpellLineStats line =
            Assert.Single(stats.Snapshot().Session.Lines);
        Assert.Equal(1, line.Fizzles);
        Assert.Equal(1, line.Attempts);
        Assert.Equal(SolrLabs.BuffBot.Stats.RecentEventKind.Fizzle, stats.Snapshot().Recent[0].Kind);
    }

    [Fact]
    public void UnlearnedLinesFromBeginAreRecordedUpFrontAndMakeTheRunPartial()
    {
        // A player-requested line with nothing learned is folded into the closing tell instead of refusing the whole request; it alone is enough to make an otherwise-clean run Partial.
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin(
            [Resolved(10, tier: 1, spellId: 42)], Target, TargetName,
            unlearnedLines: ["Willpower Other"]);

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.Equal(CastRunOutcome.Partial, done!.Outcome);
        Assert.Equal(2, done.Steps.Count);
        Assert.Equal(CastOutcome.Failed, done.Steps[0].Outcome); // the unlearned line, recorded first
        Assert.Equal("Willpower Other", done.Steps[0].Line);
        Assert.Equal(CastFailureKind.NotLearned, done.Steps[0].Failure!.Value.Kind);
        Assert.Equal(CastOutcome.Cast, done.Steps[1].Outcome);
    }

    [Fact]
    public void UnlearnedLinesAloneWithAnEmptyPlanStillFinishAsPartial()
    {
        var (machine, _, _, _) = PreparedMachine();
        machine.Begin([], Target, TargetName, unlearnedLines: ["Willpower Other", "Focus Other"]);

        CastRunResult? done = machine.Advance(0, []);

        Assert.NotNull(done);
        Assert.Equal(CastRunOutcome.Partial, done!.Outcome);
        Assert.Equal(2, done.Steps.Count);
    }

    // -- bane-on-shield ---------------------------------------------

    [Fact]
    public void ABaneStepTargetsTheShieldRatherThanTheRequesterAndConfirmsOnTheShieldsName()
    {
        const uint ShieldObjectId = 777;
        const string ShieldName = "Round Shield";

        var (machine, magic, enchantments, _) = PreparedMachine();
        ResolvedSpell bane = Resolved(family: 10, tier: 1, spellId: 42)
            with { TargetOverride = (ShieldObjectId, ShieldName) };
        // Begin's own target/targetName is still the requester; only this one step overrides it.
        machine.Begin([bane], Target, TargetName);

        Assert.Null(machine.Advance(0, []));
        Assert.Equal(ShieldObjectId, magic.LastGateTargetObjectId);
        Assert.Equal(ShieldObjectId, magic.LastRequestTargetObjectId);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: ShieldObjectId, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", ShieldName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Equal(CastOutcome.Cast, Assert.Single(done.Steps).Outcome);
        Assert.True(enchantments.Reported);
        Assert.Equal(ShieldObjectId, enchantments.LastTarget); // ledgered under the shield, not the requester
    }

    [Fact]
    public void ABaneStepNeverConfirmsOnTheRequestersOwnNameOnceItHasAShieldOverride()
    {
        // The server's confirmation line names the shield ("You cast ... on Round Shield"), never the requester.
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell bane = Resolved(family: 10, tier: 1, spellId: 42)
            with { TargetOverride = (777u, "Round Shield") };
        machine.Begin([bane], Target, TargetName);

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? stillWaiting = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.Null(stillWaiting); // no confirmation matched yet -- still waiting on the shield's own line
    }

    [Fact]
    public void NoShieldLinesFromBeginAreRecordedUpFrontAndMakeTheRunPartial()
    {
        // The same up-front-recording shape unlearnedLines uses, for banes BuffCoordinator skipped because the requester had no shield equipped.
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin(
            [Resolved(10, tier: 1, spellId: 42)], Target, TargetName,
            noShieldLines: ["Acid Bane", "Blade Bane"]);

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.Equal(CastRunOutcome.Partial, done!.Outcome);
        Assert.Equal(3, done.Steps.Count);
        Assert.Equal(CastFailureKind.NoShield, done.Steps[0].Failure!.Value.Kind);
        Assert.Equal("Acid Bane", done.Steps[0].Line);
        Assert.Equal(CastFailureKind.NoShield, done.Steps[1].Failure!.Value.Kind);
        Assert.Equal("Blade Bane", done.Steps[1].Line);
        Assert.Equal(CastOutcome.Cast, done.Steps[2].Outcome);
    }

    [Fact]
    public void NoWandInInventoryFailsTheRequest()
    {
        var items = new FakeItems(); // empty — nothing to wield
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1)], Target, TargetName);

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.False(result!.IsSuccess);
        Assert.Equal(CastFailureKind.NoWand, result.Failure!.Value.Kind);
        Assert.Equal(0, magic.GateCalls); // never got far enough to check the gate
    }

    [Fact]
    public void MagicModeRefusalFailsTheRequestAfterWielding()
    {
        var items = new FakeItems();
        items.Add(Wand(WandObjectId, wielded: false));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { NextEnterModeResult = new(PluginCombatCommandStatus.Refused, "no spellbook") };
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1)], Target, TargetName);

        CastRunResult? result = RunUntilFinished(machine, [], maxTicks: 10);

        Assert.NotNull(result);
        Assert.False(result!.IsSuccess);
        Assert.Equal(CastFailureKind.ModeFailed, result.Failure!.Value.Kind);
        Assert.Equal(WandObjectId, equipment.LastEquipped); // wielding itself succeeded
    }

    [Fact]
    public void DroppedCastIsSkippedAsAPerSpellFailureWhenNoConfirmationArrivesWithinTheWindow()
    {
        // DidNotLand is per-spell: a single dropped cast at the end of a one-spell plan still
        // reaches the end, so the run reports Partial rather than aborting.
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // gate Ready -> RequestCast Sent

        // The server-drop shape: use-done fires immediately with error 0, and IsCasting is never observed true.
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 9, TargetObjectId: Target, WeenieError: 0);

        Assert.Null(machine.Advance(8d, [])); // within the 12 s confirmation window
        CastRunResult? result = machine.Advance(4.5d, []); // past it, still no chat line

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Partial, result!.Outcome);
        CastStep step = Assert.Single(result.Steps);
        Assert.Equal(CastOutcome.Failed, step.Outcome);
        Assert.Equal(CastFailureKind.DidNotLand, step.Failure!.Value.Kind);
    }

    [Fact]
    public void ConfirmationArrivingAtEightSecondsStillSucceeds()
    {
        // A level-7 cast's confirmation chat line can trail the completion by more than a second, well within the confirmation window.
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // gate Ready -> RequestCast Sent
        Assert.Null(machine.Advance(8d, [])); // still nothing — not yet a failure

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
    }

    [Fact]
    public void PeaceIsNotRequestedWhileStillCastingOnTheServer()
    {
        var (machine, magic, _, combat) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // gate Ready -> RequestCast Sent, IsCasting -> true

        // This machine gives up on the confirmation, but the fake server never told us the cast actually finished.
        CastRunResult? timedOut = machine.Advance(13d, []);
        Assert.NotNull(timedOut);
        Assert.False(timedOut!.IsSuccess);

        combat.LastRequestedMode = null;
        Assert.Null(machine.Advance(1d, [])); // idle tick, but still casting per the server
        Assert.Null(combat.LastRequestedMode);

        magic.IsCasting = false;
        Assert.Null(machine.Advance(1d, [])); // now clear to return to peace
        Assert.Equal(PluginCombatMode.Peace, combat.LastRequestedMode);
    }

    [Fact]
    public void FizzleThenSuccessSucceeds()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // first RequestCast sent
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // fizzled -> retried within the same tick
        Assert.Equal(2, magic.GateCalls);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 9, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
    }

    [Fact]
    public void FizzlesNeverGiveUpOnASpellTheyJustKeepRetryingIt()
    {
        // A fizzle is never, by itself, a reason to give up on a spell: some bots fizzle in
        // reality, and treating it too harshly would give up on real casters constantly.
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // first RequestCast sent
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // fizzle #1 -> retried
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // fizzle #2 -> still retried, not abandoned
        Assert.Equal(3, magic.GateCalls); // three real attempts at the same spell

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 9, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess); // never Partial - nothing was ever marked Failed
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(CastOutcome.Cast, step.Outcome);
    }

    [Fact]
    public void ThreeFizzlesInARowDropTheChainToTheNextLowerTierAndTheRetryUsesIt()
    {
        // Three fizzles in a row drops the chain to its next tier for the remainder of the chain; the count is chain-wide (ConsecutiveFizzlesBeforeChainTierDrop), not per-spell.
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0));
        machine.Begin([spell], Target, TargetName);

        Assert.Null(machine.Advance(0, []));           // VI requested
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // fizzle #1
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // fizzle #2
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // fizzle #3 -> chain drops to V, V requested

        // A confirmation for VI (the tier it started at) must not satisfy the now-lower retry.
        Assert.Null(machine.Advance(0.1, [Confirm("Strength Other VI", TargetName)]));

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 43, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other V", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess); // never Partial - a fizzle never marks a spell Failed
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(43u, step.SpellId); // landed at V, not the VI it started at
    }

    [Fact]
    public void SixFizzlesInARowDropTwoTiersRatherThanStoppingAtOne()
    {
        // Sustained bad luck keeps ratcheting the chain's tier down every further three in a row, rather than stopping after one drop. An explicit fizzleSkipBound above the default keeps this story about the tier drop specifically — see SixConsecutiveFizzlesOnOneSpellSkipItAtTheDefaultBound for the default bound's own behavior at exactly six.
        var (machine, magic, _, _) = PreparedMachine(fizzleSkipBound: 10);
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0),
            (SpellId: 44, Name: "Strength Other IV", ManaCost: 0));
        machine.Begin([spell], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // VI requested
        for (int i = 0; i < 5; i++)
            Assert.Null(machine.Advance(0.1, [Fizzle()])); // #1-5 (#3 drops to V)
        Assert.Null(machine.Advance(0.1, [Fizzle()]));      // #6 -> drops to IV too

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 44, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other IV", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(44u, step.SpellId);
    }

    [Fact]
    public void SixConsecutiveFizzlesOnOneSpellSkipItAtTheDefaultBound()
    {
        // Default bound 6: the same spell fizzling this many times in a row is given up on, a per-spell failure rather than retried forever. The chain tier drop still ran twice here (at fizzle #3 and #6) before the skip; the drop is not wasted, since it would apply to whatever the chain cast next, but this run has nothing left after this one spell.
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0),
            (SpellId: 44, Name: "Strength Other IV", ManaCost: 0));
        machine.Begin([spell], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // VI requested
        for (int i = 0; i < 5; i++)
            Assert.Null(machine.Advance(0.1, [Fizzle()])); // #1-5 (#3 drops to V)

        CastRunResult? done = machine.Advance(0.1, [Fizzle()]); // #6 -> drops to IV, then skipped

        Assert.NotNull(done);
        Assert.Equal(CastRunOutcome.Partial, done!.Outcome);
        Assert.Null(done.Failure);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(CastOutcome.Failed, step.Outcome);
        Assert.Equal(44u, step.SpellId); // the tier it was stuck on when it gave up
        Assert.Equal(CastFailureKind.Fizzled, step.Failure!.Value.Kind);
    }

    [Fact]
    public void TheFizzleSkipBoundStillAppliesWithTheTierFallbackSwitchOff()
    {
        // Applies whether the tier fallback switch is on or off: the fizzle-skip bound is a different lever, unaffected by that switch.
        var (machine, magic, _, _) = PreparedMachine(tierFallbackEnabled: false, fizzleSkipBound: 3);
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // requested
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #1
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #2
        CastRunResult? done = machine.Advance(0.1, [Fizzle()]); // #3 -> skip bound reached

        Assert.NotNull(done);
        Assert.Equal(CastRunOutcome.Partial, done!.Outcome);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(CastOutcome.Failed, step.Outcome);
        Assert.Equal(CastFailureKind.Fizzled, step.Failure!.Value.Kind);
    }

    [Fact]
    public void AFizzleSkipDoesNotCountTowardTheBrokenSessionBound()
    {
        // A fizzle-skip is a per-spell failure but must never trip MaxConsecutivePerSpellFailures (3) on its own. If it counted, this run (one fizzle-skip plus two ordinary refusals) would stop early as CastRunOutcome.Failed instead of reaching the natural end of its three-spell plan.
        var (machine, magic, _, _) = PreparedMachine(fizzleSkipBound: 2);
        ResolvedSpell first = Resolved(family: 10, tier: 1, spellId: 9);
        ResolvedSpell second = Resolved(family: 11, tier: 1, spellId: 10);
        ResolvedSpell third = Resolved(family: 12, tier: 1, spellId: 11);
        magic.RefuseGateFor.Add(10); // spell 2
        magic.RefuseGateFor.Add(11); // spell 3
        machine.Begin([first, second, third], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // spell 1 requested
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #1
        // #2 -> spell 1 skipped; spell 2 and spell 3 both refused at the gate in the same tick
        CastRunResult? done = machine.Advance(0.1, [Fizzle()]);

        Assert.NotNull(done);
        Assert.Equal(CastRunOutcome.Partial, done!.Outcome);
        Assert.Null(done.Failure);
        Assert.Equal(3, done.Steps.Count);
    }

    [Fact]
    public void TheFizzleSkipCountResetsForANewSpellStep()
    {
        // A spell right behind a just-skipped one starts its own count at zero.
        var (machine, magic, _, _) = PreparedMachine(fizzleSkipBound: 3);
        ResolvedSpell first = Resolved(family: 10, tier: 1, spellId: 9);
        ResolvedSpell second = Resolved(family: 11, tier: 1, spellId: 10);
        machine.Begin([first, second], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // spell 1 requested
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // spell 1's #1
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // spell 1's #2
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // spell 1's #3 -> skipped, spell 2 requested

        Assert.Null(machine.Advance(0.1, [Fizzle()])); // spell 2's own #1, not spell 1's #4
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // spell 2's own #2, still short of the bound

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 10, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.Equal(CastRunOutcome.Partial, done!.Outcome);
        Assert.Equal(2, done.Steps.Count);
        Assert.Equal(CastOutcome.Failed, done.Steps[0].Outcome);
        Assert.Equal(CastOutcome.Cast, done.Steps[1].Outcome);
    }

    /// <summary><see cref="CastStateMachine.FizzleSkipBound"/> is the pending value a console write lands in — a run already in flight keeps reading the bound <see cref="CastStateMachine.Begin"/> snapshotted when it started.</summary>
    [Fact]
    public void WritingFizzleSkipBoundMidRunAppliesOnlyToTheNextRunBegun()
    {
        var (machine, magic, _, _) = PreparedMachine(fizzleSkipBound: 3);
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // requested
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #1

        // A console write arriving mid-run (BuffBotPlugin.SyncSettingsIntoRuntime's own every-tick
        // write) does not retroactively tighten the run already in flight.
        machine.FizzleSkipBound = 1;
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #2 -- still short of the run's own bound of 3

        CastRunResult? doneAtOldBound = machine.Advance(0.1, [Fizzle()]); // #3 -> the run's own bound
        Assert.NotNull(doneAtOldBound);
        Assert.Equal(CastOutcome.Failed, Assert.Single(doneAtOldBound!.Steps).Outcome);

        // The next run started after the write is the first to see it.
        machine.Begin([Resolved(family: 11, tier: 1, spellId: 10)], Target, TargetName);
        Assert.Null(machine.Advance(0, [])); // requested
        CastRunResult? doneAtNewBound = machine.Advance(0.1, [Fizzle()]); // #1 -> the new bound of 1

        Assert.NotNull(doneAtNewBound);
        Assert.Equal(CastOutcome.Failed, Assert.Single(doneAtNewBound!.Steps).Outcome);
    }

    [Fact]
    public void TierFallbackDisabledNeverDropsTierOnFizzlesEvenPastTheChainDropThreshold()
    {
        var (machine, magic, _, _) = PreparedMachine(tierFallbackEnabled: false, fizzleSkipBound: 10);
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0));
        machine.Begin([spell], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // VI requested
        for (int i = 0; i < 3; i++)
            Assert.Null(machine.Advance(0.1, [Fizzle()])); // #1-3 -- would drop to V if fallback were on

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other VI", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(42u, step.SpellId); // still VI: no drop happened
    }

    [Fact]
    public void TierFallbackDisabledBouncesForManaInsteadOfSteppingDownForAffordability()
    {
        var character = new FakeCharacter { CurrentMana = 40, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character, tierFallbackEnabled: false);
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 60),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 30));
        machine.Begin([spell], Target, TargetName, ManaUpkeepPlan());

        CastRunResult? result = machine.Advance(0, []);

        Assert.Null(result); // waiting on the upkeep bounce's own confirmation
        Assert.True(magic.SelfGateCalls > 0); // the mana-upkeep pair was gate-checked
        Assert.True(magic.LastRequestWasSelfTargeted); // an upkeep cast, not the stepped-down V
    }

    [Fact]
    public void TheChainTierDropPersistsForLaterSpellsInTheSameChain()
    {
        // "...for the remainder of that chain": a drop earned by one spell's bad luck applies to
        // every spell the chain reaches afterward, not just the one that fizzled.
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell first = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0));
        ResolvedSpell second = ResolvedWithLadder(
            "Endurance Other", family: 11,
            (SpellId: 50, Name: "Endurance Other VI", ManaCost: 0),
            (SpellId: 51, Name: "Endurance Other V", ManaCost: 0));
        machine.Begin([first, second], Target, TargetName);

        Assert.Null(machine.Advance(0, []));           // Strength Other VI requested
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #1
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #2
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // #3 -> drops to V, V requested

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 43, TargetObjectId: Target, WeenieError: 0);
        Assert.Null(machine.Advance(0.1, [Confirm("Strength Other V", TargetName)])); // lands; Endurance Other requested next

        // Endurance Other never fizzled itself, but the chain's own offset still applies to it:
        // it is requested at V, not VI.
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: 51, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Endurance Other V", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Equal(2, done.Steps.Count);
        Assert.Equal(43u, done.Steps[0].SpellId);
        Assert.Equal(51u, done.Steps[1].SpellId);
    }

    [Fact]
    public void TheChainTierOffsetResetsAtTheStartOfTheNextChain()
    {
        // "...but try again at the beginning of the next": Begin() resets the offset, so a fresh
        // requester's chain re-tests the caster's top learned tier.
        const uint SecondTarget = 777u;
        const string SecondTargetName = "Rogue";

        var (machine, magic, _, _) = PreparedMachine();
        static ResolvedSpell Ladder() => ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0));

        machine.Begin([Ladder()], Target, TargetName);
        Assert.Null(machine.Advance(0, []));
        Assert.Null(machine.Advance(0.1, [Fizzle()]));
        Assert.Null(machine.Advance(0.1, [Fizzle()]));
        Assert.Null(machine.Advance(0.1, [Fizzle()])); // drops to V

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 43, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? firstChain = machine.Advance(0.1, [Confirm("Strength Other V", TargetName)]);
        Assert.True(firstChain!.IsSuccess);

        // A different requester (so the "already up" ledger can't mask this): the fresh chain
        // starts back at VI, not the V the previous chain had dropped to.
        machine.Begin([Ladder()], SecondTarget, SecondTargetName);
        Assert.Null(machine.Advance(0, [])); // Strength Other VI requested, freshly at the top
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: 42, TargetObjectId: SecondTarget, WeenieError: 0);
        CastRunResult? secondChain = machine.Advance(0.1, [Confirm("Strength Other VI", SecondTargetName)]);

        Assert.NotNull(secondChain);
        Assert.True(secondChain!.IsSuccess);
        CastStep step = Assert.Single(secondChain.Steps);
        Assert.Equal(42u, step.SpellId);
    }

    [Fact]
    public void ThreeConsecutivePerSpellFailuresConcludeTheSessionIsBrokenAndStopTheRun()
    {
        var (machine, magic, _, _) = PreparedMachine();
        magic.Gate = PluginCastGate.NotKnown;
        machine.Begin(
            [
                Resolved(family: 1, tier: 1, spellId: 1),
                Resolved(family: 2, tier: 1, spellId: 2),
                Resolved(family: 3, tier: 1, spellId: 3),
                Resolved(family: 4, tier: 1, spellId: 4),
            ],
            Target,
            TargetName);

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Failed, result!.Outcome);
        Assert.Equal(CastFailureKind.TooManyFailures, result.Failure!.Value.Kind);
        Assert.Equal(3, result.Steps.Count); // the fourth spell was never attempted
        Assert.All(result.Steps, step => Assert.Equal(CastOutcome.Failed, step.Outcome));
        Assert.Equal(3, magic.GateCalls);
    }

    [Fact]
    public void PrefersAWandWithoutTrainingInItsNameOverOneThatHasIt()
    {
        var items = new FakeItems();
        items.Add(Wand(1001, wielded: false, name: "Training Wand"));
        items.Add(Wand(1002, wielded: false, name: "Ice Wand"));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        RunUntil(machine, [], out _, maxTicks: 10, untilSent: magic);

        Assert.Equal(1002u, equipment.LastEquipped);
    }

    [Fact]
    public void PreferredWandAlreadyWieldedIsNeverTouched()
    {
        // The preference must not fire an Equip at all when the better wand is already in hand —
        // nothing to swap to.
        var items = new FakeItems();
        items.Add(Wand(1001, wielded: false, name: "Training Wand"));
        items.Add(Wand(1002, wielded: true, name: "Ice Wand"));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 10, untilSent: magic);

        Assert.Null(result);
        Assert.True(ticks < 10);
        Assert.Equal(0u, equipment.LastEquipped); // Equip was never called
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void ALessPreferredWandAlreadyWieldedIsSwappedForABetterOneInInventory()
    {
        // A Training Wand left wielded from an earlier session (or by hand) must give way to an
        // Ice Wand sitting unwielded in inventory, not stay in hand for the rest of the run.
        var items = new FakeItems();
        items.Add(Wand(1001, wielded: true, name: "Training Wand"));
        items.Add(Wand(1002, wielded: false, name: "Ice Wand"));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 10, untilSent: magic);

        Assert.Null(result); // still waiting on confirmation once the cast is sent
        Assert.True(ticks < 10);
        Assert.Equal(1002u, equipment.LastEquipped);
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void ARefusedSwapStillCastsThroughWhateverIsAlreadyWielded()
    {
        // The preference is an optimization, never a precondition: a swap the server refuses must
        // not stop the run — it must cast through the Training Wand rather than give up.
        var items = new FakeItems();
        items.Add(Wand(1001, wielded: true, name: "Training Wand"));
        items.Add(Wand(1002, wielded: false, name: "Ice Wand"));
        var equipment = new FakeEquipment(items)
        {
            NextResult = new PluginEquipmentCommandResult(PluginEquipmentCommandStatus.Refused, "busy"),
        };
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 10, untilSent: magic);

        Assert.Null(result); // the run kept going rather than failing on the refused swap
        Assert.True(ticks < 10);
        Assert.Equal(1002u, equipment.LastEquipped); // the swap was attempted...
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode); // ...but casting proceeded anyway
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void AnUnavailableSwapRetriesWithinTheWieldTimeoutAndSucceeds()
    {
        var items = new FakeItems();
        items.Add(Wand(1001, wielded: true, name: "Training Wand"));
        items.Add(Wand(1002, wielded: false, name: "Ice Wand"));
        var sequence = new Queue<PluginEquipmentCommandResult>(
        [
            new(PluginEquipmentCommandStatus.Unavailable),
            new(PluginEquipmentCommandStatus.Unavailable),
            new(PluginEquipmentCommandStatus.Started),
        ]);
        var equipment = new FakeEquipment(items, sequence);
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 20, untilSent: magic);

        Assert.Null(result);
        Assert.True(ticks < 20);
        Assert.Equal(3, equipment.EquipCallCount); // two Unavailable answers, then the real one
        Assert.Equal(1002u, equipment.LastEquipped); // ended up asking for the Ice Wand, not giving up on it
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void AnUnavailableSwapThatNeverRecoversFallsBackWithoutBlockingTheRun()
    {
        var items = new FakeItems();
        items.Add(Wand(1001, wielded: true, name: "Training Wand"));
        items.Add(Wand(1002, wielded: false, name: "Ice Wand"));
        var equipment = new FakeEquipment(items)
        {
            NextResult = new PluginEquipmentCommandResult(PluginEquipmentCommandStatus.Unavailable),
        };
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 30, untilSent: magic);

        Assert.Null(result); // the run kept going rather than waiting on the swap forever
        Assert.True(ticks < 30);
        Assert.True(equipment.EquipCallCount > 1); // it kept asking, not just once...
        Assert.Equal(1002u, equipment.LastEquipped); // ...about the Ice Wand...
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode); // ...but cast through Training Wand
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void AnAcceptedSwapWaitsForTheNewWandToActuallyLandBeforeCastingThroughIt()
    {
        const uint TrainingWandId = 1001;
        const uint StaffObjectId = 1002;
        PluginInventoryItem trainingWielded = Wand(TrainingWandId, wielded: true, name: "Training Wand");
        PluginInventoryItem trainingUnwielded = Wand(TrainingWandId, wielded: false, name: "Training Wand");
        PluginInventoryItem staffWielded = Wand(StaffObjectId, wielded: true, name: "Silver Staff");
        PluginInventoryItem staffUnwielded = Wand(StaffObjectId, wielded: false, name: "Silver Staff");

        var items = new FakeItems();
        items.Add(trainingWielded);
        items.Add(staffUnwielded);
        items.ScriptCaptures(
            [trainingWielded, staffUnwielded], // Started requested this tick
            [trainingWielded, staffUnwielded], // old wand still shown wielded...
            [trainingWielded, staffUnwielded], // ...for a few ticks
            [trainingUnwielded, staffUnwielded], // now nothing is wielded at all...
            [trainingUnwielded, staffUnwielded], // ...for a few ticks (Busy both times)
            [trainingUnwielded, staffWielded]); // the Staff finally lands
        var equipment = new FakeEquipment(items, new Queue<PluginEquipmentCommandResult>(
        [
            new(PluginEquipmentCommandStatus.Started),
            new(PluginEquipmentCommandStatus.Busy),
            new(PluginEquipmentCommandStatus.Busy),
        ]));
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        bool sentBeforeStaffLanded = false;
        CastRunResult? result = null;
        for (int tick = 0; tick < 6 && result is null; tick++)
        {
            result = machine.Advance(0.5, []);
            if (magic.LastRequestResult == PluginCastRequestResult.Sent)
                sentBeforeStaffLanded = true;
        }

        Assert.False(sentBeforeStaffLanded); // no cast until the Staff actually showed equipped
        Assert.Null(result); // and no fatal WieldFailed either, over the Busy retries

        result = RunUntil(machine, [], out int ticks, maxTicks: 15, untilSent: magic);

        Assert.Null(result); // still just waiting on confirmation, not a WieldFailed finish
        Assert.True(ticks < 15);
        Assert.Equal(StaffObjectId, equipment.LastEquipped);
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void BusyWithNothingWieldedRetriesRatherThanFailingTheRun()
    {
        var items = new FakeItems();
        items.Add(Wand(WandObjectId, wielded: false));
        var equipment = new FakeEquipment(items, new Queue<PluginEquipmentCommandResult>(
        [
            new(PluginEquipmentCommandStatus.Busy),
            new(PluginEquipmentCommandStatus.Busy),
            new(PluginEquipmentCommandStatus.Started),
        ]));
        var combat = new FakeCombat();
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var machine = new CastStateMachine(magic, new FakeEnchantments(), items, equipment, combat);
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 20, untilSent: magic);

        Assert.Null(result); // waiting on confirmation, not failed over the Busy answers
        Assert.True(ticks < 20);
        Assert.Equal(3, equipment.EquipCallCount);
        Assert.Equal(WandObjectId, equipment.LastEquipped);
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void SecondRequestForAnIdleOrTopUpRunIsStillSkippedForTheFullDuration()
    {
        var (machine, magic, enchantments, _) = PreparedMachine();
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42, durationSeconds: 1800f)], Target, TargetName);
        magic.Gate = PluginCastGate.Ready;

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? first = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);
        Assert.True(first!.IsSuccess);
        Assert.True(enchantments.Reported);

        Assert.Null(machine.Advance(301d, [])); // idle the whole time

        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42, durationSeconds: 1800f)], Target, TargetName);
        int gateCallsBefore = magic.GateCalls;
        CastRunResult? second = machine.Advance(0, []);

        Assert.NotNull(second);
        Assert.True(second!.IsSuccess);
        CastStep step = Assert.Single(second.Steps);
        Assert.Equal(CastOutcome.Skipped, step.Outcome);
        Assert.Equal(gateCallsBefore, magic.GateCalls); // never re-evaluated the gate
    }

    [Fact]
    public void SecondPlayerRequestAlwaysRecastsRegardlessOfHowRecentlyBuffed()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin(
            [Resolved(family: 10, tier: 1, spellId: 42, durationSeconds: 1800f)], Target, TargetName,
            isPlayerRequest: true);
        magic.Gate = PluginCastGate.Ready;

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? first = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);
        Assert.True(first!.IsSuccess);

        Assert.Null(machine.Advance(1d, [])); // barely any time has passed since the first cast

        machine.Begin(
            [Resolved(family: 10, tier: 1, spellId: 42, durationSeconds: 1800f)], Target, TargetName,
            isPlayerRequest: true);
        int gateCallsBefore = magic.GateCalls;
        CastRunResult? second = RunUntil(machine, [], out int ticks, maxTicks: 10, untilSent: magic);

        Assert.Null(second); // not skipped: re-entered magic mode and sent a fresh cast
        Assert.True(ticks < 10);
        Assert.True(magic.GateCalls > gateCallsBefore); // the gate was re-evaluated, not trusted from the ledger
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void SelfTargetedStepsInsideAPlayerRequestStillSkipForTheFullDuration()
    {
        var (machine, magic, enchantments, _) = PreparedMachine();
        machine.Begin(
            [ResolvedSelf(family: 77, tier: 6, spellId: 99)], Target, TargetName, isPlayerRequest: true);

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 99, TargetObjectId: 0, WeenieError: 0);
        CastRunResult? first = machine.Advance(0.1, [SelfConfirm("Focus Self VI")]);
        Assert.True(first!.IsSuccess);
        Assert.True(enchantments.Reported);

        Assert.Null(machine.Advance(301d, [])); // well under the 1200 s duration

        machine.Begin(
            [ResolvedSelf(family: 77, tier: 6, spellId: 99)], Target, TargetName, isPlayerRequest: true);
        int selfGateCallsBefore = magic.SelfGateCalls;
        CastRunResult? second = machine.Advance(0, []);

        Assert.NotNull(second);
        Assert.True(second!.IsSuccess);
        CastStep step = Assert.Single(second.Steps);
        Assert.Equal(CastOutcome.Skipped, step.Outcome);
        Assert.Equal(selfGateCallsBefore, magic.SelfGateCalls); // never re-evaluated: full duration ledger still applies
    }

    [Fact]
    public void APlayerRequestsSelfTargetedStepCountsAsUpkeepAndItsOtherTargetedStepAsRequested()
    {
        var stats = new SolrLabs.BuffBot.Stats.BotStats();
        var (machine, magic, _, _) = PreparedMachine(stats: stats);
        machine.Begin(
            [ResolvedSelf(family: 77, tier: 6, spellId: 99), Resolved(family: 10, tier: 1, spellId: 42)],
            Target,
            TargetName,
            isPlayerRequest: true);

        Assert.Null(machine.Advance(0, [])); // self step: gate Ready (self) -> RequestCast(spellId)
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 99, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Focus Self VI")])); // confirms self, sends targeted

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);

        // The current hour's own bucket — the last of Hours' fixed 12-hour window (padded oldest
        // first), since this run happened just now.
        SolrLabs.BuffBot.Stats.HourlyBucket hour = stats.Snapshot().Session.Hours[^1];
        Assert.Equal(1, hour.Requested);
        Assert.Equal(1, hour.Upkeep);
    }

    [Fact]
    public void SecondRequestAfterADroppedCastIsCastAgain()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));
        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? dropped = machine.Advance(12.5d, []); // no chat line, past the 12 s window
        Assert.False(dropped!.IsSuccess);

        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42)], Target, TargetName);
        CastRunResult? second = machine.Advance(0, []);

        Assert.Null(second); // gate re-evaluated and a fresh cast sent, not skipped
        Assert.Equal(2, magic.GateCalls);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void BusyRetriesThenTimesOutAsAPerSpellFailure()
    {
        // TimedOut is per-spell: "you were busy too long" is a fact about the requester's state
        // during this one gate check, not the bot's session.
        var (machine, magic, _, _) = PreparedMachine();
        magic.Gate = PluginCastGate.Busy;
        machine.Begin([Resolved(10, tier: 1)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // first sees Busy
        CastRunResult? result = machine.Advance(10d, []); // still Busy (default timeout is 10 s)

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Partial, result!.Outcome);
        CastStep step = Assert.Single(result.Steps);
        Assert.Equal(CastOutcome.Failed, step.Outcome);
        Assert.Equal(CastFailureKind.TimedOut, step.Failure!.Value.Kind);
    }

    [Fact]
    public void BusyRecoversToReadyWithoutFailing()
    {
        var (machine, magic, _, _) = PreparedMachine();
        magic.Gate = PluginCastGate.Busy;
        machine.Begin([Resolved(10, tier: 1, spellId: 7)], Target, TargetName);

        Assert.Null(machine.Advance(0.5, []));
        magic.Gate = PluginCastGate.Ready;
        CastRunResult? afterRecovery = machine.Advance(0.5, []);

        Assert.Null(afterRecovery); // moved on to requesting the cast, still in flight
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
    }

    [Fact]
    public void GateRefusalSkipsThatSpellAsAPerSpellFailure()
    {
        // GateRefused is per-spell: TargetIncompatible or NotKnown are facts about this one spell,
        // re-evaluated fresh against live state on every attempt.
        var (machine, magic, _, _) = PreparedMachine();
        magic.Gate = PluginCastGate.TargetIncompatible;
        machine.Begin([Resolved(10, tier: 1)], Target, TargetName);

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Partial, result!.Outcome);
        CastStep step = Assert.Single(result.Steps);
        Assert.Equal(CastOutcome.Failed, step.Outcome);
        Assert.Equal(CastFailureKind.GateRefused, step.Failure!.Value.Kind);
    }

    [Fact]
    public void RequestCastRefusalSkipsThatSpellAsAPerSpellFailure()
    {
        var (machine, magic, _, _) = PreparedMachine();
        magic.Gate = PluginCastGate.Ready;
        magic.RequestResult = PluginCastRequestResult.UnknownSpell;
        machine.Begin([Resolved(10, tier: 1)], Target, TargetName);

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Partial, result!.Outcome);
        CastStep step = Assert.Single(result.Steps);
        Assert.Equal(CastOutcome.Failed, step.Outcome);
        Assert.Equal(CastFailureKind.RequestRefused, step.Failure!.Value.Kind);
    }

    [Fact]
    public void MissingComponentsEndsTheRunAsFatalRatherThanAPerSpellFailure()
    {
        var (machine, magic, _, _) = PreparedMachine();
        magic.Gate = PluginCastGate.Ready;
        magic.RequestResult = PluginCastRequestResult.MissingComponents;
        machine.Begin([Resolved(10, tier: 1)], Target, TargetName);

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Failed, result!.Outcome);
        Assert.Empty(result.Steps); // never attempted, and nothing else queued was reached
        Assert.Equal(CastFailureKind.MissingComponents, result.Failure!.Value.Kind);
    }

    // -- Missing-components retry: a refusal at the top tier steps down rather than giving up -----

    [Fact]
    public void MissingComponentsOnASelfBuffAtTheTopTierStepsDownAndCastsTheNextLearnedTier()
    {
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell spell = ResolvedSelfWithLadder(
            "Focus Self", family: 20,
            (SpellId: 98, Name: "Focus Self VII", ManaCost: 0),
            (SpellId: 99, Name: "Focus Self VI", ManaCost: 0));
        magic.RequestResultForSpellId[98] = PluginCastRequestResult.MissingComponents;
        machine.Begin([spell], Target, TargetName);

        CastRunResult? afterFirstTick = machine.Advance(0, []);

        Assert.Null(afterFirstTick); // waiting on confirmation of the stepped-down cast
        Assert.Equal(99u, magic.LastRequestSpellId);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 99, TargetObjectId: 0, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [SelfConfirm("Focus Self VI")]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(CastOutcome.Cast, step.Outcome);
        Assert.Equal(99u, step.SpellId);
        Assert.Equal(1, done.ComponentCeilingRung); // one rung down from the top
    }

    [Fact]
    public void TheComponentCeilingCapsALaterLineInTheSameRun()
    {
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell selfSpell = ResolvedSelfWithLadder(
            "Focus Self", family: 20,
            (SpellId: 98, Name: "Focus Self VII", ManaCost: 0),
            (SpellId: 99, Name: "Focus Self VI", ManaCost: 0));
        ResolvedSpell otherSpell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0));
        magic.RequestResultForSpellId[98] = PluginCastRequestResult.MissingComponents;
        machine.Begin([selfSpell, otherSpell], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // self step: 98 refused, 99 sent instead
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 99, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Focus Self VI")])); // confirms self, sends the Other line

        // The Other line's own top tier (42) is never requested: the ceiling (one rung down)
        // capped it to 43 before the gate was ever evaluated.
        Assert.Equal(43u, magic.LastRequestSpellId);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);

        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: 43, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other V", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Equal(2, done.Steps.Count);
        Assert.Equal(43u, done.Steps[1].SpellId);
    }

    /// <summary>Only once the lowest learned tier of a line is refused too does the run end as
    /// out of components — every rung above it was tried first, at no cost.</summary>
    [Fact]
    public void MissingComponentsAtEveryLearnedTierEndsTheRunAsOutOfComponents()
    {
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 0),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 0),
            (SpellId: 44, Name: "Strength Other IV", ManaCost: 0));
        magic.RequestResultForSpellId[42] = PluginCastRequestResult.MissingComponents;
        magic.RequestResultForSpellId[43] = PluginCastRequestResult.MissingComponents;
        magic.RequestResultForSpellId[44] = PluginCastRequestResult.MissingComponents;
        machine.Begin([spell], Target, TargetName);

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Failed, result!.Outcome);
        Assert.Empty(result.Steps); // never landed anything; the whole ladder was refused
        Assert.Equal(CastFailureKind.MissingComponents, result.Failure!.Value.Kind);
        Assert.Equal(44u, magic.LastRequestSpellId); // gave up at the lowest learned tier
    }

    // -- Mid-chain pea-split retry ----------------------------------------------------------------

    [Fact]
    public void AConfirmedMidChainSplitRetriesTheExactSameSpellAndLands()
    {
        var coordinator = new FakePeaSplitCoordinator();
        coordinator.PollResults.Enqueue(PeaSplitPollResult.Confirmed);
        var (machine, magic, _, _) = PreparedMachine(peaSplitCoordinator: coordinator);
        ResolvedSpell spell = ResolvedNeedingComponent(family: 10, spellId: 42, componentWeenieId: 691u);
        magic.RequestResultForSpellId[42] = PluginCastRequestResult.MissingComponents;
        coordinator.OnPoll = result =>
        {
            if (result == PeaSplitPollResult.Confirmed)
                magic.RequestResultForSpellId.Remove(42);
        };
        machine.Begin([spell], Target, TargetName);

        CastRunResult? afterFirstTick = machine.Advance(0, []);

        Assert.Null(afterFirstTick); // waiting on confirmation of the retried cast
        Assert.Equal(42u, magic.LastRequestSpellId); // the very same spell, no ladder to fall to
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
        Assert.Equal(1, coordinator.TryStartCallCount);
        Assert.Equal(691u, coordinator.LastComponentWeenieId);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Null(done.ComponentCeilingRung); // the tier ceiling was never touched
    }

    [Fact]
    public void MidChainMissingComponentsOnAFociCasterTriesThePrismaticPeaSplit()
    {
        var catalog = new FakeSpellCatalog(new Dictionary<uint, uint>
        {
            [0xC1u] = 37155u, // Mana Scarab -- no pea of its own, never matches a recipe
            [0xBCu] = 20631u, // Prismatic Taper -- the one a foci caster's own formula reaches for
        });
        var coordinator = new FakePeaSplitCoordinator();
        coordinator.PollResults.Enqueue(PeaSplitPollResult.Confirmed);
        var (machine, magic, _, _) = PreparedMachine(peaSplitCoordinator: coordinator, catalog: catalog);
        machine.UsesScarabOnlyFormula = school => school == 1u;
        var spell = new PluginSpellInfo(
            SpellId: 42, Name: "Focus Self I", Family: 10, Tier: 1, Difficulty: 0, ManaCost: 0,
            DurationSeconds: 120f, School: 1u, Description: string.Empty, IsSelfTargeted: true,
            IsBeneficial: true)
        { FormulaComponentIds = [0xC1u] };
        var resolved = new ResolvedSpell("Focus Self", spell);
        magic.RequestResultForSpellId[42] = PluginCastRequestResult.MissingComponents;
        coordinator.OnPoll = result =>
        {
            if (result == PeaSplitPollResult.Confirmed)
                magic.RequestResultForSpellId.Remove(42);
        };
        machine.Begin([resolved], Target, TargetName);

        CastRunResult? afterFirstTick = machine.Advance(0, []);

        Assert.Null(afterFirstTick); // waiting on confirmation of the retried cast
        Assert.Equal(1, coordinator.TryStartCallCount);
        Assert.Equal(20631u, coordinator.LastComponentWeenieId); // the taper, never the scarab

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [SelfConfirm("Focus Self I")]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
    }

    [Fact]
    public void NoPeaOrToolFallsThroughToTodaysBehaviourUnchanged()
    {
        var coordinator = new FakePeaSplitCoordinator { NextTryStartResult = false };
        var (machine, magic, _, _) = PreparedMachine(peaSplitCoordinator: coordinator);
        ResolvedSpell spell = ResolvedNeedingComponent(family: 10, spellId: 42, componentWeenieId: 691u);
        magic.RequestResultForSpellId[42] = PluginCastRequestResult.MissingComponents;
        machine.Begin([spell], Target, TargetName);

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Failed, result!.Outcome);
        Assert.Empty(result.Steps);
        Assert.Equal(CastFailureKind.MissingComponents, result.Failure!.Value.Kind);
        Assert.Equal(1, coordinator.TryStartCallCount); // asked once, refused, moved straight on
    }

    [Fact]
    public void AFailedMidChainSplitFallsThroughToTheExistingTierStepDown()
    {
        var coordinator = new FakePeaSplitCoordinator();
        coordinator.PollResults.Enqueue(PeaSplitPollResult.Failed);
        var (machine, magic, _, _) = PreparedMachine(peaSplitCoordinator: coordinator);
        var topTier = new PluginSpellInfo(
            SpellId: 98, Name: "Focus Self VII", Family: 20, Tier: 2, Difficulty: 0, ManaCost: 0,
            DurationSeconds: 1200f, School: 0, Description: string.Empty, IsSelfTargeted: true,
            IsBeneficial: true)
        { FormulaComponentIds = [691u] };
        var lowerTier = new PluginSpellInfo(
            SpellId: 99, Name: "Focus Self VI", Family: 20, Tier: 1, Difficulty: 0, ManaCost: 0,
            DurationSeconds: 1200f, School: 0, Description: string.Empty, IsSelfTargeted: true,
            IsBeneficial: true);
        var spell = new ResolvedSpell("Focus Self", topTier) { LearnedTiersDescending = [topTier, lowerTier] };
        magic.RequestResultForSpellId[98] = PluginCastRequestResult.MissingComponents;
        machine.Begin([spell], Target, TargetName);

        CastRunResult? afterFirstTick = machine.Advance(0, []);

        Assert.Null(afterFirstTick); // waiting on confirmation of the stepped-down cast
        Assert.Equal(99u, magic.LastRequestSpellId); // fell back to the next learned tier
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
        Assert.Equal(1, coordinator.TryStartCallCount); // exactly once, never retried a second time
    }

    [Fact]
    public void AMidChainSplitThatLeavesPeaceReentersMagicModeBeforeRetrying()
    {
        var items = new FakeItems();
        items.Add(Wand(WandObjectId, wielded: false));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat(); // starts in Peace
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var enchantments = new FakeEnchantments();
        var coordinator = new FakePeaSplitCoordinator();
        coordinator.PollResults.Enqueue(PeaSplitPollResult.Confirmed);
        var machine = new CastStateMachine(
            magic, enchantments, items, equipment, combat, peaSplitCoordinator: coordinator);
        ResolvedSpell spell = ResolvedNeedingComponent(family: 10, spellId: 42, componentWeenieId: 691u);
        magic.RequestResultForSpellId[42] = PluginCastRequestResult.MissingComponents;
        coordinator.OnPoll = result =>
        {
            if (result != PeaSplitPollResult.Confirmed)
                return;
            // PeaSplitter's own remarks: a split leaves peace mid-run — simulated directly here
            // rather than through a real PeaSplitter/ICombatAutomation pair.
            combat.Mode = PluginCombatMode.Peace;
            magic.RequestResultForSpellId.Remove(42);
        };
        machine.Begin([spell], Target, TargetName);

        CastRunResult? result = RunUntil(machine, [], out int ticks, maxTicks: 20, untilSent: magic);

        Assert.Null(result); // still waiting on confirmation once the retried cast is sent
        Assert.True(ticks < 20); // would have timed out on the prep budget without the fix
        Assert.Equal(42u, magic.LastRequestSpellId); // the very same spell, no ladder to fall to
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
        Assert.Equal(2, combat.EnterModeCallCount); // once entering Magic, once again after the split

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
    }

    // -- Completion-time components shortage -----------------------------------------------------

    [Fact]
    public void CompletionMissingComponentsRetriesOneTierDownAndCapsLaterSteps()
    {
        var (machine, magic, _, _) = PreparedMachine();
        ResolvedSpell armor = Resolved(family: 1, tier: 6, spellId: 11);
        ResolvedSpell acid = Resolved(family: 2, tier: 6, spellId: 12);
        ResolvedSpell bludgeoning = Resolved(family: 3, tier: 6, spellId: 13);
        ResolvedSpell blade = Resolved(family: 4, tier: 6, spellId: 14);
        ResolvedSpell fireProt = ResolvedWithLadder(
            "Fire Protection Other", family: 5,
            (SpellId: 205, Name: "Fire Protection Other VI", ManaCost: 0),
            (SpellId: 206, Name: "Fire Protection Other V", ManaCost: 0));
        ResolvedSpell coldProt = ResolvedWithLadder(
            "Cold Protection Other", family: 6,
            (SpellId: 305, Name: "Cold Protection Other VI", ManaCost: 0),
            (SpellId: 306, Name: "Cold Protection Other V", ManaCost: 0));
        ResolvedSpell lightningProt = ResolvedWithLadder(
            "Lightning Protection Other", family: 7,
            (SpellId: 405, Name: "Lightning Protection Other VI", ManaCost: 0),
            (SpellId: 406, Name: "Lightning Protection Other V", ManaCost: 0));

        machine.Begin(
            [armor, acid, bludgeoning, blade, fireProt, coldProt, lightningProt], Target, TargetName);

        // Armor, Acid, Bludgeoning and Blade Protection Other land exactly as they did live.
        uint revision = 0;
        foreach (uint spellId in new uint[] { 11, 12, 13, 14 })
        {
            Assert.Null(machine.Advance(0, []));
            Assert.Equal(spellId, magic.LastRequestSpellId);
            magic.LastCompletion = new PluginCastCompletion(
                Revision: ++revision, SpellId: spellId, TargetObjectId: Target, WeenieError: 0);
            Assert.Null(machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]));
        }

        // Fire Protection Other VI is sent, then its completion carries error 1024.
        Assert.Equal(205u, magic.LastRequestSpellId);
        magic.LastCompletion = new PluginCastCompletion(
            Revision: ++revision, SpellId: 205, TargetObjectId: Target, WeenieError: 0x0400);
        Assert.Null(machine.Advance(0.1, []));

        // Retried one tier down within the very same tick — never recorded as a per-spell failure.
        Assert.Equal(206u, magic.LastRequestSpellId);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);

        magic.LastCompletion = new PluginCastCompletion(
            Revision: ++revision, SpellId: 206, TargetObjectId: Target, WeenieError: 0);
        Assert.Null(machine.Advance(0.1, [Confirm("Fire Protection Other V", TargetName)]));

        // The ceiling caps every later line before its own top tier is ever attempted.
        Assert.Equal(306u, magic.LastRequestSpellId);
        magic.LastCompletion = new PluginCastCompletion(
            Revision: ++revision, SpellId: 306, TargetObjectId: Target, WeenieError: 0);
        Assert.Null(machine.Advance(0.1, [Confirm("Cold Protection Other V", TargetName)]));

        Assert.Equal(406u, magic.LastRequestSpellId);
        magic.LastCompletion = new PluginCastCompletion(
            Revision: ++revision, SpellId: 406, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? result = machine.Advance(0.1, [Confirm("Lightning Protection Other V", TargetName)]);

        Assert.NotNull(result);
        Assert.True(result!.IsSuccess); // not TooManyFailures, not Partial
        Assert.Equal(7, result.Steps.Count);
        Assert.All(result.Steps, step => Assert.Equal(CastOutcome.Cast, step.Outcome));
        Assert.Equal(1, result.ComponentCeilingRung);
    }

    [Fact]
    public void CompletionWeenieError1024WithNoLowerTierEndsAsOutOfComponents()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(
            Revision: 1, SpellId: 9, TargetObjectId: Target, WeenieError: 0x0400);
        CastRunResult? result = machine.Advance(0.5, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Failed, result!.Outcome);
        Assert.Equal(CastFailureKind.MissingComponents, result.Failure!.Value.Kind);
        Assert.Empty(result.Steps); // never attempted anything else, exactly like the refusal path
    }

    // -- RequestStop --------------------------------------------------------------------------

    [Fact]
    public void RequestStopLetsACastAlreadyInFlightResolveBeforeStopping()
    {
        var (machine, magic, enchantments, _) = PreparedMachine();
        machine.Begin(
            [Resolved(family: 10, tier: 1, spellId: 42), Resolved(family: 11, tier: 1, spellId: 43)],
            Target, TargetName);
        magic.Gate = PluginCastGate.Ready;

        Assert.Null(machine.Advance(0, [])); // Strength Other's first cast is now in flight
        machine.RequestStop(RunStopReason.RequesterCancelled);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? result = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Stopped, result!.Outcome);
        Assert.Equal(RunStopReason.RequesterCancelled, result.StopReason);
        CastStep step = Assert.Single(result.Steps); // the second spell was never reached
        Assert.Equal(CastOutcome.Cast, step.Outcome);
        Assert.True(enchantments.Reported); // ReportCast still ran for the in-flight cast
        Assert.Equal(1, machine.TotalCastsLanded);
    }

    /// <summary>A fizzle counts as the current cast resolving too — the stop takes effect before
    /// the retry, not after it.</summary>
    [Fact]
    public void RequestStopTakesEffectAfterAFizzleRatherThanWaitingForARetry()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(family: 10, tier: 1, spellId: 42)], Target, TargetName);
        magic.Gate = PluginCastGate.Ready;

        Assert.Null(machine.Advance(0, []));
        machine.RequestStop(RunStopReason.BotDisabling);

        CastRunResult? result = machine.Advance(0.1, [Fizzle()]);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Stopped, result!.Outcome);
        Assert.Equal(RunStopReason.BotDisabling, result.StopReason);
        Assert.Empty(result.Steps); // a fizzle is not a per-spell failure or a landed cast
    }

    [Fact]
    public void FailedCompletionSkipsThatSpellWithoutWaitingForConfirmation()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 9, TargetObjectId: Target, WeenieError: 999);
        CastRunResult? result = machine.Advance(0.5, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Partial, result!.Outcome);
        CastStep step = Assert.Single(result.Steps);
        Assert.Equal(CastOutcome.Failed, step.Outcome);
        Assert.Equal(CastFailureKind.CastFailed, step.Failure!.Value.Kind);
    }

    [Fact]
    public void FailedCompletionWithAMappedWeenieErrorReportsThePlainTextReason()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 9, TargetObjectId: Target, WeenieError: 0x401);
        CastRunResult? result = machine.Advance(0.5, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Partial, result!.Outcome);
        CastStep step = Assert.Single(result.Steps);
        Assert.Equal(CastFailureKind.CastFailed, step.Failure!.Value.Kind);
        Assert.Equal("I'm out of mana", step.Failure!.Value.Detail);
    }

    [Fact]
    public void FailedCompletionWithAnUnmappedWeenieErrorKeepsTheRawCode()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 9)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 9, TargetObjectId: Target, WeenieError: 999);
        CastRunResult? result = machine.Advance(0.5, []);

        Assert.NotNull(result);
        Assert.Equal(CastRunOutcome.Partial, result!.Outcome);
        CastStep step = Assert.Single(result.Steps);
        Assert.Equal(CastFailureKind.CastFailed, step.Failure!.Value.Kind);
        Assert.Equal("weenie error 999", step.Failure!.Value.Detail);
    }

    [Fact]
    public void ReturnsToPeaceOnceIdleAfterACastingRun()
    {
        var (machine, magic, _, combat) = PreparedMachine();
        machine.Begin([Resolved(10, tier: 1, spellId: 42)], Target, TargetName);
        Assert.Null(machine.Advance(0, []));
        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? finished = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);
        Assert.True(finished!.IsSuccess);

        combat.LastRequestedMode = null;
        Assert.Null(machine.Advance(1d, [])); // idle tick: queue has drained
        Assert.Equal(PluginCombatMode.Peace, combat.LastRequestedMode);

        combat.LastRequestedMode = null;
        Assert.Null(machine.Advance(1d, [])); // best effort: one attempt, not a retry loop
        Assert.Null(combat.LastRequestedMode);
    }

    [Fact]
    public void SelfTargetedCastUsesTheNoTargetOverloadsAndConfirmsFromOnYourself()
    {
        var (machine, magic, enchantments, _) = PreparedMachine();
        machine.Begin([ResolvedSelf(family: 77, tier: 6, spellId: 99)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // gate Ready (self) -> RequestCast(spellId) sent
        Assert.Equal(true, magic.LastRequestWasSelfTargeted);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
        Assert.Equal(1, magic.SelfGateCalls);
        Assert.Equal(0, magic.GateCalls); // the targeted overload was never touched

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 99, TargetObjectId: 0, WeenieError: 0);

        // A confirmation addressed to the requester's name must NOT satisfy a self step.
        CastRunResult? notYet = machine.Advance(0.1, [Confirm("Focus Self VI", TargetName)]);
        Assert.Null(notYet);

        CastRunResult? done = machine.Advance(0.1, [SelfConfirm("Focus Self VI")]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(CastOutcome.Cast, step.Outcome);
        Assert.True(enchantments.Reported);
        Assert.Equal(99u, enchantments.LastSpellId);
    }

    [Fact]
    public void AMixedPlanCastsTheSelfStepBeforeTheTargetedStep()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin(
            [ResolvedSelf(family: 77, tier: 6, spellId: 99), Resolved(family: 10, tier: 1, spellId: 42)],
            Target,
            TargetName);

        Assert.Null(machine.Advance(0, [])); // self step: gate Ready (self) -> RequestCast(spellId)
        Assert.Equal(true, magic.LastRequestWasSelfTargeted);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 99, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Focus Self VI")])); // confirms self, sends targeted
        Assert.Equal(false, magic.LastRequestWasSelfTargeted);

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Equal(2, done.Steps.Count);
        Assert.Equal(CastOutcome.Cast, done.Steps[0].Outcome);
        Assert.Equal(CastOutcome.Cast, done.Steps[1].Outcome);
    }

    [Fact]
    public void SelfStepDoesNotMaskTheOtherStepOfTheSameFamilyForARequester()
    {
        // A self step recorded under the requester's own target id must not make the Other
        // step of the same family look already up.
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin(
            [ResolvedSelf(family: 30, tier: 6, spellId: 99), Resolved(family: 30, tier: 1, spellId: 42)],
            Target,
            TargetName);

        Assert.Null(machine.Advance(0, [])); // self step: gate Ready (self) -> RequestCast(spellId)
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 99, TargetObjectId: 0, WeenieError: 0);
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Focus Self VI")])); // confirms self, sends targeted

        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 2, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Equal(2, done.Steps.Count);
        Assert.Equal(CastOutcome.Cast, done.Steps[0].Outcome);
        Assert.Equal(CastOutcome.Cast, done.Steps[1].Outcome); // must land, not be skipped as already up
    }

    [Fact]
    public void HealthAndStaminaReadStraightThroughFromTheCharacter()
    {
        // The health/stamina bars read these the same way the console already reads
        // CurrentMana/MaxMana.
        var character = new FakeCharacter { CurrentStamina = 55 };
        var (machine, _, _, _) = PreparedMachine(character);

        Assert.Equal(character.CurrentHealth, machine.CurrentHealth);
        Assert.Equal(character.MaxHealth, machine.MaxHealth);
        Assert.Equal(55u, machine.CurrentStamina);
        Assert.Equal(character.MaxStamina, machine.MaxStamina);
    }

    [Fact]
    public void SufficientManaNeverTriggersAnUpkeepBounce()
    {
        var character = new FakeCharacter { CurrentMana = 1000, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);
        machine.Begin([Resolved(10, tier: 1, spellId: 42, manaCost: 50)], Target, TargetName, ManaUpkeepPlan());

        Assert.Null(machine.Advance(0, []));
        Assert.Equal(0, magic.SelfGateCalls); // the upkeep pair was never even gate-checked
        Assert.Equal(1, magic.GateCalls);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Single(done.Steps);
    }

    [Fact]
    public void TheManaWindowKeepsBouncingPastTheLowMarkUntilItReachesTheHighMarkThenResumes()
    {
        var character = new FakeCharacter { CurrentMana = 0, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);
        machine.Begin([Resolved(10, tier: 1, spellId: 42, manaCost: 50)], Target, TargetName, ManaUpkeepPlan());

        Assert.Null(machine.Advance(0, [])); // 0 mana, below the low mark -> bounces first
        Assert.True(magic.LastRequestWasSelfTargeted);
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Stamina to Mana Self I")])); // sends Revitalize Self

        // Partial recovery only (100 of 200, still under the 160 high mark): the window must
        // stay open and bounce again rather than resuming Strength Other early.
        character.CurrentMana = 100;
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Revitalize Self I")])); // sends a 2nd Stamina to Mana Self
        Assert.True(magic.LastRequestWasSelfTargeted);
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Stamina to Mana Self I")])); // sends a 2nd Revitalize Self

        // This cycle clears the high mark: the window closes and Strength Other finally resumes.
        character.CurrentMana = 170;
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Revitalize Self I")]));
        Assert.False(magic.LastRequestWasSelfTargeted); // Strength Other was sent, not a third bounce

        magic.LastCompletion = new PluginCastCompletion(Revision: 5, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Equal(5, done.Steps.Count); // two full bounce cycles, then Strength Other
        Assert.Equal(CastOutcome.Cast, done.Steps[^1].Outcome);
    }

    [Fact]
    public void ManaBounceLowWaterFractionIsPendingUntilTheNextBegin()
    {
        var character = new FakeCharacter { CurrentMana = 110, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);

        machine.ManaBounceLowWaterFraction = 0.6;
        machine.Begin([Resolved(10, tier: 1, spellId: 42, manaCost: 50)], Target, TargetName, ManaUpkeepPlan());

        Assert.Null(machine.Advance(0, [])); // 110 is below the new 120 low mark -> bounces first
        Assert.True(magic.LastRequestWasSelfTargeted);
    }

    [Fact]
    public void ManaBounceHighWaterFractionIsPendingUntilTheNextBeginTopUp()
    {
        var character = new FakeCharacter { CurrentMana = 150 };
        var (machine, _, _, _) = PreparedMachine(character);

        Assert.True(machine.NeedsManaTopUp); // 150 < 160 (default high mark)

        machine.ManaBounceHighWaterFraction = 0.5;
        Assert.True(machine.NeedsManaTopUp); // still the old snapshot, nothing has begun yet

        machine.BeginTopUp(ManaUpkeepPlan());
        Assert.False(machine.NeedsManaTopUp); // 150 is now above the new 100 high mark
    }

    [Fact]
    public void ASpellBetweenTheTwoMarksIsCastDirectlyWithoutOpeningTheWindow()
    {
        var character = new FakeCharacter { CurrentMana = 100, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);
        machine.Begin([Resolved(10, tier: 1, spellId: 42, manaCost: 50)], Target, TargetName, ManaUpkeepPlan());

        Assert.Null(machine.Advance(0, []));
        Assert.False(magic.LastRequestWasSelfTargeted); // Strength Other sent directly
        Assert.Equal(0, magic.SelfGateCalls); // the upkeep pair was never even gate-checked
    }

    [Fact]
    public void NetNegativeManaBouncesTheBoundedNumberOfTimesThenStopsAndReturnsToPeace()
    {
        // "Perpetual" mana only holds with enough Mana Conversion and high enough tiers. Here
        // mana never rises no matter how many times the bot bounces it. It must give up, not spin.
        var character = new FakeCharacter { CurrentMana = 0, CurrentStamina = 100 };
        var (machine, magic, _, combat) = PreparedMachine(character);
        machine.Begin([Resolved(10, tier: 1, spellId: 42, manaCost: 50)], Target, TargetName, ManaUpkeepPlan());

        CastRunResult? result = machine.Advance(0, []);
        string[] cycle = ["Stamina to Mana Self I", "Revitalize Self I"];
        for (int i = 0; result is null && i < 10; i++)
        {
            magic.IsCasting = false; // the server's use-done for the just-sent bounce spell
            result = machine.Advance(0.1, [SelfConfirm(cycle[i % 2])]);
        }

        Assert.NotNull(result);
        Assert.False(result!.IsSuccess);
        Assert.Equal(CastFailureKind.OutOfMana, result.Failure!.Value.Kind);
        Assert.Equal(
            "I tried bouncing my mana back up and it's still not enough", result.Failure!.Value.Detail);
        Assert.Equal(4, magic.SelfGateCalls); // two unproductive bounces, two spells each
        Assert.Equal(0, magic.GateCalls); // Strength Other was never attempted against the requester
        Assert.Equal(4, result.Steps.Count);

        // Best-effort return to peace (a wand was wielded and magic mode entered to run the
        // bounce), same as any other finished or abandoned run.
        combat.LastRequestedMode = null;
        Assert.Null(machine.Advance(1d, []));
        Assert.Equal(PluginCombatMode.Peace, combat.LastRequestedMode);
    }

    [Fact]
    public void BeginTopUpBouncesUntilTheHighMarkThenFinishesWithNoRequesterInvolved()
    {
        var character = new FakeCharacter { CurrentMana = 0, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);

        machine.BeginTopUp(ManaUpkeepPlan());

        Assert.Null(machine.Advance(0, [])); // Stamina to Mana Self sent
        Assert.True(magic.LastRequestWasSelfTargeted);
        Assert.Null(machine.Advance(0.1, [SelfConfirm("Stamina to Mana Self I")])); // Revitalize Self sent

        character.CurrentMana = 170; // clears the high mark (160 of 200)
        CastRunResult? done = machine.Advance(0.1, [SelfConfirm("Revitalize Self I")]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        Assert.Equal(2, done.Steps.Count); // exactly one bounce cycle - no more needed
    }

    [Fact]
    public void BeginTopUpGivesUpBoundedOnANetNegativeCasterRatherThanGrindingForever()
    {
        // Nobody is waiting on a top-up's own outcome, which makes it more important, not less,
        // that it cannot spin forever.
        var character = new FakeCharacter { CurrentMana = 0, CurrentStamina = 100 };
        var (machine, magic, _, combat) = PreparedMachine(character);

        machine.BeginTopUp(ManaUpkeepPlan());

        CastRunResult? result = machine.Advance(0, []);
        string[] cycle = ["Stamina to Mana Self I", "Revitalize Self I"];
        for (int i = 0; result is null && i < 10; i++)
        {
            magic.IsCasting = false;
            result = machine.Advance(0.1, [SelfConfirm(cycle[i % 2])]);
        }

        Assert.NotNull(result); // finished rather than bouncing forever
        Assert.True(result!.IsSuccess); // every cast in the cycle actually landed - it just never
        Assert.Equal(4, magic.SelfGateCalls); // three cycles, two spells each - same bound as
                                               // the in-chain window uses

        combat.LastRequestedMode = null;
        Assert.Null(machine.Advance(1d, []));
        Assert.Equal(PluginCombatMode.Peace, combat.LastRequestedMode); // still returns to peace
    }

    [Fact]
    public void BeginTopUpReentersMagicModeWhenTheCasterHasAlreadyReturnedToPeace()
    {

        var character = new FakeCharacter { CurrentMana = 100, CurrentStamina = 100 };
        var (machine, magic, _, combat) = PreparedMachine(character);

        // The previous chain already wielded and entered magic mode once, then the caster
        // drained back to peace the way TickIdle's own return does between chains.
        machine.Begin([Resolved(10, tier: 1, spellId: 42, manaCost: 0)], Target, TargetName);
        Assert.Null(machine.Advance(0, [])); // wields, confirms magic mode, sends the first cast
        combat.Mode = PluginCombatMode.Peace;
        combat.LastRequestedMode = null;
        character.CurrentMana = 0; // exactly what a heavy chain leaves behind

        machine.BeginTopUp(ManaUpkeepPlan());
        int selfGateCallsBeforeTopUp = magic.SelfGateCalls;

        Assert.Null(machine.Advance(0, [])); // must ask to re-enter magic mode, not cast blind
        Assert.Equal(selfGateCallsBeforeTopUp, magic.SelfGateCalls); // no bounce spell gated yet
        Assert.Equal(PluginCombatMode.Magic, combat.LastRequestedMode); // asked to re-enter

        Assert.Null(machine.Advance(0.1, [])); // Stamina to Mana Self sent
        Assert.Equal(selfGateCallsBeforeTopUp + 1, magic.SelfGateCalls);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);
        Assert.True(magic.LastRequestWasSelfTargeted);
    }

    [Fact]
    public void NoLearnedUpkeepSpellsFailsImmediatelyRatherThanBouncing()
    {
        var character = new FakeCharacter { CurrentMana = 0, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);
        machine.Begin([Resolved(10, tier: 1, spellId: 42, manaCost: 50)], Target, TargetName); // no upkeep plan

        CastRunResult? result = machine.Advance(0, []);

        Assert.NotNull(result);
        Assert.False(result!.IsSuccess);
        Assert.Equal(CastFailureKind.OutOfMana, result.Failure!.Value.Kind);
        Assert.Equal(0, magic.SelfGateCalls);
        Assert.Equal(0, magic.GateCalls);
    }

    [Fact]
    public void AnUnaffordableUpkeepSpellIsAttemptedRatherThanBouncedAgainstItself()
    {
        var character = new FakeCharacter { CurrentMana = 41, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);
        machine.Begin(
            [Resolved(family: 30, tier: 1, spellId: 42, manaCost: 60)],
            Target,
            TargetName,
            ManaUpkeepPlan(staminaToManaCost: 50));

        CastRunResult? result = machine.Advance(0, []);

        Assert.Null(result); // must be waiting on the upkeep cast's own confirmation, not finished
        Assert.Equal(1, magic.SelfGateCalls); // the upkeep spell's own affordability was actually checked with the server's gate
        Assert.True(magic.LastRequestWasSelfTargeted);
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult); // actually sent - the whole point of the bounce
    }

    [Fact]
    public void AnAffordableLowerTierIsPreferredOverBouncingForMana()
    {
        var character = new FakeCharacter { CurrentMana = 40, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 60),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 30));
        machine.Begin([spell], Target, TargetName, ManaUpkeepPlan());

        CastRunResult? result = machine.Advance(0, []); // VI unaffordable -> V cast instead of bouncing

        Assert.Null(result); // waiting on V's own confirmation, not bounced and not finished
        Assert.Equal(0, magic.SelfGateCalls); // the upkeep pair was never even gate-checked
        Assert.False(magic.LastRequestWasSelfTargeted); // Strength Other was cast, not an upkeep spell
        Assert.Equal(PluginCastRequestResult.Sent, magic.LastRequestResult);

        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 43, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? done = machine.Advance(0.1, [Confirm("Strength Other V", TargetName)]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(43u, step.SpellId);
    }

    [Fact]
    public void TheAffordabilityLadderWillNotDropMoreThanTwoRungsAndBouncesInstead()
    {
        var character = new FakeCharacter { CurrentMana = 12, CurrentStamina = 200 };
        var (machine, magic, _, _) = PreparedMachine(character);
        ResolvedSpell spell = ResolvedWithLadder(
            "Item Enchantment Mastery Other", family: 11,
            (SpellId: 60, Name: "Item Enchantment Mastery Other VI", ManaCost: 60),
            (SpellId: 61, Name: "Item Enchantment Mastery Other V", ManaCost: 50),
            (SpellId: 62, Name: "Item Enchantment Mastery Other IV", ManaCost: 40),
            (SpellId: 63, Name: "Item Enchantment Mastery Other I", ManaCost: 10));
        machine.Begin([spell], Target, TargetName, ManaUpkeepPlan());

        machine.Advance(0, []);

        // The bounce ran instead: an upkeep spell was gate-checked and cast, and the
        // nearly-worthless tier I was not what got cast.
        Assert.True(magic.SelfGateCalls > 0);
        Assert.True(magic.LastRequestWasSelfTargeted);
    }

    [Fact]
    public void WhenEveryLearnedTierIsUnaffordableTheBounceStillRuns()
    {
        var character = new FakeCharacter { CurrentMana = 20, CurrentStamina = 100 };
        var (machine, magic, _, _) = PreparedMachine(character);
        ResolvedSpell spell = ResolvedWithLadder(
            "Strength Other", family: 10,
            (SpellId: 42, Name: "Strength Other VI", ManaCost: 60),
            (SpellId: 43, Name: "Strength Other V", ManaCost: 30));
        machine.Begin([spell], Target, TargetName, ManaUpkeepPlan());

        Assert.Null(machine.Advance(0, [])); // neither learned tier fits 20 mana -> bounces
        Assert.True(magic.LastRequestWasSelfTargeted);
        Assert.Equal(1, magic.SelfGateCalls);
    }

    [Fact]
    public void RepeatedOtherCastOfTheSameFamilyStillSkipsTheSecondAttempt()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([Resolved(family: 30, tier: 1, spellId: 42)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 42, TargetObjectId: Target, WeenieError: 0);
        CastRunResult? first = machine.Advance(0.1, [Confirm("Strength Other I", TargetName)]);
        Assert.True(first!.IsSuccess);

        machine.Begin([Resolved(family: 30, tier: 1, spellId: 42)], Target, TargetName);
        CastRunResult? second = machine.Advance(0, []);

        Assert.NotNull(second);
        Assert.True(second!.IsSuccess);
        CastStep step = Assert.Single(second.Steps);
        Assert.Equal(CastOutcome.Skipped, step.Outcome);
    }

    [Fact]
    public void RevitalizeSelfConfirmsFromTheVitalRestoreLine()
    {
        // ACE never says "on yourself" for a vital-restore spell.
        var (machine, magic, enchantments, _) = PreparedMachine();
        machine.Begin([ResolvedSelfVital("Revitalize Self VI", family: 901, spellId: 902)], Target, TargetName);

        Assert.Null(machine.Advance(0, [])); // gate Ready (self) -> RequestCast(spellId) sent
        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 902, TargetObjectId: 0, WeenieError: 0);

        CastRunResult? done = machine.Advance(
            0.1, [VitalRestoreConfirm("Revitalize Self VI", boost: 78, vital: "stamina")]);

        Assert.NotNull(done);
        Assert.True(done!.IsSuccess);
        CastStep step = Assert.Single(done.Steps);
        Assert.Equal(CastOutcome.Cast, step.Outcome);
        Assert.True(enchantments.Reported);
        Assert.Equal(902u, enchantments.LastSpellId);
    }

    [Fact]
    public void AVitalRestoreLineForADifferentSpellDoesNotConfirm()
    {
        var (machine, magic, _, _) = PreparedMachine();
        machine.Begin([ResolvedSelfVital("Revitalize Self VI", family: 901, spellId: 902)], Target, TargetName);

        Assert.Null(machine.Advance(0, []));
        magic.IsCasting = false;
        magic.LastCompletion = new PluginCastCompletion(Revision: 1, SpellId: 902, TargetObjectId: 0, WeenieError: 0);

        CastRunResult? notYet = machine.Advance(
            0.1, [VitalRestoreConfirm("Stamina to Mana Self VI", boost: 40, vital: "mana")]);

        Assert.Null(notYet);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary><paramref name="character"/> defaults to null, which the machine
    /// turns into unlimited mana.</summary>
    private static (CastStateMachine Machine, FakeMagic Magic, FakeEnchantments Enchantments, FakeCombat Combat)
        PreparedMachine(
            ICharacterInfo? character = null,
            bool tierFallbackEnabled = true,
            int fizzleSkipBound = CastStateMachine.DefaultFizzleSkipBound,
            SolrLabs.BuffBot.Stats.BotStats? stats = null,
            IPeaSplitCoordinator? peaSplitCoordinator = null,
            ISpellCatalog? catalog = null)
    {
        var items = new FakeItems();
        items.Add(Wand(WandObjectId, wielded: true));
        var equipment = new FakeEquipment(items);
        var combat = new FakeCombat { Mode = PluginCombatMode.Magic };
        var magic = new FakeMagic { Gate = PluginCastGate.Ready };
        var enchantments = new FakeEnchantments();
        var machine = new CastStateMachine(
            magic, enchantments, items, equipment, combat, character,
            tierFallbackEnabled: tierFallbackEnabled, fizzleSkipBound: fizzleSkipBound, stats: stats,
            peaSplitCoordinator: peaSplitCoordinator, catalog: catalog);
        return (machine, magic, enchantments, combat);
    }

    private static IReadOnlyList<ResolvedSpell> ManaUpkeepPlan(
        int staminaToManaCost = 0, int revitalizeCost = 0) =>
        [
            new(
                "Stamina to Mana Self",
                new PluginSpellInfo(
                    SpellId: 901, Name: "Stamina to Mana Self I", Family: 900, Tier: 1, Difficulty: 0,
                    ManaCost: staminaToManaCost, DurationSeconds: 0f, School: 0, Description: string.Empty,
                    IsSelfTargeted: true, IsBeneficial: true)),
            new(
                "Revitalize Self",
                new PluginSpellInfo(
                    SpellId: 902, Name: "Revitalize Self I", Family: 901, Tier: 1, Difficulty: 0,
                    ManaCost: revitalizeCost, DurationSeconds: 0f, School: 0, Description: string.Empty,
                    IsSelfTargeted: true, IsBeneficial: true)),
        ];

    /// <summary>Mana-upkeep milestone: mutable, unlike the interface's server-fed default,
    /// so a test can move the caster's own vitals mid-run.</summary>
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

    /// <summary>Pumps <paramref name="machine"/> (0.5 s per tick) until <see cref="IMagicCommands.RequestCast"/>
    /// has actually been sent (Fix 1's preparation can take a few ticks), or the run finishes first.</summary>
    private static CastRunResult? RunUntil(
        CastStateMachine machine,
        IReadOnlyList<PluginChatMessage> messages,
        out int ticks,
        int maxTicks,
        FakeMagic untilSent)
    {
        for (ticks = 0; ticks < maxTicks; ticks++)
        {
            CastRunResult? result = machine.Advance(0.5, messages);
            if (result is not null)
                return result;
            if (untilSent.LastRequestResult == PluginCastRequestResult.Sent)
                return null;
        }
        return null;
    }

    private static CastRunResult? RunUntilFinished(
        CastStateMachine machine, IReadOnlyList<PluginChatMessage> messages, int maxTicks)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            CastRunResult? result = machine.Advance(0.5, messages);
            if (result is not null)
                return result;
        }
        return null;
    }

    private static PluginChatMessage Confirm(string spellName, string targetName) =>
        new(
            Sequence: 1,
            SenderObjectId: 0,
            Kind: (int)ChatKind.System,
            Sender: "",
            Text: $"You cast {spellName} on {targetName}",
            ChannelName: "");

    /// <summary>ACE resolves a landed self cast's target name to the literal
    /// "yourself" rather than the target's own name.</summary>
    private static PluginChatMessage SelfConfirm(string spellName) =>
        new(
            Sequence: 1,
            SenderObjectId: 0,
            Kind: (int)ChatKind.System,
            Sender: "",
            Text: $"You cast {spellName} on yourself",
            ChannelName: "");

    /// <summary>ACE's landed vital-restore text has no target clause at all,
    /// unlike the enchantment shape <see cref="SelfConfirm"/> builds.</summary>
    private static PluginChatMessage VitalRestoreConfirm(string spellName, int boost, string vital) =>
        new(
            Sequence: 1,
            SenderObjectId: 0,
            Kind: (int)ChatKind.System,
            Sender: "",
            Text: $"You cast {spellName} and restore {boost} points of your {vital}.",
            ChannelName: "");

    private static PluginChatMessage Fizzle() =>
        new(
            Sequence: 1,
            SenderObjectId: 0,
            Kind: (int)ChatKind.System,
            Sender: "",
            Text: "Your spell fizzled.",
            ChannelName: "");

    private static PluginInventoryItem Wand(uint objectId, bool wielded, string name = "Staff of the Mhoire Forge") =>
        new(
            ObjectId: objectId,
            WeenieClassId: 1,
            Name: name,
            ItemType: 0,
            ContainerObjectId: 0,
            WielderObjectId: wielded ? 1u : 0u,
            ValidLocations: 0,
            EquippedLocation: wielded ? 1u : 0u,
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

    private static ResolvedSpell Resolved(
        uint family, int tier, uint spellId = 1, int manaCost = 0, float durationSeconds = 120f) =>
        new(
            "Strength Other",
            new PluginSpellInfo(
                SpellId: spellId,
                Name: "Strength Other I",
                Family: family,
                Tier: tier,
                Difficulty: 0,
                ManaCost: manaCost,
                DurationSeconds: durationSeconds,
                School: 0,
                Description: string.Empty,
                IsSelfTargeted: false,
                IsBeneficial: true));

    private static ResolvedSpell ResolvedNeedingComponent(
        uint family, uint spellId, uint componentWeenieId, bool selfTargeted = false) =>
        new(
            selfTargeted ? "Focus Self" : "Strength Other",
            new PluginSpellInfo(
                SpellId: spellId,
                Name: selfTargeted ? "Focus Self I" : "Strength Other I",
                Family: family,
                Tier: 1,
                Difficulty: 0,
                ManaCost: 0,
                DurationSeconds: 120f,
                School: 0,
                Description: string.Empty,
                IsSelfTargeted: selfTargeted,
                IsBeneficial: true)
            { FormulaComponentIds = [componentWeenieId] });

    private static ResolvedSpell ResolvedWithLadder(
        string line, uint family, params (uint SpellId, string Name, int ManaCost)[] tiersDescending)
    {
        var ladder = new List<PluginSpellInfo>(tiersDescending.Length);
        foreach ((uint spellId, string name, int manaCost) in tiersDescending)
        {
            ladder.Add(new PluginSpellInfo(
                SpellId: spellId,
                Name: name,
                Family: family,
                Tier: ladder.Count + 1,
                Difficulty: 0,
                ManaCost: manaCost,
                DurationSeconds: 120f,
                School: 0,
                Description: string.Empty,
                IsSelfTargeted: false,
                IsBeneficial: true));
        }

        return new ResolvedSpell(line, ladder[0]) { LearnedTiersDescending = ladder };
    }

    private static ResolvedSpell ResolvedSelfWithLadder(
        string line, uint family, params (uint SpellId, string Name, int ManaCost)[] tiersDescending)
    {
        var ladder = new List<PluginSpellInfo>(tiersDescending.Length);
        foreach ((uint spellId, string name, int manaCost) in tiersDescending)
        {
            ladder.Add(new PluginSpellInfo(
                SpellId: spellId,
                Name: name,
                Family: family,
                Tier: ladder.Count + 1,
                Difficulty: 0,
                ManaCost: manaCost,
                DurationSeconds: 1200f,
                School: 0,
                Description: string.Empty,
                IsSelfTargeted: true,
                IsBeneficial: true));
        }

        return new ResolvedSpell(line, ladder[0]) { LearnedTiersDescending = ladder };
    }

    private static ResolvedSpell ResolvedSelf(uint family, int tier, uint spellId = 1) =>
        new(
            "Focus Self",
            new PluginSpellInfo(
                SpellId: spellId,
                Name: "Focus Self VI",
                Family: family,
                Tier: tier,
                Difficulty: 0,
                ManaCost: 0,
                DurationSeconds: 1200f,
                School: 0,
                Description: string.Empty,
                IsSelfTargeted: true,
                IsBeneficial: true));

    /// <summary>A self-targeted vital-restore step, e.g. <c>Revitalize Self</c> — confirmed by
    /// <see cref="VitalRestoreConfirm"/>'s shape rather than <see cref="SelfConfirm"/>'s.</summary>
    private static ResolvedSpell ResolvedSelfVital(string name, uint family, uint spellId) =>
        new(
            name,
            new PluginSpellInfo(
                SpellId: spellId,
                Name: name,
                Family: family,
                Tier: 6,
                Difficulty: 0,
                ManaCost: 0,
                DurationSeconds: 0f,
                School: 0,
                Description: string.Empty,
                IsSelfTargeted: true,
                IsBeneficial: true));

    private sealed class FakeMagic : IMagicCommands
    {
        internal PluginCastGate Gate { get; set; } = PluginCastGate.Ready;

        /// <summary>Gate for the no-target (self-cast) overload — kept separate from
        /// <see cref="Gate"/> so a test can tell the two paths apart.</summary>
        internal PluginCastGate SelfGate { get; set; } = PluginCastGate.Ready;

        internal PluginCastRequestResult RequestResult { get; set; } = PluginCastRequestResult.Sent;
        internal PluginCastRequestResult? LastRequestResult { get; private set; }
        internal bool? LastRequestWasSelfTargeted { get; private set; }
        internal uint LastRequestSpellId { get; private set; }
        internal int GateCalls { get; private set; }
        internal int SelfGateCalls { get; private set; }

        internal Dictionary<uint, PluginCastRequestResult> RequestResultForSpellId { get; } = [];

        internal uint LastGateTargetObjectId { get; private set; }

        /// <summary>The target object id the last targeted <see cref="RequestCast(uint, uint)"/>
        /// call was sent against — see <see cref="LastGateTargetObjectId"/>'s own remarks.</summary>
        internal uint LastRequestTargetObjectId { get; private set; }

        internal HashSet<uint> RefuseGateFor { get; } = [];

        public bool IsCasting { get; set; }
        public PluginCastCompletion LastCompletion { get; set; }

        public PluginCastGate EvaluateGate(uint spellId)
        {
            SelfGateCalls++;
            return SelfGate;
        }

        public bool Cast(uint spellId) => false;

        public PluginCastGate EvaluateGate(uint spellId, uint targetObjectId)
        {
            GateCalls++;
            LastGateTargetObjectId = targetObjectId;
            return RefuseGateFor.Contains(spellId) ? PluginCastGate.NotKnown : Gate;
        }

        public bool Cast(uint spellId, uint targetObjectId) => false;

        public PluginCastRequestResult RequestCast(uint spellId)
        {
            PluginCastRequestResult result = RequestResultForSpellId.TryGetValue(spellId, out var over)
                ? over
                : RequestResult;
            LastRequestWasSelfTargeted = true;
            LastRequestSpellId = spellId;
            LastRequestResult = result;
            if (result == PluginCastRequestResult.Sent)
                IsCasting = true;
            return result;
        }

        public PluginCastRequestResult RequestCast(uint spellId, uint targetObjectId)
        {
            PluginCastRequestResult result = RequestResultForSpellId.TryGetValue(spellId, out var over)
                ? over
                : RequestResult;
            LastRequestWasSelfTargeted = false;
            LastRequestSpellId = spellId;
            LastRequestTargetObjectId = targetObjectId;
            LastRequestResult = result;
            if (result == PluginCastRequestResult.Sent)
                IsCasting = true;
            return result;
        }
    }

    private sealed class FakeEnchantments : IEnchantmentAutomation
    {
        internal bool Reported { get; private set; }
        internal uint LastTarget { get; private set; }
        internal uint LastSpellId { get; private set; }
        internal double LastDuration { get; private set; }

        public IReadOnlyList<PluginTrackedEnchantment> Capture(uint targetObjectId) =>
            throw new InvalidOperationException(
                "The state machine must not consult the host's own enchantment tracker — it is "
                + "fed by the same optimistic completion signal this fix stops trusting.");

        public bool ReportCast(uint targetObjectId, uint spellId, double durationSeconds)
        {
            Reported = true;
            LastTarget = targetObjectId;
            LastSpellId = spellId;
            LastDuration = durationSeconds;
            return true;
        }
    }

    private sealed class FakeItems : IItemAutomation
    {
        private readonly List<PluginInventoryItem> _items = [];
        private readonly Queue<IReadOnlyList<PluginInventoryItem>> _scriptedCaptures = new();

        internal void Add(PluginInventoryItem item) => _items.Add(item);

        /// <summary>Mirrors ACE's single-slot wield: wielding a new wand unwields
        /// whatever was already in that slot.</summary>
        internal void MarkWielded(uint objectId)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].ObjectClass == PluginObjectClass.WandStaffOrb && _items[i].ObjectId != objectId)
                    _items[i] = _items[i] with { EquippedLocation = 0u, WielderObjectId = 0u };
                if (_items[i].ObjectId == objectId)
                    _items[i] = _items[i] with { EquippedLocation = 1u, WielderObjectId = 1u };
            }
        }

        internal void ScriptCaptures(params IReadOnlyList<PluginInventoryItem>[] snapshots)
        {
            foreach (IReadOnlyList<PluginInventoryItem> snapshot in snapshots)
                _scriptedCaptures.Enqueue(snapshot);
        }

        public IReadOnlyList<PluginInventoryItem> CaptureOwnedItems() =>
            _scriptedCaptures.Count > 0 ? _scriptedCaptures.Dequeue() : _items;
    }

    private sealed class FakeEquipment : IEquipmentAutomation
    {
        private readonly FakeItems _items;
        private readonly Queue<PluginEquipmentCommandResult>? _resultSequence;

        internal FakeEquipment(FakeItems items, Queue<PluginEquipmentCommandResult>? resultSequence = null)
        {
            _items = items;
            _resultSequence = resultSequence;
        }

        internal PluginEquipmentCommandResult NextResult { get; set; } =
            new(PluginEquipmentCommandStatus.Started);
        internal uint LastEquipped { get; private set; }
        internal int EquipCallCount { get; private set; }

        public PluginEquipmentCommandResult Equip(uint objectId, uint requestedLocation = 0u)
        {
            LastEquipped = objectId;
            EquipCallCount++;
            PluginEquipmentCommandResult next = _resultSequence is { Count: > 0 }
                ? _resultSequence.Dequeue()
                : NextResult;
            if (next.Accepted)
                _items.MarkWielded(objectId);
            return next;
        }
    }

    private sealed class FakeCombat : ICombatAutomation
    {
        internal PluginCombatMode Mode { get; set; } = PluginCombatMode.Peace;
        internal PluginCombatCommandResult NextEnterModeResult { get; set; } =
            new(PluginCombatCommandStatus.ModeChangeSent);
        internal PluginCombatMode? LastRequestedMode { get; set; }
        internal int EnterModeCallCount { get; private set; }

        public PluginCombatSnapshot Snapshot => new(
            0u, Mode, PluginAttackHeight.Medium, 0f, 0f, false, false, false, false);

        public IReadOnlyList<PluginCombatTarget> CaptureHostileTargets(float maximumDistance) =>
            Array.Empty<PluginCombatTarget>();

        public PluginCombatCommandResult EnterDefaultMode() => new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult EnterMode(PluginCombatMode mode)
        {
            EnterModeCallCount++;
            LastRequestedMode = mode;
            if (NextEnterModeResult.Accepted)
                Mode = mode;
            return NextEnterModeResult;
        }

        public PluginCombatCommandResult BeginPhysicalAttack(
            uint targetObjectId, PluginAttackHeight height, float power) =>
            new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult ReleasePhysicalAttack() =>
            new(PluginCombatCommandStatus.Unavailable);

        public PluginCombatCommandResult AbortPhysicalAttack() =>
            new(PluginCombatCommandStatus.Unavailable);
    }

    private sealed class FakePeaSplitCoordinator : IPeaSplitCoordinator
    {
        internal bool NextTryStartResult { get; set; } = true;
        internal Queue<PeaSplitPollResult> PollResults { get; } = new();
        internal int TryStartCallCount { get; private set; }
        internal uint? LastComponentWeenieId { get; private set; }

        internal Action<PeaSplitPollResult>? OnPoll { get; set; }

        public bool TryStart(uint componentWeenieId, string reason)
        {
            TryStartCallCount++;
            LastComponentWeenieId = componentWeenieId;
            return NextTryStartResult;
        }

        public PeaSplitPollResult Poll(double deltaSeconds)
        {
            PeaSplitPollResult result = PollResults.Count > 0 ? PollResults.Dequeue() : PeaSplitPollResult.Pending;
            OnPoll?.Invoke(result);
            return result;
        }
    }

    private sealed class FakeSpellCatalog : ISpellCatalog
    {
        private readonly IReadOnlyDictionary<uint, uint> _weenieClassIdByComponentId;

        internal FakeSpellCatalog(IReadOnlyDictionary<uint, uint> weenieClassIdByComponentId) =>
            _weenieClassIdByComponentId = weenieClassIdByComponentId;

        public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs => Array.Empty<PluginSpellInfo>();

        public bool TryGet(uint spellId, out PluginSpellInfo info)
        {
            info = default;
            return false;
        }

        public bool TryGetComponent(uint componentId, out PluginSpellComponentInfo info)
        {
            if (!_weenieClassIdByComponentId.TryGetValue(componentId, out uint weenieClassId))
            {
                info = default;
                return false;
            }
            info = new PluginSpellComponentInfo(componentId, weenieClassId, string.Empty, 0, 0, 0, 0, 0, "", "");
            return true;
        }
    }
}
