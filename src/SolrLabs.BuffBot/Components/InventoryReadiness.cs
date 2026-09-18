using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Components;

/// <summary>Not ready until two consecutive captures carry the same non-empty holdings — a
/// headless run can read an empty first capture before inventory has streamed in.</summary>
internal sealed class InventoryReadiness
{
    internal const double StableCaptureIntervalSeconds = 2d;

    private readonly double _stableCaptureIntervalSeconds;

    private IReadOnlyList<PluginInventoryItem>? _priorSample;
    private double _elapsedSinceSample;
    private bool _wasInWorld;
    private uint _lastCharacterObjectId;

    internal InventoryReadiness(double stableCaptureIntervalSeconds = StableCaptureIntervalSeconds) =>
        _stableCaptureIntervalSeconds = stableCaptureIntervalSeconds;

    /// <summary>True once two identical non-empty captures have proven the inventory stable —
    /// stays true until <see cref="Tick"/> sees a reset condition.</summary>
    internal bool IsReady { get; private set; }

    /// <summary><paramref name="captureOwnedItems"/> is never called once <see cref="IsReady"/>
    /// is already true.</summary>
    internal void Tick(
        double deltaSeconds, bool isInWorld, uint characterObjectId,
        Func<IReadOnlyList<PluginInventoryItem>> captureOwnedItems)
    {
        bool enteredOrLeftWorld = isInWorld != _wasInWorld;
        bool characterChanged = isInWorld && _wasInWorld && characterObjectId != _lastCharacterObjectId;
        if (enteredOrLeftWorld || characterChanged)
            Reset();

        _wasInWorld = isInWorld;
        _lastCharacterObjectId = characterObjectId;

        if (!isInWorld || IsReady)
            return;

        _elapsedSinceSample += deltaSeconds;
        if (_elapsedSinceSample < _stableCaptureIntervalSeconds)
            return;

        IReadOnlyList<PluginInventoryItem> sample = captureOwnedItems();
        _elapsedSinceSample = 0;

        if (sample.Count > 0 && _priorSample is { } prior && SameHoldings(prior, sample))
        {
            IsReady = true;
            _priorSample = null;
            return;
        }

        _priorSample = sample;
    }

    private void Reset()
    {
        IsReady = false;
        _priorSample = null;
        _elapsedSinceSample = 0;
    }

    private static bool SameHoldings(IReadOnlyList<PluginInventoryItem> a, IReadOnlyList<PluginInventoryItem> b)
    {
        if (a.Count != b.Count)
            return false;

        var stacks = new Dictionary<uint, int>(a.Count);
        foreach (PluginInventoryItem item in a)
            stacks[item.ObjectId] = item.StackSize;

        foreach (PluginInventoryItem item in b)
            if (!stacks.TryGetValue(item.ObjectId, out int stack) || stack != item.StackSize)
                return false;

        return true;
    }
}
