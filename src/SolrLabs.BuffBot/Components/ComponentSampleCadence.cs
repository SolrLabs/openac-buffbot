namespace SolrLabs.BuffBot.Components;

/// <summary>Decides when the components tile is due to refresh: every interval, or immediately on
/// the first call and any call after <see cref="ForceDue"/>.</summary>
internal sealed class ComponentSampleCadence
{
    internal const double DefaultIntervalSeconds = 10d;

    private readonly double _intervalSeconds;
    private double _elapsedSeconds;

    /// <summary>True until the first <see cref="Advance"/> call, or after <see cref="ForceDue"/>.</summary>
    private bool _duePending = true;

    internal ComponentSampleCadence(double intervalSeconds = DefaultIntervalSeconds) =>
        _intervalSeconds = intervalSeconds;

    /// <summary>Advances by <paramref name="deltaSeconds"/> and reports whether a refresh is due,
    /// resetting the interval either way.</summary>
    internal bool Advance(double deltaSeconds)
    {
        _elapsedSeconds += deltaSeconds;
        if (!_duePending && _elapsedSeconds < _intervalSeconds)
            return false;

        _elapsedSeconds = 0d;
        _duePending = false;
        return true;
    }

    /// <summary>Marks the next <see cref="Advance"/> call due, regardless of elapsed time.</summary>
    internal void ForceDue() => _duePending = true;
}
