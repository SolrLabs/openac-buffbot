namespace SolrLabs.BuffBot.Web;

/// <summary>Copies <see cref="Stats.SpellLineStats"/> field for field, on the wire.</summary>
internal readonly record struct MeshSpellLineStats(string Line, int Attempts, int Landed, int Fizzles);

/// <summary>Copies <see cref="Stats.HourlyBucket"/> field for field, on the wire. <see cref="Fizzles"/> defaults to 0 so a body from before this field existed still parses as the count it always meant.</summary>
internal readonly record struct MeshHourlyBucket(DateTimeOffset HourStartUtc, int Requested, int Upkeep, int Fizzles = 0);

/// <summary><see cref="Total"/> is computed rather than carried, since <see cref="Stats.RefusalCounts"/>'s own reasons already sum to it.</summary>
internal readonly record struct MeshRefusalCounts(int Total, int OutOfRange, int UnknownLine, int NothingLearned);

/// <summary>See <see cref="Stats.WaitStats"/>.</summary>
internal readonly record struct MeshWaitStats(double? MedianSeconds, double? LongestSeconds);

/// <summary>The wire's <c>stats</c> block — <see cref="Stats.SessionStatsSnapshot"/> copied field for field.</summary>
internal sealed record MeshStats(
    DateTimeOffset StartedUtc,
    MeshRefusalCounts Refused,
    IReadOnlyList<MeshSpellLineStats> Lines,
    IReadOnlyList<MeshHourlyBucket> Hours,
    MeshWaitStats Wait,
    int PlayersServed,
    int PlayersReturning)
{
    /// <summary>A zeroed <see cref="MeshStats"/>, the wire-side twin of a fresh <see cref="Stats.BotStats"/> snapshot.</summary>
    internal static readonly MeshStats Empty = new(
        DateTimeOffset.UnixEpoch, new MeshRefusalCounts(0, 0, 0, 0),
        Array.Empty<MeshSpellLineStats>(), Array.Empty<MeshHourlyBucket>(),
        new MeshWaitStats(null, null), 0, 0);
}

/// <summary>See <see cref="Stats.RecentEvent"/>. <see cref="Kind"/> is the wire's lower-camel string for <see cref="Stats.RecentEventKind"/>.</summary>
internal readonly record struct MeshRecentEvent(
    DateTimeOffset Timestamp,
    string Kind,
    string? Line,
    string? TargetName,
    string? Reason,
    uint? ManaBefore,
    uint? ManaAfter,
    string? Who,
    string? Archetype,
    string? Source);

/// <summary>The same fields as <see cref="Requests.QueueEntry"/>, copied rather than reused so this file never has to know that type changed shape.</summary>
internal readonly record struct MeshWaitingEntry(uint ObjectId, string Name, string Archetype, double WaitingSeconds);

/// <summary>Field names match <see cref="Casting.SessionCounters"/> one for one, camelCased on the way out by <see cref="MeshJson"/>.</summary>
internal readonly record struct MeshCounters(
    int TellsAnswered, int CastsLanded, int Fizzles, int ManaBounces, int TierStepDowns);

/// <summary>The wire's shape for a <see cref="Portals.PortalTie"/> — <see cref="Direction"/> carries the lower-case wire word, not the enum, the same way <see cref="MeshStatus.Activity"/> and <see cref="MeshRecentEvent.Kind"/> already cross the wire.</summary>
internal sealed record MeshPortalTie(string Description, string Direction)
{
    internal static readonly MeshPortalTie Empty = new("", "front");
}

/// <summary>Field names match <see cref="Settings.BuffBotSettings"/> one for one, camelCased on the way out by <see cref="MeshJson"/>. <see cref="ManaBounceLowWaterFraction"/> and <see cref="ManaBounceHighWaterFraction"/> are the mana bar's draggable handles, fractions of max mana; defaulted so a body from before this pair existed still parses as the 20%/80% it always meant. <see cref="PrimaryPortal"/> and <see cref="SecondaryPortal"/> default to <see langword="null"/> rather than <see cref="MeshPortalTie.Empty"/> when this struct itself is default-constructed — guard with <c>?? MeshPortalTie.Empty</c>, the same guard <see cref="MeshComponents.Items"/> already needs.</summary>
internal readonly record struct MeshSettings(
    bool SelfBuffUpkeep,
    double RefusalRangeMeters,
    int RepliesPerSenderPerMinute,
    bool IntakePaused,
    int? TargetTier,
    bool TierFallback,
    int FizzlesBeforeSkip,
    int ComponentLowStock = Settings.BuffBotSettings.DefaultComponentLowStock,
    double ManaBounceLowWaterFraction = Settings.BuffBotSettings.DefaultManaBounceLowWaterFraction,
    double ManaBounceHighWaterFraction = Settings.BuffBotSettings.DefaultManaBounceHighWaterFraction,
    bool SplitPeas = Settings.BuffBotSettings.DefaultSplitPeas,
    MeshPortalTie? PrimaryPortal = null,
    MeshPortalTie? SecondaryPortal = null);

/// <summary>Field names match <see cref="Components.ComponentUsage"/> one for one, camelCased on the way out by <see cref="MeshJson"/>.</summary>
internal readonly record struct MeshComponentItem(uint WeenieClassId, string Name, int Stock, int UsedBy);

/// <summary><see cref="Available"/> false means the item surface or the spell/component catalog cannot answer right now; the console shows a message rather than an empty table. An empty but available <see cref="Items"/> is a genuine "nothing needed" answer. <see cref="CatalogAvailable"/> is false only when the resolved profile's spells carry component ids the magic catalog resolved none of. Defaults true so a body from before this field existed still parses as the answer it always meant.</summary>
internal readonly record struct MeshComponents(
    bool Available, IReadOnlyList<MeshComponentItem> Items, bool CatalogAvailable = true);

/// <summary>The health/stamina/trade/donation fields default to a value that reads as "not sent"
/// rather than a crash, for an older spoke that never sent them.</summary>
internal sealed record MeshStatus(
    bool Enabled,
    string Activity,
    string? CurrentRequesterName,
    uint? CurrentRequesterObjectId,
    string? CurrentSpellLine,
    uint CurrentSpellId,
    int StepIndex,
    int StepCount,
    uint CurrentMana,
    uint MaxMana,
    IReadOnlyList<MeshWaitingEntry> Waiting,
    MeshCounters Counters,
    MeshStats Stats,
    IReadOnlyList<MeshRecentEvent> Recent,
    MeshSettings Settings = default,
    MeshComponents Components = default,
    uint? CurrentHealth = null,
    uint? MaxHealth = null,
    uint? CurrentStamina = null,
    uint? MaxStamina = null,
    bool TradeOpen = false,
    int DonationsCompleted = 0,
    int DonationItemsReceived = 0);
