using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Web;

/// <summary>Hand-built JSON, not reflection-driven serialization: the wire shape is exact — field names, nesting, explicit nulls, a literal <c>schema</c>.</summary>
internal static class MeshJson
{
    internal static JsonObject Status(MeshStatus status)
    {
        var waiting = new JsonArray();
        foreach (MeshWaitingEntry entry in status.Waiting)
            waiting.Add(new JsonObject
            {
                ["objectId"] = entry.ObjectId,
                ["name"] = entry.Name,
                ["archetype"] = entry.Archetype,
                ["waitingSeconds"] = entry.WaitingSeconds,
            });

        var lines = new JsonArray();
        foreach (MeshSpellLineStats line in status.Stats.Lines)
            lines.Add(new JsonObject
            {
                ["line"] = line.Line,
                ["attempts"] = line.Attempts,
                ["landed"] = line.Landed,
                ["fizzles"] = line.Fizzles,
            });

        var hours = new JsonArray();
        foreach (MeshHourlyBucket hour in status.Stats.Hours)
            hours.Add(new JsonObject
            {
                ["hourStartUtc"] = hour.HourStartUtc.ToString("O", CultureInfo.InvariantCulture),
                ["requested"] = hour.Requested,
                ["upkeep"] = hour.Upkeep,
                ["fizzles"] = hour.Fizzles,
            });

        var recent = new JsonArray();
        foreach (MeshRecentEvent evt in status.Recent)
            recent.Add(new JsonObject
            {
                ["timestamp"] = evt.Timestamp.ToString("O", CultureInfo.InvariantCulture),
                ["kind"] = evt.Kind,
                ["line"] = evt.Line,
                ["targetName"] = evt.TargetName,
                ["reason"] = evt.Reason,
                ["manaBefore"] = evt.ManaBefore,
                ["manaAfter"] = evt.ManaAfter,
                ["who"] = evt.Who,
                ["archetype"] = evt.Archetype,
                ["source"] = evt.Source,
            });

        return new JsonObject
        {
            ["enabled"] = status.Enabled,
            ["activity"] = status.Activity,
            ["currentRequesterName"] = status.CurrentRequesterName,
            ["currentRequesterObjectId"] = status.CurrentRequesterObjectId,
            ["currentSpellLine"] = status.CurrentSpellLine,
            ["currentSpellId"] = status.CurrentSpellId,
            ["stepIndex"] = status.StepIndex,
            ["stepCount"] = status.StepCount,
            ["currentMana"] = status.CurrentMana,
            ["maxMana"] = status.MaxMana,
            ["currentHealth"] = status.CurrentHealth,
            ["maxHealth"] = status.MaxHealth,
            ["currentStamina"] = status.CurrentStamina,
            ["maxStamina"] = status.MaxStamina,
            ["waiting"] = waiting,
            ["counters"] = new JsonObject
            {
                ["tellsAnswered"] = status.Counters.TellsAnswered,
                ["castsLanded"] = status.Counters.CastsLanded,
                ["fizzles"] = status.Counters.Fizzles,
                ["manaBounces"] = status.Counters.ManaBounces,
                ["tierStepDowns"] = status.Counters.TierStepDowns,
            },
            ["stats"] = new JsonObject
            {
                ["startedUtc"] = status.Stats.StartedUtc.ToString("O", CultureInfo.InvariantCulture),
                ["refused"] = new JsonObject
                {
                    ["total"] = status.Stats.Refused.Total,
                    ["outOfRange"] = status.Stats.Refused.OutOfRange,
                    ["unknownLine"] = status.Stats.Refused.UnknownLine,
                    ["nothingLearned"] = status.Stats.Refused.NothingLearned,
                },
                ["lines"] = lines,
                ["hours"] = hours,
                ["wait"] = new JsonObject
                {
                    ["medianSeconds"] = status.Stats.Wait.MedianSeconds,
                    ["longestSeconds"] = status.Stats.Wait.LongestSeconds,
                },
                ["playersServed"] = status.Stats.PlayersServed,
                ["playersReturning"] = status.Stats.PlayersReturning,
            },
            ["recent"] = recent,
            ["settings"] = new JsonObject
            {
                ["selfBuffUpkeep"] = status.Settings.SelfBuffUpkeep,
                ["refusalRangeMeters"] = status.Settings.RefusalRangeMeters,
                ["repliesPerSenderPerMinute"] = status.Settings.RepliesPerSenderPerMinute,
                ["intakePaused"] = status.Settings.IntakePaused,
                ["targetTier"] = status.Settings.TargetTier,
                ["tierFallback"] = status.Settings.TierFallback,
                ["fizzlesBeforeSkip"] = status.Settings.FizzlesBeforeSkip,
                ["componentLowStock"] = status.Settings.ComponentLowStock,
                ["manaBounceLowWaterFraction"] = status.Settings.ManaBounceLowWaterFraction,
                ["manaBounceHighWaterFraction"] = status.Settings.ManaBounceHighWaterFraction,
                ["splitPeas"] = status.Settings.SplitPeas,
            },
            ["components"] = ComponentsObject(status.Components),
            ["tradeOpen"] = status.TradeOpen,
            ["donationsCompleted"] = status.DonationsCompleted,
            ["donationItemsReceived"] = status.DonationItemsReceived,
        };
    }

    /// <summary>Guards against a default <see cref="MeshComponents"/>, whose <c>Items</c> is null unlike every other default here.</summary>
    private static JsonObject ComponentsObject(MeshComponents components)
    {
        var items = new JsonArray();
        foreach (MeshComponentItem item in components.Items ?? Array.Empty<MeshComponentItem>())
            items.Add(new JsonObject
            {
                ["weenieClassId"] = item.WeenieClassId,
                ["name"] = item.Name,
                ["stock"] = item.Stock,
                ["usedBy"] = item.UsedBy,
            });

        return new JsonObject
        {
            ["available"] = components.Available,
            ["items"] = items,
            ["catalog"] = components.CatalogAvailable,
        };
    }

    internal static JsonObject Bot(MeshBot bot) => new()
    {
        ["botId"] = bot.BotId,
        ["name"] = bot.Name,
        ["world"] = bot.World,
        ["isHub"] = bot.IsHub,
        ["stale"] = bot.Stale,
        ["lastSeenSeconds"] = bot.LastSeenSeconds,
        ["status"] = Status(bot.Status),
    };

    internal static string BotsResponse(MeshBotsSnapshot snapshot)
    {
        var bots = new JsonArray();
        foreach (MeshBot bot in snapshot.Bots)
            bots.Add(Bot(bot));

        return new JsonObject
        {
            ["schema"] = 1,
            ["hubBotId"] = snapshot.HubBotId,
            ["bots"] = bots,
        }.ToJsonString();
    }

    internal static string Accepted() => new JsonObject { ["accepted"] = true }.ToJsonString();

    /// <summary>The ledger itself is never pruned; these trim only what crosses the wire.</summary>
    private const int MaxContributorsInResponse = 200;
    private const int MaxEntriesPerContributorInResponse = 200;

    /// <summary>Donors in the order <see cref="Donations.ContributorLedger.Load"/> already sorted them, entries within each donor newest first, and <c>totalCount</c> summed over every entry the donor has, even past the <see cref="MaxEntriesPerContributorInResponse"/> cap.</summary>
    internal static string ContributorsResponse(string botId, IReadOnlyList<Contributor> contributors)
    {
        var donorsArray = new JsonArray();
        foreach (Contributor contributor in contributors.Take(MaxContributorsInResponse))
        {
            int totalCount = 0;
            foreach (ContributorEntry entry in contributor.Entries)
                totalCount += entry.Count;

            var entriesArray = new JsonArray();
            foreach (ContributorEntry entry in contributor.Entries
                .OrderByDescending(e => e.UtcTime)
                .Take(MaxEntriesPerContributorInResponse))
                entriesArray.Add(new JsonObject
                {
                    ["item"] = entry.ItemName,
                    ["wcid"] = entry.WeenieClassId,
                    ["count"] = entry.Count,
                    ["utc"] = entry.UtcTime.ToString("O", CultureInfo.InvariantCulture),
                });

            donorsArray.Add(new JsonObject
            {
                ["donorObjectId"] = contributor.DonorObjectId,
                ["donorName"] = contributor.DonorName,
                ["totalCount"] = totalCount,
                ["entries"] = entriesArray,
            });
        }

        return new JsonObject
        {
            ["schema"] = 1,
            ["botId"] = botId,
            ["contributors"] = donorsArray,
        }.ToJsonString();
    }

    internal static JsonObject Command(MeshCommand command)
    {
        var json = new JsonObject
        {
            ["kind"] = command.Kind switch
            {
                MeshCommandKind.Mute => "mute",
                MeshCommandKind.Release => "release",
                MeshCommandKind.Enable => "enable",
                MeshCommandKind.Disable => "disable",
                MeshCommandKind.Settings => "settings",
                MeshCommandKind.Drain => "drain",
                _ => throw new ArgumentOutOfRangeException(nameof(command)),
            },
            ["objectId"] = command.ObjectId,
        };
        if (command.Settings is { } patch)
            json["settings"] = SettingsPatch(patch);
        return json;
    }

    /// <summary>Only the fields the console actually changed; an explicit JSON <c>null</c> for <c>targetTier</c> is written when <see cref="MeshSettingsPatch.HasTargetTier"/> is set, even though the value itself is null.</summary>
    private static JsonObject SettingsPatch(MeshSettingsPatch patch)
    {
        var json = new JsonObject();
        if (patch.SelfBuffUpkeep is { } selfBuffUpkeep) json["selfBuffUpkeep"] = selfBuffUpkeep;
        if (patch.RefusalRangeMeters is { } refusalRangeMeters) json["refusalRangeMeters"] = refusalRangeMeters;
        if (patch.RepliesPerSenderPerMinute is { } replies) json["repliesPerSenderPerMinute"] = replies;
        if (patch.IntakePaused is { } intakePaused) json["intakePaused"] = intakePaused;
        if (patch.HasTargetTier) json["targetTier"] = patch.TargetTier;
        if (patch.TierFallback is { } tierFallback) json["tierFallback"] = tierFallback;
        if (patch.FizzlesBeforeSkip is { } fizzlesBeforeSkip) json["fizzlesBeforeSkip"] = fizzlesBeforeSkip;
        if (patch.ComponentLowStock is { } componentLowStock) json["componentLowStock"] = componentLowStock;
        if (patch.ManaBounceLowWaterFraction is { } manaBounceLowWaterFraction)
            json["manaBounceLowWaterFraction"] = manaBounceLowWaterFraction;
        if (patch.ManaBounceHighWaterFraction is { } manaBounceHighWaterFraction)
            json["manaBounceHighWaterFraction"] = manaBounceHighWaterFraction;
        if (patch.SplitPeas is { } splitPeas) json["splitPeas"] = splitPeas;
        return json;
    }

    internal static string HeartbeatResponse(IReadOnlyList<MeshCommand> commands)
    {
        var array = new JsonArray();
        foreach (MeshCommand command in commands)
            array.Add(Command(command));

        return new JsonObject { ["schema"] = 1, ["commands"] = array }.ToJsonString();
    }

    internal static string Heartbeat(string botId, string name, string world, MeshStatus status, DateTimeOffset nodeStartedUtc) => new JsonObject
    {
        ["schema"] = 1,
        ["botId"] = botId,
        ["name"] = name,
        ["world"] = world,
        ["nodeStartedUtc"] = nodeStartedUtc.ToString("O", CultureInfo.InvariantCulture),
        ["status"] = Status(status),
    }.ToJsonString();

    /// <summary>No token required, so it is deliberately thin: just enough for the console page to show which key is live and whether the hub is still deciding.</summary>
    internal static string HubResponse(string keyFingerprint, bool deciding, string linkFile) => new JsonObject
    {
        ["schema"] = 1,
        ["keyFingerprint"] = keyFingerprint,
        ["deciding"] = deciding,
        ["linkFile"] = linkFile,
    }.ToJsonString();

    /// <summary><see langword="null"/> for any malformed shape — bad JSON, an unknown <c>kind</c>,
    /// or a missing <c>objectId</c> on a kind that requires one — so the caller can answer 400.</summary>
    internal static MeshCommand? TryParseCommand(string json)
    {
        JsonObject? root = TryParseObject(json);
        return root is null ? null : ParseCommandObject(root);
    }

    /// <summary>Parses a <c>POST /mesh/heartbeat</c> body. <see langword="null"/> for anything
    /// malformed, including a <c>schema</c> other than 1.</summary>
    internal static (string BotId, string Name, string World, MeshStatus Status, DateTimeOffset NodeStartedUtc)? TryParseHeartbeat(string json)
    {
        JsonObject? root = TryParseObject(json);
        if (root is null || !TryInt(root, "schema", out int schema) || schema != 1)
            return null;
        if (!TryString(root, "botId", out string botId)
            || !TryString(root, "name", out string name)
            || !TryString(root, "world", out string world)
            || !TryDateTimeOffset(root, "nodeStartedUtc", out DateTimeOffset nodeStartedUtc))
            return null;
        if (!root.TryGetPropertyValue("status", out JsonNode? statusNode) || statusNode is not JsonObject statusObject)
            return null;

        MeshStatus? status = TryParseStatus(statusObject);
        return status is null ? null : (botId, name, world, status, nodeStartedUtc);
    }

    /// <summary>The spoke side of the round trip <see cref="TryParseCommand"/> answers on the hub side.</summary>
    internal static IReadOnlyList<MeshCommand>? TryParseHeartbeatResponse(string json)
    {
        JsonObject? root = TryParseObject(json);
        if (root is null || !TryInt(root, "schema", out int schema) || schema != 1)
            return null;
        if (!root.TryGetPropertyValue("commands", out JsonNode? commandsNode) || commandsNode is not JsonArray array)
            return null;

        var commands = new List<MeshCommand>(array.Count);
        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject entry)
                return null;
            MeshCommand? command = ParseCommandObject(entry);
            if (command is null)
                return null;
            commands.Add(command);
        }
        return commands;
    }

    private static MeshCommand? ParseCommandObject(JsonObject root)
    {
        if (!TryString(root, "kind", out string kind))
            return null;

        MeshCommandKind? parsedKind = kind switch
        {
            "mute" => MeshCommandKind.Mute,
            "release" => MeshCommandKind.Release,
            "enable" => MeshCommandKind.Enable,
            "disable" => MeshCommandKind.Disable,
            "settings" => MeshCommandKind.Settings,
            "drain" => MeshCommandKind.Drain,
            _ => null,
        };
        if (parsedKind is null)
            return null;

        uint? objectId = TryUInt(root, "objectId", out uint parsedObjectId) ? parsedObjectId : null;
        if (parsedKind is MeshCommandKind.Mute or MeshCommandKind.Release && objectId is null)
            return null;

        if (parsedKind == MeshCommandKind.Settings)
        {
            if (!root.TryGetPropertyValue("settings", out JsonNode? settingsNode) || settingsNode is not JsonObject settingsObject)
                return null;
            MeshSettingsPatch? patch = TryParseSettingsPatch(settingsObject);
            if (patch is null)
                return null;
            return new MeshCommand(parsedKind.Value, objectId, patch);
        }

        return new MeshCommand(parsedKind.Value, objectId);
    }

    /// <summary>Every present field must be the right type, or the whole patch is rejected. Clamping to the settings model's own bounds happens afterwards, in <see cref="Settings.BuffBotSettings.WithPatch"/>, not here.</summary>
    private static MeshSettingsPatch? TryParseSettingsPatch(JsonObject root)
    {
        if (!TryOptionalBool(root, "selfBuffUpkeep", out bool? selfBuffUpkeep)) return null;
        if (!TryOptionalDouble(root, "refusalRangeMeters", out double? refusalRangeMeters)) return null;
        if (!TryOptionalInt(root, "repliesPerSenderPerMinute", out int? repliesPerSenderPerMinute)) return null;
        if (!TryOptionalBool(root, "intakePaused", out bool? intakePaused)) return null;
        if (!TryOptionalNullableInt(root, "targetTier", out bool hasTargetTier, out int? targetTier)) return null;
        if (!TryOptionalBool(root, "tierFallback", out bool? tierFallback)) return null;
        if (!TryOptionalInt(root, "fizzlesBeforeSkip", out int? fizzlesBeforeSkip)) return null;
        if (!TryOptionalInt(root, "componentLowStock", out int? componentLowStock)) return null;
        if (!TryOptionalDouble(root, "manaBounceLowWaterFraction", out double? manaBounceLowWaterFraction)) return null;
        if (!TryOptionalDouble(root, "manaBounceHighWaterFraction", out double? manaBounceHighWaterFraction)) return null;
        if (!TryOptionalBool(root, "splitPeas", out bool? splitPeas)) return null;

        return new MeshSettingsPatch(
            selfBuffUpkeep, refusalRangeMeters, repliesPerSenderPerMinute, intakePaused,
            hasTargetTier, targetTier, tierFallback, fizzlesBeforeSkip, componentLowStock,
            manaBounceLowWaterFraction, manaBounceHighWaterFraction, splitPeas);
    }

    private static MeshStatus? TryParseStatus(JsonObject root)
    {
        if (!TryBool(root, "enabled", out bool enabled)) return null;
        if (!TryString(root, "activity", out string activity)) return null;
        if (!TryUInt(root, "currentSpellId", out uint spellId)) return null;
        if (!TryInt(root, "stepIndex", out int stepIndex)) return null;
        if (!TryInt(root, "stepCount", out int stepCount)) return null;
        if (!TryUInt(root, "currentMana", out uint currentMana)) return null;
        if (!TryUInt(root, "maxMana", out uint maxMana)) return null;
        // Additive: an older spoke that never sent these renders as a missing field rather than failing to parse the rest of the body.
        uint? currentHealth = OptionalUInt(root, "currentHealth");
        uint? maxHealth = OptionalUInt(root, "maxHealth");
        uint? currentStamina = OptionalUInt(root, "currentStamina");
        uint? maxStamina = OptionalUInt(root, "maxStamina");
        // Additive, absent on a body written before these existed, and that always meant no trade open and nothing donated yet.
        bool tradeOpen = OptionalBool(root, "tradeOpen") ?? false;
        int donationsCompleted = OptionalInt(root, "donationsCompleted") ?? 0;
        int donationItemsReceived = OptionalInt(root, "donationItemsReceived") ?? 0;

        if (!root.TryGetPropertyValue("waiting", out JsonNode? waitingNode) || waitingNode is not JsonArray waitingArray)
            return null;
        var waiting = new List<MeshWaitingEntry>(waitingArray.Count);
        foreach (JsonNode? item in waitingArray)
        {
            if (item is not JsonObject entry
                || !TryUInt(entry, "objectId", out uint objectId)
                || !TryString(entry, "name", out string name)
                || !TryString(entry, "archetype", out string archetype)
                || !TryDouble(entry, "waitingSeconds", out double waitingSeconds))
                return null;
            waiting.Add(new MeshWaitingEntry(objectId, name, archetype, waitingSeconds));
        }

        if (!root.TryGetPropertyValue("counters", out JsonNode? countersNode) || countersNode is not JsonObject counters)
            return null;
        if (!TryInt(counters, "tellsAnswered", out int tells)
            || !TryInt(counters, "castsLanded", out int casts)
            || !TryInt(counters, "fizzles", out int fizzles)
            || !TryInt(counters, "manaBounces", out int bounces)
            || !TryInt(counters, "tierStepDowns", out int stepDowns))
            return null;

        if (!root.TryGetPropertyValue("settings", out JsonNode? settingsNode) || settingsNode is not JsonObject settingsObject)
            return null;
        if (!TryBool(settingsObject, "selfBuffUpkeep", out bool selfBuffUpkeep)) return null;
        if (!TryDouble(settingsObject, "refusalRangeMeters", out double refusalRangeMeters)) return null;
        if (!TryInt(settingsObject, "repliesPerSenderPerMinute", out int repliesPerSenderPerMinute)) return null;
        if (!TryBool(settingsObject, "intakePaused", out bool intakePaused)) return null;
        int? targetTier = OptionalInt(settingsObject, "targetTier");
        if (!TryBool(settingsObject, "tierFallback", out bool tierFallback)) return null;
        if (!TryInt(settingsObject, "fizzlesBeforeSkip", out int fizzlesBeforeSkip)) return null;
        if (!TryInt(settingsObject, "componentLowStock", out int componentLowStock)) return null;
        // Additive, absent on a body written before this pair existed and that always meant the 20%/80% default.
        double manaBounceLowWaterFraction = OptionalDouble(settingsObject, "manaBounceLowWaterFraction")
            ?? Settings.BuffBotSettings.DefaultManaBounceLowWaterFraction;
        double manaBounceHighWaterFraction = OptionalDouble(settingsObject, "manaBounceHighWaterFraction")
            ?? Settings.BuffBotSettings.DefaultManaBounceHighWaterFraction;
        bool splitPeas = OptionalBool(settingsObject, "splitPeas") ?? Settings.BuffBotSettings.DefaultSplitPeas;

        if (!root.TryGetPropertyValue("components", out JsonNode? componentsNode) || componentsNode is not JsonObject componentsObject)
            return null;
        if (!TryBool(componentsObject, "available", out bool componentsAvailable)) return null;
        bool componentsCatalogAvailable = OptionalBool(componentsObject, "catalog") ?? true;
        if (!componentsObject.TryGetPropertyValue("items", out JsonNode? componentItemsNode) || componentItemsNode is not JsonArray componentItemsArray)
            return null;
        var componentItems = new List<MeshComponentItem>(componentItemsArray.Count);
        foreach (JsonNode? item in componentItemsArray)
        {
            if (item is not JsonObject entry
                || !TryUInt(entry, "weenieClassId", out uint weenieClassId)
                || !TryString(entry, "name", out string componentName)
                || !TryInt(entry, "stock", out int stock)
                || !TryInt(entry, "usedBy", out int usedBy))
                return null;
            componentItems.Add(new MeshComponentItem(weenieClassId, componentName, stock, usedBy));
        }

        MeshStats? stats = TryParseStats(root);
        if (stats is null)
            return null;

        if (!root.TryGetPropertyValue("recent", out JsonNode? recentNode) || recentNode is not JsonArray recentArray)
            return null;
        var recent = new List<MeshRecentEvent>(recentArray.Count);
        foreach (JsonNode? item in recentArray)
        {
            if (item is not JsonObject entry
                || !TryDateTimeOffset(entry, "timestamp", out DateTimeOffset timestamp)
                || !TryString(entry, "kind", out string kind))
                return null;
            recent.Add(new MeshRecentEvent(
                timestamp, kind, OptionalString(entry, "line"), OptionalString(entry, "targetName"),
                OptionalString(entry, "reason"), OptionalUInt(entry, "manaBefore"),
                OptionalUInt(entry, "manaAfter"), OptionalString(entry, "who"),
                OptionalString(entry, "archetype"), OptionalString(entry, "source")));
        }

        return new MeshStatus(
            enabled, activity, OptionalString(root, "currentRequesterName"),
            OptionalUInt(root, "currentRequesterObjectId"), OptionalString(root, "currentSpellLine"),
            spellId, stepIndex, stepCount, currentMana, maxMana, waiting,
            new MeshCounters(tells, casts, fizzles, bounces, stepDowns),
            stats, recent,
            new MeshSettings(
                selfBuffUpkeep, refusalRangeMeters, repliesPerSenderPerMinute, intakePaused,
                targetTier, tierFallback, fizzlesBeforeSkip, componentLowStock,
                manaBounceLowWaterFraction, manaBounceHighWaterFraction, splitPeas),
            new MeshComponents(componentsAvailable, componentItems, componentsCatalogAvailable),
            currentHealth, maxHealth, currentStamina, maxStamina,
            tradeOpen, donationsCompleted, donationItemsReceived);
    }

    private static MeshStats? TryParseStats(JsonObject root)
    {
        if (!root.TryGetPropertyValue("stats", out JsonNode? statsNode) || statsNode is not JsonObject statsObject)
            return null;
        if (!TryDateTimeOffset(statsObject, "startedUtc", out DateTimeOffset startedUtc))
            return null;

        if (!statsObject.TryGetPropertyValue("refused", out JsonNode? refusedNode)
            || refusedNode is not JsonObject refused
            || !TryInt(refused, "total", out int refusedTotal)
            || !TryInt(refused, "outOfRange", out int outOfRange)
            || !TryInt(refused, "unknownLine", out int unknownLine)
            || !TryInt(refused, "nothingLearned", out int nothingLearned))
            return null;

        if (!statsObject.TryGetPropertyValue("lines", out JsonNode? linesNode) || linesNode is not JsonArray linesArray)
            return null;
        var lines = new List<MeshSpellLineStats>(linesArray.Count);
        foreach (JsonNode? item in linesArray)
        {
            if (item is not JsonObject entry
                || !TryString(entry, "line", out string line)
                || !TryInt(entry, "attempts", out int attempts)
                || !TryInt(entry, "landed", out int landed)
                || !TryInt(entry, "fizzles", out int lineFizzles))
                return null;
            lines.Add(new MeshSpellLineStats(line, attempts, landed, lineFizzles));
        }

        if (!statsObject.TryGetPropertyValue("hours", out JsonNode? hoursNode) || hoursNode is not JsonArray hoursArray)
            return null;
        var hours = new List<MeshHourlyBucket>(hoursArray.Count);
        foreach (JsonNode? item in hoursArray)
        {
            if (item is not JsonObject entry
                || !TryDateTimeOffset(entry, "hourStartUtc", out DateTimeOffset hourStartUtc)
                || !TryInt(entry, "requested", out int requested)
                || !TryInt(entry, "upkeep", out int upkeep))
                return null;
            // Additive (schema 1): a spoke older than this field simply never sends it.
            int hourFizzles = OptionalInt(entry, "fizzles") ?? 0;
            hours.Add(new MeshHourlyBucket(hourStartUtc, requested, upkeep, hourFizzles));
        }

        if (!statsObject.TryGetPropertyValue("wait", out JsonNode? waitNode) || waitNode is not JsonObject wait)
            return null;
        var waitStats = new MeshWaitStats(
            OptionalDouble(wait, "medianSeconds"), OptionalDouble(wait, "longestSeconds"));

        if (!TryInt(statsObject, "playersServed", out int playersServed)
            || !TryInt(statsObject, "playersReturning", out int playersReturning))
            return null;

        return new MeshStats(
            startedUtc,
            new MeshRefusalCounts(refusedTotal, outOfRange, unknownLine, nothingLearned),
            lines, hours, waitStats, playersServed, playersReturning);
    }

    private static JsonObject? TryParseObject(string json)
    {
        try
        {
            return JsonNode.Parse(json) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryString(JsonObject root, string name, out string value)
    {
        if (root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out string? s))
        {
            value = s;
            return true;
        }
        value = "";
        return false;
    }

    private static string? OptionalString(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out string? s)
            ? s
            : null;

    private static bool TryBool(JsonObject root, string name, out bool value)
    {
        if (root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out value))
            return true;
        value = false;
        return false;
    }

    private static bool TryInt(JsonObject root, string name, out int value)
    {
        if (root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out value))
            return true;
        value = 0;
        return false;
    }

    private static bool TryUInt(JsonObject root, string name, out uint value)
    {
        if (root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out value))
            return true;
        value = 0;
        return false;
    }

    private static bool TryDouble(JsonObject root, string name, out double value)
    {
        if (root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out value))
            return true;
        value = 0;
        return false;
    }

    private static bool TryDateTimeOffset(JsonObject root, string name, out DateTimeOffset value)
    {
        if (TryString(root, name, out string s)
            && DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out value))
            return true;
        value = default;
        return false;
    }

    private static double? OptionalDouble(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out double d)
            ? d
            : null;

    private static int? OptionalInt(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out int i)
            ? i
            : null;

    private static bool? OptionalBool(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out bool b)
            ? b
            : null;

    /// <summary>A patch field's tri-state: absent leaves <paramref name="value"/> null and returns
    /// true; wrong type returns false and rejects the whole patch.</summary>
    private static bool TryOptionalBool(JsonObject root, string name, out bool? value)
    {
        if (!root.TryGetPropertyValue(name, out JsonNode? node)) { value = null; return true; }
        if (node is JsonValue v && v.TryGetValue(out bool b)) { value = b; return true; }
        value = null;
        return false;
    }

    private static bool TryOptionalInt(JsonObject root, string name, out int? value)
    {
        if (!root.TryGetPropertyValue(name, out JsonNode? node)) { value = null; return true; }
        if (node is JsonValue v && v.TryGetValue(out int i)) { value = i; return true; }
        value = null;
        return false;
    }

    private static bool TryOptionalDouble(JsonObject root, string name, out double? value)
    {
        if (!root.TryGetPropertyValue(name, out JsonNode? node)) { value = null; return true; }
        if (node is JsonValue v && v.TryGetValue(out double d)) { value = d; return true; }
        value = null;
        return false;
    }

    /// <summary>Like <see cref="TryOptionalInt"/>, but an explicit JSON <c>null</c> is a third,
    /// legal state — see <see cref="MeshSettingsPatch"/>.</summary>
    private static bool TryOptionalNullableInt(JsonObject root, string name, out bool present, out int? value)
    {
        if (!root.TryGetPropertyValue(name, out JsonNode? node)) { present = false; value = null; return true; }
        present = true;
        if (node is null) { value = null; return true; }
        if (node is JsonValue v && v.TryGetValue(out int i)) { value = i; return true; }
        value = null;
        return false;
    }

    private static uint? OptionalUInt(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out uint u)
            ? u
            : null;
}
