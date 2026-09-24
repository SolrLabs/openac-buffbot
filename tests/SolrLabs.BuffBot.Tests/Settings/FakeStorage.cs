using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Tests.Settings;

/// <summary>An in-memory stand-in for the host's real <see cref="IPluginStorage"/>.</summary>
internal sealed class FakeStorage : IPluginStorage
{
    private readonly Dictionary<string, string> _values = new();

    /// <summary>Every key a caller has written, in order, so a test can assert a run wrote nothing.</summary>
    public readonly List<string> WrittenKeys = new();

    public bool IsAvailable { get; set; } = true;

    public string? ReadText(string key) => _values.GetValueOrDefault(key);

    public void WriteText(string key, string content)
    {
        _values[key] = content;
        WrittenKeys.Add(key);
    }

    public bool Delete(string key) => _values.Remove(key);
}
