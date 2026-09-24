using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

/// <summary>An in-memory stand-in for <see cref="ILegacyStorageSource"/>, able to simulate a
/// listing or a read that fails rather than answers empty.</summary>
internal sealed class FakeLegacySource : ILegacyStorageSource
{
    private readonly Dictionary<string, string> _values = new();

    public bool IsAvailable { get; set; } = true;
    public bool ThrowOnList { get; set; }
    public bool ThrowOnRead { get; set; }

    internal void Set(string key, string value) => _values[key] = value;

    public IReadOnlyList<string> ListKeys()
    {
        if (ThrowOnList)
            throw new IOException("legacy directory could not be listed.");
        return _values.Keys.OrderBy(static k => k, StringComparer.Ordinal).ToArray();
    }

    public string? ReadText(string key)
    {
        if (ThrowOnRead)
            throw new IOException("legacy file could not be read.");
        return _values.GetValueOrDefault(key);
    }
}
