using System.Text.Json;
using System.Text.Json.Nodes;
using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Portals;

namespace SolrLabs.BuffBot.Settings;

/// <summary>Persists <see cref="BuffBotSettings"/> through the host's <see cref="IPluginStorage"/>, one JSON blob per character object id — the GUI and headless hosts resolve their own storage root, so a setting saved on one host does not carry to the other. Hand-built JSON, not reflection-driven serialization. <see cref="Load"/> never throws: a missing key, unavailable storage, malformed JSON or a field of the wrong type all fall back to <see cref="BuffBotSettings.Default"/> for that field.</summary>
internal sealed class BuffBotSettingsStore
{
    private readonly IPluginStorage _storage;

    internal BuffBotSettingsStore(IPluginStorage storage) => _storage = storage;

    internal BuffBotSettings Load(uint characterObjectId)
    {
        if (!_storage.IsAvailable)
            return BuffBotSettings.Default;

        string? json = _storage.ReadText(Key(characterObjectId));
        if (json is null)
            return BuffBotSettings.Default;

        return Parse(json).Clamped();
    }

    internal void Save(uint characterObjectId, BuffBotSettings settings)
    {
        if (!_storage.IsAvailable)
            return;

        _storage.WriteText(Key(characterObjectId), Serialize(settings.Clamped()));
    }

    private static string Key(uint characterObjectId) => $"settings/{characterObjectId}";

    private static string Serialize(BuffBotSettings settings) => new JsonObject
    {
        ["selfBuffUpkeep"] = settings.SelfBuffUpkeep,
        ["refusalRangeMeters"] = settings.RefusalRangeMeters,
        ["repliesPerSenderPerMinute"] = settings.RepliesPerSenderPerMinute,
        ["intakePaused"] = settings.IntakePaused,
        ["targetTier"] = settings.TargetTier,
        ["tierFallback"] = settings.TierFallback,
        ["fizzlesBeforeSkip"] = settings.FizzlesBeforeSkip,
        ["primaryPortal"] = PortalTieObject(settings.PrimaryPortal),
        ["secondaryPortal"] = PortalTieObject(settings.SecondaryPortal),
        ["componentLowStock"] = settings.ComponentLowStock,
        ["manaBounceLowWaterFraction"] = settings.ManaBounceLowWaterFraction,
        ["manaBounceHighWaterFraction"] = settings.ManaBounceHighWaterFraction,
        ["splitPeas"] = settings.SplitPeas,
        ["queuePauseSeconds"] = settings.QueuePauseSeconds,
    }.ToJsonString();

    private static JsonObject PortalTieObject(PortalTie tie) => new()
    {
        ["description"] = tie.Description,
        ["direction"] = PortalDirectionText.ToText(tie.Direction),
    };

    /// <summary>Field by field: a malformed field defaults rather than taking the rest of the record down with it.</summary>
    private static BuffBotSettings Parse(string json)
    {
        JsonObject? root = TryParseObject(json);
        if (root is null)
            return BuffBotSettings.Default;

        return new BuffBotSettings(
            OptionalBool(root, "selfBuffUpkeep") ?? BuffBotSettings.DefaultSelfBuffUpkeep,
            OptionalDouble(root, "refusalRangeMeters") ?? BuffBotSettings.DefaultRefusalRangeMeters,
            OptionalInt(root, "repliesPerSenderPerMinute") ?? BuffBotSettings.DefaultRepliesPerSenderPerMinute,
            OptionalBool(root, "intakePaused") ?? BuffBotSettings.DefaultIntakePaused,
            OptionalInt(root, "targetTier"),
            OptionalBool(root, "tierFallback") ?? BuffBotSettings.DefaultTierFallback,
            OptionalInt(root, "fizzlesBeforeSkip") ?? BuffBotSettings.DefaultFizzlesBeforeSkip,
            ParsePortalTie(root, "primaryPortal"),
            ParsePortalTie(root, "secondaryPortal"),
            OptionalInt(root, "componentLowStock") ?? BuffBotSettings.DefaultComponentLowStock,
            OptionalDouble(root, "manaBounceLowWaterFraction") ?? BuffBotSettings.DefaultManaBounceLowWaterFraction,
            OptionalDouble(root, "manaBounceHighWaterFraction") ?? BuffBotSettings.DefaultManaBounceHighWaterFraction,
            OptionalBool(root, "splitPeas") ?? BuffBotSettings.DefaultSplitPeas,
            OptionalDouble(root, "queuePauseSeconds") ?? BuffBotSettings.DefaultQueuePauseSeconds);
    }

    /// <summary>A missing key, or a value of the wrong shape, both fall back to <see cref="PortalTie.Empty"/>.</summary>
    private static PortalTie ParsePortalTie(JsonObject root, string name)
    {
        if (!root.TryGetPropertyValue(name, out JsonNode? node) || node is not JsonObject tieObject)
            return PortalTie.Empty;

        string description = OptionalString(tieObject, "description") ?? "";
        PortalDirection direction = PortalDirectionText.Parse(OptionalString(tieObject, "direction"));
        return new PortalTie(description, direction);
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

    private static bool? OptionalBool(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out bool b)
            ? b
            : null;

    private static int? OptionalInt(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out int i)
            ? i
            : null;

    private static double? OptionalDouble(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out double d)
            ? d
            : null;

    private static string? OptionalString(JsonObject root, string name) =>
        root.TryGetPropertyValue(name, out JsonNode? node) && node is JsonValue v && v.TryGetValue(out string? s)
            ? s
            : null;
}
