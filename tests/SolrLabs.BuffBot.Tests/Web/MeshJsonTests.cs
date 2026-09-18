using System.Text.Json.Nodes;
using SolrLabs.BuffBot.Casting;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Stats;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>Host-free coverage of the wire shape: field names, camelCase, explicit nulls, the
/// activity names, and the round trip for every body the mesh sends or receives.</summary>
public sealed class MeshJsonTests
{
    private static readonly BotStatsSnapshot EmptyStats = new BotStats().Snapshot();

    private static readonly MeshStatus SampleStatus = new(
        Enabled: true,
        Activity: "serving",
        CurrentRequesterName: "Archer",
        CurrentRequesterObjectId: 77u,
        CurrentSpellLine: "Strength Other",
        CurrentSpellId: 0x10E4u,
        StepIndex: 2,
        StepCount: 5,
        CurrentMana: 500u,
        MaxMana: 1000u,
        Waiting: [new MeshWaitingEntry(11, "Probe", "heavy", 12.5)],
        Counters: new MeshCounters(5, 4, 1, 0, 0),
        Stats: MeshStats.Empty,
        Recent: Array.Empty<MeshRecentEvent>(),
        Settings: new MeshSettings(true, 67.5, 12, false, null, true, 6, 25, 0.20, 0.80),
        Components: new MeshComponents(true, [new MeshComponentItem(5000, "Tiger Eye Agate", 24, 2)]),
        CurrentHealth: 800u,
        MaxHealth: 1000u,
        CurrentStamina: 600u,
        MaxStamina: 900u,
        TradeOpen: true,
        DonationsCompleted: 3,
        DonationItemsReceived: 7);

    [Fact]
    public void StatusJsonMatchesTheWireShapeExactly()
    {
        JsonObject json = MeshJson.Status(SampleStatus);

        Assert.True(json["enabled"]!.GetValue<bool>());
        Assert.Equal("serving", json["activity"]!.GetValue<string>());
        Assert.Equal("Archer", json["currentRequesterName"]!.GetValue<string>());
        Assert.Equal(77u, json["currentRequesterObjectId"]!.GetValue<uint>());
        Assert.Equal("Strength Other", json["currentSpellLine"]!.GetValue<string>());
        Assert.Equal(0x10E4u, json["currentSpellId"]!.GetValue<uint>());
        Assert.Equal(2, json["stepIndex"]!.GetValue<int>());
        Assert.Equal(5, json["stepCount"]!.GetValue<int>());
        Assert.Equal(500u, json["currentMana"]!.GetValue<uint>());
        Assert.Equal(1000u, json["maxMana"]!.GetValue<uint>());
        Assert.Equal(800u, json["currentHealth"]!.GetValue<uint>());
        Assert.Equal(1000u, json["maxHealth"]!.GetValue<uint>());
        Assert.Equal(600u, json["currentStamina"]!.GetValue<uint>());
        Assert.Equal(900u, json["maxStamina"]!.GetValue<uint>());
        Assert.True(json["tradeOpen"]!.GetValue<bool>());
        Assert.Equal(3, json["donationsCompleted"]!.GetValue<int>());
        Assert.Equal(7, json["donationItemsReceived"]!.GetValue<int>());

        JsonObject waiting = Assert.IsType<JsonObject>(json["waiting"]![0]);
        Assert.Equal(11u, waiting["objectId"]!.GetValue<uint>());
        Assert.Equal("Probe", waiting["name"]!.GetValue<string>());
        Assert.Equal("heavy", waiting["archetype"]!.GetValue<string>());
        Assert.Equal(12.5, waiting["waitingSeconds"]!.GetValue<double>());

        JsonObject counters = Assert.IsType<JsonObject>(json["counters"]);
        Assert.Equal(5, counters["tellsAnswered"]!.GetValue<int>());
        Assert.Equal(4, counters["castsLanded"]!.GetValue<int>());
        Assert.Equal(1, counters["fizzles"]!.GetValue<int>());
        Assert.Equal(0, counters["manaBounces"]!.GetValue<int>());
        Assert.Equal(0, counters["tierStepDowns"]!.GetValue<int>());

        JsonObject settings = Assert.IsType<JsonObject>(json["settings"]);
        Assert.True(settings["selfBuffUpkeep"]!.GetValue<bool>());
        Assert.Equal(67.5, settings["refusalRangeMeters"]!.GetValue<double>());
        Assert.Equal(12, settings["repliesPerSenderPerMinute"]!.GetValue<int>());
        Assert.False(settings["intakePaused"]!.GetValue<bool>());
        Assert.True(settings.ContainsKey("targetTier"));
        Assert.Null(settings["targetTier"]);
        Assert.True(settings["tierFallback"]!.GetValue<bool>());
        Assert.Equal(6, settings["fizzlesBeforeSkip"]!.GetValue<int>());
        Assert.Equal(25, settings["componentLowStock"]!.GetValue<int>());
        Assert.Equal(0.20, settings["manaBounceLowWaterFraction"]!.GetValue<double>());
        Assert.Equal(0.80, settings["manaBounceHighWaterFraction"]!.GetValue<double>());

        JsonObject components = Assert.IsType<JsonObject>(json["components"]);
        Assert.True(components["available"]!.GetValue<bool>());
        Assert.True(components["catalog"]!.GetValue<bool>());
        JsonObject componentItem = Assert.IsType<JsonObject>(components["items"]![0]);
        Assert.Equal(5000u, componentItem["weenieClassId"]!.GetValue<uint>());
        Assert.Equal("Tiger Eye Agate", componentItem["name"]!.GetValue<string>());
        Assert.Equal(24, componentItem["stock"]!.GetValue<int>());
        Assert.Equal(2, componentItem["usedBy"]!.GetValue<int>());
    }

    [Fact]
    public void StatusJsonWritesAnUnavailableComponentsBlockAsAnEmptyItemsArray()
    {
        MeshStatus unavailable = SampleStatus with { Components = new MeshComponents(false, []) };

        JsonObject json = MeshJson.Status(unavailable);

        JsonObject components = Assert.IsType<JsonObject>(json["components"]);
        Assert.False(components["available"]!.GetValue<bool>());
        Assert.Empty((JsonArray)components["items"]!);
    }

    [Fact]
    public void StatusJsonWritesTargetTierAsANumberWhenItIsSet()
    {
        MeshStatus withTier = SampleStatus with { Settings = SampleStatus.Settings with { TargetTier = 4 } };

        JsonObject json = MeshJson.Status(withTier);

        Assert.Equal(4, ((JsonObject)json["settings"]!)["targetTier"]!.GetValue<int>());
    }

    [Fact]
    public void StatusJsonWritesNullsExplicitlyRatherThanOmittingTheProperty()
    {
        MeshStatus idle = SampleStatus with
        {
            Activity = "idle",
            CurrentRequesterName = null,
            CurrentRequesterObjectId = null,
            CurrentSpellLine = null,
        };

        JsonObject json = MeshJson.Status(idle);

        Assert.True(json.ContainsKey("currentRequesterName"));
        Assert.Null(json["currentRequesterName"]);
        Assert.True(json.ContainsKey("currentRequesterObjectId"));
        Assert.Null(json["currentRequesterObjectId"]);
        Assert.True(json.ContainsKey("currentSpellLine"));
        Assert.Null(json["currentSpellLine"]);
    }

    [Fact]
    public void MapperNamesEveryActivity()
    {
        Assert.Equal("idle", MapActivity(BotActivity.Idle));
        Assert.Equal("serving", MapActivity(BotActivity.Serving));
        Assert.Equal("selfBuffing", MapActivity(BotActivity.SelfBuffing));
        Assert.Equal("toppingUp", MapActivity(BotActivity.ToppingUp));
    }

    private static string MapActivity(BotActivity activity)
    {
        BuffBotStatus status = new(
            Enabled: true, Activity: activity, CurrentRequesterName: null, CurrentRequesterObjectId: null,
            CurrentSpellLine: null, CurrentSpellId: 0u, StepIndex: 0, StepCount: 0, CurrentMana: 0u, MaxMana: 0u,
            Waiting: [], Muted: [], Counters: new SessionCounters(0, 0, 0, 0, 0),
            Stats: EmptyStats.Session, Recent: EmptyStats.Recent);

        return MeshStatusMapper.From(status, new FakeClock()).Activity;
    }

    [Fact]
    public void BotsResponseCarriesSchemaHubIdAndEveryBot()
    {
        var snapshot = new MeshBotsSnapshot(
            "local/1", [new MeshBot("local/1", "Alice", "local", true, false, 0.5, SampleStatus)]);

        string json = MeshJson.BotsResponse(snapshot);
        JsonObject root = (JsonObject)JsonNode.Parse(json)!;

        Assert.Equal(1, root["schema"]!.GetValue<int>());
        Assert.Equal("local/1", root["hubBotId"]!.GetValue<string>());
        JsonObject bot = (JsonObject)root["bots"]![0]!;
        Assert.Equal("local/1", bot["botId"]!.GetValue<string>());
        Assert.Equal("Alice", bot["name"]!.GetValue<string>());
        Assert.Equal("local", bot["world"]!.GetValue<string>());
        Assert.True(bot["isHub"]!.GetValue<bool>());
        Assert.False(bot["stale"]!.GetValue<bool>());
        Assert.Equal(0.5, bot["lastSeenSeconds"]!.GetValue<double>());
        Assert.NotNull(bot["status"]);
    }

    [Fact]
    public void AcceptedJsonIsTheLiteralWireShape()
    {
        Assert.Equal("{\"accepted\":true}", MeshJson.Accepted());
    }

    [Fact]
    public void ParsesAValidMuteCommand()
    {
        MeshCommand? command = MeshJson.TryParseCommand("{\"kind\":\"mute\",\"objectId\":7}");

        Assert.NotNull(command);
        Assert.Equal(MeshCommandKind.Mute, command!.Kind);
        Assert.Equal(7u, command.ObjectId);
    }

    [Fact]
    public void ParsesAValidReleaseCommand()
    {
        MeshCommand? command = MeshJson.TryParseCommand("{\"kind\":\"release\",\"objectId\":8}");

        Assert.NotNull(command);
        Assert.Equal(MeshCommandKind.Release, command!.Kind);
        Assert.Equal(8u, command.ObjectId);
    }

    [Fact]
    public void ParsesAnEnableCommandWithNoObjectId()
    {
        MeshCommand? command = MeshJson.TryParseCommand("{\"kind\":\"enable\"}");

        Assert.NotNull(command);
        Assert.Equal(MeshCommandKind.Enable, command!.Kind);
        Assert.Null(command.ObjectId);
    }

    [Fact]
    public void ParsesADisableCommandWithNoObjectId()
    {
        MeshCommand? command = MeshJson.TryParseCommand("{\"kind\":\"disable\"}");

        Assert.NotNull(command);
        Assert.Equal(MeshCommandKind.Disable, command!.Kind);
        Assert.Null(command.ObjectId);
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("disable")]
    public void EnableAndDisableCommandsRoundTripThroughTheHeartbeatResponse(string wireKind)
    {
        MeshCommandKind kind = wireKind == "enable" ? MeshCommandKind.Enable : MeshCommandKind.Disable;
        IReadOnlyList<MeshCommand> commands = [new MeshCommand(kind, null)];

        string json = MeshJson.HeartbeatResponse(commands);
        IReadOnlyList<MeshCommand>? parsed = MeshJson.TryParseHeartbeatResponse(json);

        Assert.Contains($"\"kind\":\"{wireKind}\"", json);
        Assert.NotNull(parsed);
        Assert.Equal(commands, parsed);
    }

    [Theory]
    [InlineData("{\"kind\":\"mute\"}")] // missing objectId on a kind that requires one
    [InlineData("{\"kind\":\"nonsense\",\"objectId\":1}")] // unknown kind
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"kind\":\"settings\"}")] // missing the settings object
    [InlineData("{\"kind\":\"settings\",\"settings\":\"nope\"}")] // settings is not an object
    [InlineData("{\"kind\":\"settings\",\"settings\":{\"refusalRangeMeters\":\"far\"}}")] // wrong type
    [InlineData("{\"kind\":\"settings\",\"settings\":{\"targetTier\":\"four\"}}")] // wrong type, nullable field
    [InlineData("{\"kind\":\"settings\",\"settings\":{\"manaBounceLowWaterFraction\":\"low\"}}")] // wrong type
    public void RejectsAMalformedCommandBody(string json) => Assert.Null(MeshJson.TryParseCommand(json));

    [Fact]
    public void ParsesADrainCommandWithNoObjectId()
    {
        MeshCommand? command = MeshJson.TryParseCommand("{\"kind\":\"drain\"}");

        Assert.NotNull(command);
        Assert.Equal(MeshCommandKind.Drain, command!.Kind);
        Assert.Null(command.ObjectId);
    }

    [Fact]
    public void ParsesASettingsCommandWithOnlyThePresentFields()
    {
        MeshCommand? command = MeshJson.TryParseCommand(
            "{\"kind\":\"settings\",\"settings\":{\"selfBuffUpkeep\":false,\"targetTier\":4}}");

        Assert.NotNull(command);
        Assert.Equal(MeshCommandKind.Settings, command!.Kind);
        MeshSettingsPatch patch = command.Settings!;
        Assert.Equal(false, patch.SelfBuffUpkeep);
        Assert.Null(patch.RefusalRangeMeters);
        Assert.True(patch.HasTargetTier);
        Assert.Equal(4, patch.TargetTier);
        Assert.Null(patch.TierFallback);
    }

    [Fact]
    public void ParsesASettingsCommandThatExplicitlySetsTargetTierBackToTopLearned()
    {
        MeshCommand? command = MeshJson.TryParseCommand(
            "{\"kind\":\"settings\",\"settings\":{\"targetTier\":null}}");

        Assert.NotNull(command);
        MeshSettingsPatch patch = command!.Settings!;
        Assert.True(patch.HasTargetTier);
        Assert.Null(patch.TargetTier);
    }

    [Fact]
    public void ParsesASettingsCommandWithComponentLowStock()
    {
        MeshCommand? command = MeshJson.TryParseCommand(
            "{\"kind\":\"settings\",\"settings\":{\"componentLowStock\":500}}");

        Assert.NotNull(command);
        MeshSettingsPatch patch = command!.Settings!;
        Assert.Equal(500, patch.ComponentLowStock);
    }

    [Fact]
    public void ParsesASettingsCommandWithManaBounceFractions()
    {
        MeshCommand? command = MeshJson.TryParseCommand(
            "{\"kind\":\"settings\",\"settings\":{\"manaBounceLowWaterFraction\":0.3,\"manaBounceHighWaterFraction\":0.9}}");

        Assert.NotNull(command);
        MeshSettingsPatch patch = command!.Settings!;
        Assert.Equal(0.3, patch.ManaBounceLowWaterFraction);
        Assert.Equal(0.9, patch.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void ParsesAnEmptySettingsPatchAsANoOp()
    {
        MeshCommand? command = MeshJson.TryParseCommand("{\"kind\":\"settings\",\"settings\":{}}");

        Assert.NotNull(command);
        MeshSettingsPatch patch = command!.Settings!;
        Assert.Null(patch.SelfBuffUpkeep);
        Assert.False(patch.HasTargetTier);
        Assert.Null(patch.ManaBounceLowWaterFraction);
        Assert.Null(patch.ManaBounceHighWaterFraction);
    }

    [Fact]
    public void SettingsCommandRoundTripsThroughTheHeartbeatResponse()
    {
        var patch = new MeshSettingsPatch(
            SelfBuffUpkeep: null, RefusalRangeMeters: 50d, RepliesPerSenderPerMinute: 8,
            IntakePaused: true, HasTargetTier: true, TargetTier: null, TierFallback: false,
            FizzlesBeforeSkip: 4, ComponentLowStock: 500,
            ManaBounceLowWaterFraction: 0.3, ManaBounceHighWaterFraction: 0.9);
        IReadOnlyList<MeshCommand> commands = [new MeshCommand(MeshCommandKind.Settings, null, patch)];

        string json = MeshJson.HeartbeatResponse(commands);
        IReadOnlyList<MeshCommand>? parsed = MeshJson.TryParseHeartbeatResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal(commands, parsed);
    }

    [Fact]
    public void HeartbeatRoundTripsThroughItsOwnWriterAndReader()
    {
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", SampleStatus, nodeStartedUtc);

        (string BotId, string Name, string World, MeshStatus Status, DateTimeOffset NodeStartedUtc)? parsed =
            MeshJson.TryParseHeartbeat(json);

        Assert.NotNull(parsed);
        Assert.Equal("local/1", parsed!.Value.BotId);
        Assert.Equal("Alice", parsed.Value.Name);
        Assert.Equal("local", parsed.Value.World);
        Assert.Equal(nodeStartedUtc, parsed.Value.NodeStartedUtc);
        Assert.Equal(SampleStatus.Enabled, parsed.Value.Status.Enabled);
        Assert.Equal(SampleStatus.Activity, parsed.Value.Status.Activity);
        Assert.Equal(SampleStatus.CurrentRequesterName, parsed.Value.Status.CurrentRequesterName);
        Assert.Equal(SampleStatus.CurrentRequesterObjectId, parsed.Value.Status.CurrentRequesterObjectId);
        Assert.Equal(SampleStatus.CurrentSpellId, parsed.Value.Status.CurrentSpellId);
        Assert.Equal(SampleStatus.Waiting, parsed.Value.Status.Waiting);
        Assert.Equal(SampleStatus.Counters, parsed.Value.Status.Counters);
        Assert.Equal(SampleStatus.Settings, parsed.Value.Status.Settings);
        Assert.Equal(SampleStatus.Components.Available, parsed.Value.Status.Components.Available);
        Assert.Equal(SampleStatus.Components.Items, parsed.Value.Status.Components.Items);
        Assert.Equal(SampleStatus.Components.CatalogAvailable, parsed.Value.Status.Components.CatalogAvailable);
        Assert.Equal(SampleStatus.TradeOpen, parsed.Value.Status.TradeOpen);
        Assert.Equal(SampleStatus.DonationsCompleted, parsed.Value.Status.DonationsCompleted);
        Assert.Equal(SampleStatus.DonationItemsReceived, parsed.Value.Status.DonationItemsReceived);
    }

    /// <summary>Additive on schema 1: absent on a body written before these fields existed, and
    /// that always meant no trade open and nothing donated yet.</summary>
    [Fact]
    public void TradeAndDonationFieldsDefaultWhenAbsent()
    {
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", SampleStatus, nodeStartedUtc);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject status = (JsonObject)root["status"]!;
        status.Remove("tradeOpen");
        status.Remove("donationsCompleted");
        status.Remove("donationItemsReceived");

        var parsed = MeshJson.TryParseHeartbeat(root.ToJsonString());

        Assert.NotNull(parsed);
        Assert.False(parsed!.Value.Status.TradeOpen);
        Assert.Equal(0, parsed.Value.Status.DonationsCompleted);
        Assert.Equal(0, parsed.Value.Status.DonationItemsReceived);
    }

    /// <summary>The wire's third components state — ids exist, none resolved through the magic
    /// catalog — round-trips as its own value, not folded into <c>available</c>.</summary>
    [Fact]
    public void ComponentsCatalogUnavailableRoundTripsThroughTheHeartbeat()
    {
        MeshStatus withCatalogGap = SampleStatus with
        {
            Components = new MeshComponents(true, Array.Empty<MeshComponentItem>(), CatalogAvailable: false),
        };
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", withCatalogGap, nodeStartedUtc);

        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject components = (JsonObject)((JsonObject)root["status"]!)["components"]!;
        Assert.False(components["catalog"]!.GetValue<bool>());

        var parsed = MeshJson.TryParseHeartbeat(json);
        Assert.NotNull(parsed);
        Assert.False(parsed!.Value.Status.Components.CatalogAvailable);
    }

    /// <summary>Additive on schema 1: a body written before this field existed has no
    /// <c>catalog</c> key at all, and that always meant "available".</summary>
    [Fact]
    public void ComponentsCatalogDefaultsToAvailableWhenTheFieldIsAbsent()
    {
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", SampleStatus, nodeStartedUtc);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        ((JsonObject)((JsonObject)root["status"]!)["components"]!).Remove("catalog");

        var parsed = MeshJson.TryParseHeartbeat(root.ToJsonString());

        Assert.NotNull(parsed);
        Assert.True(parsed!.Value.Status.Components.CatalogAvailable);
    }

    /// <summary>An older build's body that never sent health/stamina renders as a missing
    /// field rather than failing to parse the rest of the body.</summary>
    [Fact]
    public void VitalsDefaultToNullWhenTheFieldsAreAbsent()
    {
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", SampleStatus, nodeStartedUtc);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject status = (JsonObject)root["status"]!;
        status.Remove("currentHealth");
        status.Remove("maxHealth");
        status.Remove("currentStamina");
        status.Remove("maxStamina");

        var parsed = MeshJson.TryParseHeartbeat(root.ToJsonString());

        Assert.NotNull(parsed);
        Assert.Null(parsed!.Value.Status.CurrentHealth);
        Assert.Null(parsed.Value.Status.MaxHealth);
        Assert.Null(parsed.Value.Status.CurrentStamina);
        Assert.Null(parsed.Value.Status.MaxStamina);
    }

    [Fact]
    public void VitalsRoundTripThroughTheHeartbeatWhenPresent()
    {
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", SampleStatus, nodeStartedUtc);

        var parsed = MeshJson.TryParseHeartbeat(json);

        Assert.NotNull(parsed);
        Assert.Equal(SampleStatus.CurrentHealth, parsed!.Value.Status.CurrentHealth);
        Assert.Equal(SampleStatus.MaxHealth, parsed.Value.Status.MaxHealth);
        Assert.Equal(SampleStatus.CurrentStamina, parsed.Value.Status.CurrentStamina);
        Assert.Equal(SampleStatus.MaxStamina, parsed.Value.Status.MaxStamina);
    }

    /// <summary>Additive on schema 1: a settings block from before this pair existed
    /// gets the 20%/80% default, not a bounce window pinned to zero.</summary>
    [Fact]
    public void ManaBounceFractionsDefaultToTwentyEightyWhenAbsentFromSettings()
    {
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", SampleStatus, nodeStartedUtc);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject settings = (JsonObject)((JsonObject)root["status"]!)["settings"]!;
        settings.Remove("manaBounceLowWaterFraction");
        settings.Remove("manaBounceHighWaterFraction");

        var parsed = MeshJson.TryParseHeartbeat(root.ToJsonString());

        Assert.NotNull(parsed);
        Assert.Equal(0.20, parsed!.Value.Status.Settings.ManaBounceLowWaterFraction);
        Assert.Equal(0.80, parsed.Value.Status.Settings.ManaBounceHighWaterFraction);
    }

    [Theory]
    [InlineData("{\"schema\":2,\"botId\":\"x\",\"name\":\"x\",\"world\":\"x\",\"nodeStartedUtc\":\"2026-01-01T00:00:00.0000000+00:00\",\"status\":{}}")]
    [InlineData("{\"schema\":1,\"botId\":\"x\",\"name\":\"x\",\"world\":\"x\",\"status\":{}}")] // missing nodeStartedUtc
    [InlineData("not json")]
    public void RejectsAMalformedHeartbeatBody(string json) => Assert.Null(MeshJson.TryParseHeartbeat(json));

    [Fact]
    public void HeartbeatResponseRoundTripsCommands()
    {
        IReadOnlyList<MeshCommand> commands = [new MeshCommand(MeshCommandKind.Mute, 7u), new MeshCommand(MeshCommandKind.Drain, null)];

        string json = MeshJson.HeartbeatResponse(commands);
        IReadOnlyList<MeshCommand>? parsed = MeshJson.TryParseHeartbeatResponse(json);

        Assert.NotNull(parsed);
        Assert.Equal(commands, parsed);
    }

    // -- stats and recent on the wire ------------------------------------------------------------

    private static readonly MeshStatus StatusWithStatsAndRecent = SampleStatus with
    {
        Stats = new MeshStats(
            StartedUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Refused: new MeshRefusalCounts(3, 1, 1, 1),
            Lines: [new MeshSpellLineStats("Strength Other", 10, 8, 2)],
            Hours: [new MeshHourlyBucket(new DateTimeOffset(2026, 1, 1, 5, 0, 0, TimeSpan.Zero), 5, 2, 4)],
            Wait: new MeshWaitStats(12.5, 40d),
            PlayersServed: 4,
            PlayersReturning: 1),
        Recent =
        [
            new MeshRecentEvent(
                new DateTimeOffset(2026, 1, 1, 0, 0, 5, TimeSpan.Zero), "cast", "Strength Other",
                "Archer", null, null, null, null, null, null),
            new MeshRecentEvent(
                new DateTimeOffset(2026, 1, 1, 0, 0, 3, TimeSpan.Zero), "manaBounce", null, null, null,
                100u, 400u, null, null, null),
        ],
    };

    [Fact]
    public void StatsJsonMatchesTheWireShape()
    {
        JsonObject json = MeshJson.Status(StatusWithStatsAndRecent);

        JsonObject stats = Assert.IsType<JsonObject>(json["stats"]);
        Assert.Equal(3, stats["refused"]!["total"]!.GetValue<int>());
        Assert.Equal(1, stats["refused"]!["outOfRange"]!.GetValue<int>());
        JsonObject line = Assert.IsType<JsonObject>(stats["lines"]![0]);
        Assert.Equal("Strength Other", line["line"]!.GetValue<string>());
        Assert.Equal(10, line["attempts"]!.GetValue<int>());
        JsonObject hour = Assert.IsType<JsonObject>(stats["hours"]![0]);
        Assert.Equal(5, hour["requested"]!.GetValue<int>());
        Assert.Equal(4, hour["fizzles"]!.GetValue<int>());
        Assert.Equal(12.5, stats["wait"]!["medianSeconds"]!.GetValue<double>());
        Assert.Equal(4, stats["playersServed"]!.GetValue<int>());

        JsonObject cast = Assert.IsType<JsonObject>(json["recent"]![0]);
        Assert.Equal("cast", cast["kind"]!.GetValue<string>());
        Assert.Equal("Strength Other", cast["line"]!.GetValue<string>());
        Assert.True(cast.ContainsKey("manaBefore"));
        Assert.Null(cast["manaBefore"]);

        JsonObject bounce = Assert.IsType<JsonObject>(json["recent"]![1]);
        Assert.Equal("manaBounce", bounce["kind"]!.GetValue<string>());
        Assert.Equal(100u, bounce["manaBefore"]!.GetValue<uint>());
        Assert.Equal(400u, bounce["manaAfter"]!.GetValue<uint>());
    }

    [Fact]
    public void StatsAndRecentRoundTripThroughAHeartbeat()
    {
        string json = MeshJson.Heartbeat(
            "local/1", "Alice", "local", StatusWithStatsAndRecent, DateTimeOffset.UnixEpoch);

        var parsed = MeshJson.TryParseHeartbeat(json);

        Assert.NotNull(parsed);
        MeshStats stats = parsed!.Value.Status.Stats;
        Assert.Equal(StatusWithStatsAndRecent.Stats.StartedUtc, stats.StartedUtc);
        Assert.Equal(StatusWithStatsAndRecent.Stats.Refused, stats.Refused);
        Assert.Equal(StatusWithStatsAndRecent.Stats.Lines, stats.Lines);
        Assert.Equal(StatusWithStatsAndRecent.Stats.Hours, stats.Hours);
        Assert.Equal(StatusWithStatsAndRecent.Stats.Wait, stats.Wait);
        Assert.Equal(StatusWithStatsAndRecent.Stats.PlayersServed, stats.PlayersServed);
        Assert.Equal(StatusWithStatsAndRecent.Stats.PlayersReturning, stats.PlayersReturning);
        Assert.Equal(StatusWithStatsAndRecent.Recent, parsed.Value.Status.Recent);
    }

    /// <summary>Additive on schema 1: an hourly bucket from before this field existed
    /// gets zero fizzles that hour, never a parse failure.</summary>
    [Fact]
    public void AnHourlyBucketsFizzlesDefaultToZeroWhenAbsent()
    {
        var nodeStartedUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        string json = MeshJson.Heartbeat("local/1", "Alice", "local", StatusWithStatsAndRecent, nodeStartedUtc);
        JsonObject root = JsonNode.Parse(json)!.AsObject();
        JsonObject hour = (JsonObject)((JsonObject)((JsonObject)root["status"]!)["stats"]!)["hours"]![0]!;
        hour.Remove("fizzles");

        var parsed = MeshJson.TryParseHeartbeat(root.ToJsonString());

        Assert.NotNull(parsed);
        Assert.Equal(0, Assert.Single(parsed!.Value.Status.Stats.Hours).Fizzles);
    }

    [Fact]
    public void APayloadWithAFullRingAndEightySpellLinesStaysWellUnderTheSixtyFourKilobyteBodyLimit()
    {
        var lines = new List<MeshSpellLineStats>();
        for (int i = 0; i < 80; i++)
            lines.Add(new MeshSpellLineStats($"Some Fairly Long Spell Line Name Other {i:D3}", 100 + i, 90 + i, 10 + i));

        var recent = new List<MeshRecentEvent>();
        for (int i = 0; i < SolrLabs.BuffBot.Stats.ActivityLog.Capacity; i++)
            recent.Add(new MeshRecentEvent(
                new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero) + TimeSpan.FromSeconds(i),
                "cast", $"Some Fairly Long Spell Line Name Other {i:D3}", "SomeRequesterName", null,
                null, null, null, null, null));

        MeshStatus status = StatusWithStatsAndRecent with
        {
            Stats = StatusWithStatsAndRecent.Stats with { Lines = lines },
            Recent = recent,
        };

        string json = MeshJson.Heartbeat("local/1", "Alice", "local", status, DateTimeOffset.UnixEpoch);

        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(json) < 64 * 1024,
            $"heartbeat body was {System.Text.Encoding.UTF8.GetByteCount(json)} bytes");
    }

    /// <summary><c>totalCount</c> is summed over the donor's whole history, not
    /// just what the entries array carries.</summary>
    [Fact]
    public void ContributorsResponseMatchesTheWireShape()
    {
        var utc = new DateTime(2026, 9, 17, 11, 42, 0, DateTimeKind.Utc);
        var contributors = new List<Contributor>
        {
            new(DonorObjectId: 5u, DonorName: "Archer", Entries: [new ContributorEntry("Prismatic Pea", 1234u, 3, utc)]),
        };

        string json = MeshJson.ContributorsResponse("local/1", contributors);
        JsonObject root = (JsonObject)JsonNode.Parse(json)!;

        Assert.Equal(1, root["schema"]!.GetValue<int>());
        Assert.Equal("local/1", root["botId"]!.GetValue<string>());
        JsonObject donor = Assert.IsType<JsonObject>(root["contributors"]![0]);
        Assert.Equal(5u, donor["donorObjectId"]!.GetValue<uint>());
        Assert.Equal("Archer", donor["donorName"]!.GetValue<string>());
        Assert.Equal(3, donor["totalCount"]!.GetValue<int>());
        JsonObject entry = Assert.IsType<JsonObject>(donor["entries"]![0]);
        Assert.Equal("Prismatic Pea", entry["item"]!.GetValue<string>());
        Assert.Equal(1234u, entry["wcid"]!.GetValue<uint>());
        Assert.Equal(3, entry["count"]!.GetValue<int>());
        Assert.Equal(utc, DateTime.Parse(entry["utc"]!.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind));
    }

    [Fact]
    public void ContributorsResponseWithNoContributorsIsAnEmptyArray()
    {
        string json = MeshJson.ContributorsResponse("local/1", Array.Empty<Contributor>());
        JsonObject root = (JsonObject)JsonNode.Parse(json)!;

        Assert.Empty(root["contributors"]!.AsArray());
    }
}
