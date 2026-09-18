using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Tests.Donations;

internal sealed class FakeStorage : IPluginStorage
{
    private readonly Dictionary<string, string> _values = new();

    public bool IsAvailable { get; set; } = true;

    public string? ReadText(string key) => _values.GetValueOrDefault(key);

    public IReadOnlyList<string> List(string prefix) =>
        _values.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

    public void WriteText(string key, string content) => _values[key] = content;

    public bool Delete(string key) => _values.Remove(key);

    /// <summary>Writes a key's raw content directly — how a test plants a corrupt file.</summary>
    internal void Corrupt(string key, string content) => _values[key] = content;
}
