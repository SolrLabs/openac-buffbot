using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests;

/// <summary>Host-free coverage of <see cref="BuffBotPlugin.BuildDisabledStatus"/>; the rest
/// of <see cref="BuffBotPlugin"/> touches the host directly.</summary>
public sealed class BuffBotPluginTests
{
    private static readonly BotStatsSnapshot EmptyStats = new BotStats().Snapshot();

    /// <summary>A caster whose vitals are all distinct from zero, so a test can tell a real
    /// reading apart from a hardcoded one.</summary>
    private sealed class FakeCharacter : ICharacterInfo
    {
        internal bool InWorld { get; set; } = true;
        public bool IsInWorld => InWorld;
        public uint ObjectId => 1;
        public uint CurrentHealth => 80;
        public uint MaxHealth => 100;
        public uint CurrentStamina => 90;
        public uint MaxStamina => 120;
        public uint CurrentMana => 340;
        public uint MaxMana => 500;
        public IReadOnlyList<PluginSkillInfo> Skills => Array.Empty<PluginSkillInfo>();
        public IReadOnlyList<PluginAttributeInfo> Attributes => Array.Empty<PluginAttributeInfo>();
        public IReadOnlyList<PluginActiveEnchantment> ActiveEnchantments => Array.Empty<PluginActiveEnchantment>();

        public bool TryGetSkill(uint skillId, out PluginSkillInfo skill)
        {
            skill = default;
            return false;
        }
    }

    private static readonly FakeCharacter InWorldCharacter = new();

    [Fact]
    public void DisabledStatusReportsDisabledIdleAndAnEmptyQueue()
    {
        BuffBotStatus status = BuffBotPlugin.BuildDisabledStatus(
            InWorldCharacter, muted: [], lastCounters: default, tellsAnswered: 0, EmptyStats);

        Assert.False(status.Enabled);
        Assert.Equal(BotActivity.Idle, status.Activity);
        Assert.Null(status.CurrentRequesterName);
        Assert.Empty(status.Waiting);
    }

    [Fact]
    public void DisabledStatusCarriesTheRealMutedListThrough()
    {
        var muted = new[] { new MutedEntry(7, "Nuisance", Until: null, IsManual: true) };

        BuffBotStatus status = BuffBotPlugin.BuildDisabledStatus(
            InWorldCharacter, muted, lastCounters: default, tellsAnswered: 0, EmptyStats);

        Assert.Same(muted, status.Muted);
    }

    [Fact]
    public void DisabledStatusKeepsThePriorCastCountersAndUpdatesOnlyTellsAnswered()
    {
        var lastCounters = new SessionCounters(
            TellsAnswered: 3, CastsLanded: 9, Fizzles: 2, ManaBounces: 1, TierStepDowns: 1);

        BuffBotStatus status = BuffBotPlugin.BuildDisabledStatus(
            InWorldCharacter, muted: [], lastCounters, tellsAnswered: 5, EmptyStats);

        Assert.Equal(5, status.Counters.TellsAnswered);
        Assert.Equal(9, status.Counters.CastsLanded);
        Assert.Equal(2, status.Counters.Fizzles);
        Assert.Equal(1, status.Counters.ManaBounces);
        Assert.Equal(1, status.Counters.TierStepDowns);
    }

    /// <summary>An inert bot still reports the caster's real vitals.</summary>
    [Fact]
    public void DisabledStatusCarriesTheRealVitalsWhenTheCharacterIsInWorld()
    {
        BuffBotStatus status = BuffBotPlugin.BuildDisabledStatus(
            InWorldCharacter, muted: [], lastCounters: default, tellsAnswered: 0, EmptyStats);

        Assert.Equal(340u, status.CurrentMana);
        Assert.Equal(500u, status.MaxMana);
        Assert.Equal(80u, status.CurrentHealth);
        Assert.Equal(100u, status.MaxHealth);
        Assert.Equal(90u, status.CurrentStamina);
        Assert.Equal(120u, status.MaxStamina);
    }

    /// <summary>Out of the world, vitals are null — a dash on the console rather than a number
    /// that could pass for a reading.</summary>
    [Fact]
    public void DisabledStatusShowsNoHealthOrStaminaWhenTheCharacterIsNotInWorld()
    {
        var character = new FakeCharacter { InWorld = false };

        BuffBotStatus status = BuffBotPlugin.BuildDisabledStatus(
            character, muted: [], lastCounters: default, tellsAnswered: 0, EmptyStats);

        Assert.Null(status.CurrentHealth);
        Assert.Null(status.MaxHealth);
        Assert.Null(status.CurrentStamina);
        Assert.Null(status.MaxStamina);
    }

    /// <summary>The exact pipeline <c>OnTick</c> feeds to the mesh node: <see
    /// cref="BuffBotPlugin.BuildDisabledStatus"/> into <see cref="MeshStatusMapper.From"/>.</summary>
    [Fact]
    public void DisabledStatusMapsOntoTheWireAsEnabledFalseAndIdle()
    {
        BuffBotStatus status = BuffBotPlugin.BuildDisabledStatus(
            InWorldCharacter,
            muted: [new MutedEntry(9, "Nuisance", Until: null, IsManual: true)],
            lastCounters: new SessionCounters(TellsAnswered: 2, CastsLanded: 4, Fizzles: 0, ManaBounces: 0, TierStepDowns: 0),
            tellsAnswered: 3,
            EmptyStats);

        MeshStatus wire = MeshStatusMapper.From(status, new FakeClock());

        Assert.False(wire.Enabled);
        Assert.Equal("idle", wire.Activity);
        Assert.Empty(wire.Waiting);
        Assert.Equal(3, wire.Counters.TellsAnswered);
        Assert.Equal(4, wire.Counters.CastsLanded);
        Assert.Equal(340u, wire.CurrentMana);
        Assert.Equal(80u, wire.CurrentHealth);
    }

    /// <summary>An hourly bucket's fizzle count maps field for field, like its other counts.
    /// </summary>
    [Fact]
    public void AnHourlyBucketsFizzleCountCarriesThroughToTheWire()
    {
        BuffBotStatus status = BuffBotPlugin.BuildDisabledStatus(
            InWorldCharacter, muted: [], lastCounters: default, tellsAnswered: 0, EmptyStats);
        SessionStatsSnapshot session = EmptyStats.Session;
        HourlyBucket hour = new(new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero), Requested: 5, Upkeep: 2, Fizzles: 3);
        status = status with { Stats = session with { Hours = [hour] } };

        MeshStatus wire = MeshStatusMapper.From(status, new FakeClock());

        MeshHourlyBucket wireHour = Assert.Single(wire.Stats.Hours);
        Assert.Equal(5, wireHour.Requested);
        Assert.Equal(2, wireHour.Upkeep);
        Assert.Equal(3, wireHour.Fizzles);
    }

    [Fact]
    public void FirstDistanceForARequesterIsAlwaysWorthTracing()
    {
        Assert.True(BuffBotPlugin.DistanceTraceIsWorthLogging(30d, inRange: true, lastTraced: null));
    }

    [Fact]
    public void RepeatingTheSameDistanceAndVerdictIsNotWorthTracingAgain()
    {
        // The repeated-identical-line case: an unchanged distance is not worth a line.
        Assert.False(BuffBotPlugin.DistanceTraceIsWorthLogging(30d, inRange: true, lastTraced: (30d, true)));
    }

    [Fact]
    public void ANearlyIdenticalDistanceUnderAMeterIsNotWorthTracingAgain()
    {
        Assert.False(BuffBotPlugin.DistanceTraceIsWorthLogging(30.4d, inRange: true, lastTraced: (30d, true)));
    }

    [Fact]
    public void MovingAFullMeterOrMoreIsWorthTracingAgain()
    {
        Assert.True(BuffBotPlugin.DistanceTraceIsWorthLogging(31d, inRange: true, lastTraced: (30d, true)));
    }

    [Fact]
    public void TheInRangeVerdictFlippingIsWorthTracingEvenWithoutMoving()
    {
        Assert.True(BuffBotPlugin.DistanceTraceIsWorthLogging(30d, inRange: false, lastTraced: (30d, true)));
    }
}
