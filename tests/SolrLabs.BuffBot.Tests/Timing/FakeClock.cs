using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Tests.Timing;

/// <summary>A clock tests drive by hand instead of sleeping.</summary>
internal sealed class FakeClock : IClock
{
    internal FakeClock(DateTimeOffset? start = null) =>
        UtcNow = start ?? new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public DateTimeOffset UtcNow { get; private set; }

    internal void Advance(TimeSpan by) => UtcNow += by;
}
