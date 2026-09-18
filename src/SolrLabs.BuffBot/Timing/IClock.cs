namespace SolrLabs.BuffBot.Timing;

/// <summary>Host-free indirection over wall-clock time, so callers can be driven by a fake clock in tests.</summary>
internal interface IClock
{
    DateTimeOffset UtcNow { get; }
}

internal sealed class SystemClock : IClock
{
    internal static SystemClock Instance { get; } = new();

    private SystemClock()
    {
    }

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
