namespace SolrLabs.BuffBot.Settings;

/// <summary>Everything a folder lookup needs, seamed so a test can hand it a made-up operating system rather than the real one running the test.</summary>
internal interface ILegacyPathEnvironment
{
    bool IsWindows { get; }

    bool IsMacOS { get; }

    string? GetEnvironmentVariable(string name);

    string GetFolderPath(Environment.SpecialFolder folder);
}

/// <summary>The operating system this process is actually running on.</summary>
internal sealed class RealLegacyPathEnvironment : ILegacyPathEnvironment
{
    internal static readonly RealLegacyPathEnvironment Instance = new();

    private RealLegacyPathEnvironment() { }

    public bool IsWindows => OperatingSystem.IsWindows();
    public bool IsMacOS => OperatingSystem.IsMacOS();
    public string? GetEnvironmentVariable(string name) => Environment.GetEnvironmentVariable(name);
    public string GetFolderPath(Environment.SpecialFolder folder) => Environment.GetFolderPath(folder);
}

/// <summary>Builds a filesystem path by hand, which a plugin is otherwise told never to do: the folder this locates predates the host's storage API entirely, so there is no scoped call that reaches it, and this exists only to read it once and never again. Matches, per operating system, where a pre-single-root client kept a plugin's storage: the user config folder, then "plugins" then the plugin's own id.</summary>
internal static class LegacyStorageLocator
{
    private const string PluginId = "solrlabs.buffbot";

    internal static string? TryResolveRoot() => TryResolveRoot(RealLegacyPathEnvironment.Instance);

    internal static string? TryResolveRoot(ILegacyPathEnvironment environment)
    {
        try
        {
            string? configDirectory = TryResolveConfigDirectory(environment);
            return configDirectory is null ? null : Path.Combine(configDirectory, "plugins", PluginId);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or PlatformNotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private static string? TryResolveConfigDirectory(ILegacyPathEnvironment environment)
    {
        if (environment.IsWindows)
        {
            string roaming = environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return string.IsNullOrWhiteSpace(roaming) ? null : Path.Combine(roaming, "acdream");
        }

        if (environment.IsMacOS)
        {
            string home = environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrWhiteSpace(home)
                ? null
                : Path.Combine(home, "Library", "Application Support", "acdream", "config");
        }

        // Linux and anything else POSIX-shaped: XDG, falling back to ~/.config exactly as a
        // missing or blank XDG_CONFIG_HOME does everywhere else in this ecosystem.
        string? xdgConfigHome = environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrWhiteSpace(xdgConfigHome))
            return Path.Combine(xdgConfigHome, "acdream");

        string userHome = environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return string.IsNullOrWhiteSpace(userHome) ? null : Path.Combine(userHome, ".config", "acdream");
    }
}
