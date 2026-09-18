namespace SolrLabs.BuffBot.Stats;

/// <summary>Closed on purpose: other per-spell outcomes count toward <see cref="SessionStats"/>
/// without earning a line here.</summary>
internal enum RecentEventKind
{
    Cast,
    Fizzle,
    Skipped,
    ManaBounce,
    RequestAccepted,
    Refused,
    Mute,
    Release,
    Unwedge,
}

/// <summary>Every field beyond <see cref="Timestamp"/> and <see cref="Kind"/> is populated only
/// for the kinds that use it.</summary>
internal readonly record struct RecentEvent(
    DateTimeOffset Timestamp,
    RecentEventKind Kind,
    string? Line = null,
    string? TargetName = null,
    string? Reason = null,
    uint? ManaBefore = null,
    uint? ManaAfter = null,
    string? Who = null,
    string? Archetype = null,
    string? Source = null)
{
    internal static RecentEvent Cast(DateTimeOffset now, string line, string targetName) =>
        new(now, RecentEventKind.Cast, Line: line, TargetName: targetName);

    internal static RecentEvent Fizzle(DateTimeOffset now, string line) =>
        new(now, RecentEventKind.Fizzle, Line: line);

    internal static RecentEvent Skipped(DateTimeOffset now, string line, string reason) =>
        new(now, RecentEventKind.Skipped, Line: line, Reason: reason);

    internal static RecentEvent ManaBounce(DateTimeOffset now, uint before, uint after) =>
        new(now, RecentEventKind.ManaBounce, ManaBefore: before, ManaAfter: after);

    internal static RecentEvent RequestAccepted(DateTimeOffset now, string requester, string archetype) =>
        new(now, RecentEventKind.RequestAccepted, Who: requester, Archetype: archetype);

    internal static RecentEvent Refused(DateTimeOffset now, string requester, string reason) =>
        new(now, RecentEventKind.Refused, Who: requester, Reason: reason);

    internal static RecentEvent Mute(DateTimeOffset now, string who, string source) =>
        new(now, RecentEventKind.Mute, Who: who, Source: source);

    internal static RecentEvent Release(DateTimeOffset now, string who, string source) =>
        new(now, RecentEventKind.Release, Who: who, Source: source);

    internal static RecentEvent Unwedge(DateTimeOffset now, string source) =>
        new(now, RecentEventKind.Unwedge, Source: source);
}
