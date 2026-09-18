namespace SolrLabs.BuffBot.Policy;

/// <summary>Refuses a request once the requester is far enough away that they could plausibly drift out of cast range between asking and the bot casting.</summary>
internal static class RangePolicy
{
    /// <summary>Meters. Matches the client's and server's clamp on a targeted spell's effective range.</summary>
    internal const double MaxCastRangeMeters = 75d;

    /// <summary>Default refusal distance, 90% of <see cref="MaxCastRangeMeters"/>.</summary>
    internal static bool IsInRange(double distanceMeters) =>
        IsInRange(distanceMeters, MaxCastRangeMeters * 0.9);

    internal static bool IsInRange(double distanceMeters, double refusalRangeMeters) =>
        distanceMeters <= refusalRangeMeters;
}
