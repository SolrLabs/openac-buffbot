using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

/// <summary>A made-up operating system, so a locator built for three of them can be exercised
/// on whichever one is actually running the tests.</summary>
internal sealed class FakePathEnvironment : ILegacyPathEnvironment
{
    private readonly Dictionary<string, string> _environmentVariables = new();
    private readonly Dictionary<Environment.SpecialFolder, string> _folders = new();

    public bool IsWindows { get; init; }
    public bool IsMacOS { get; init; }

    internal FakePathEnvironment WithFolder(Environment.SpecialFolder folder, string path)
    {
        _folders[folder] = path;
        return this;
    }

    internal FakePathEnvironment WithEnvironmentVariable(string name, string value)
    {
        _environmentVariables[name] = value;
        return this;
    }

    public string? GetEnvironmentVariable(string name) => _environmentVariables.GetValueOrDefault(name);

    public string GetFolderPath(Environment.SpecialFolder folder) => _folders.GetValueOrDefault(folder, "");
}
