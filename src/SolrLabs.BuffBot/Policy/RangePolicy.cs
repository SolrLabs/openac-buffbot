namespace SolrLabs.BuffBot.Policy;

/// <summary>Refuses a portal summon too far to plausibly stay in cast range.</summary>
internal static class RangePolicy
{
    internal const double MaxCastRangeMeters = 75d;

    internal static bool IsInRange(double distanceMeters) =>
        distanceMeters <= MaxCastRangeMeters * 0.9;
}
