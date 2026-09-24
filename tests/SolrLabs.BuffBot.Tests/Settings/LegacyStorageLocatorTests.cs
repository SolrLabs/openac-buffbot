using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class LegacyStorageLocatorTests
{
    [Fact]
    public void OnWindowsTheRootIsUnderRoamingAppDataThenAcdreamThenPlugins()
    {
        var environment = new FakePathEnvironment { IsWindows = true }
            .WithFolder(Environment.SpecialFolder.ApplicationData, @"C:\Users\shane\AppData\Roaming");

        string? root = LegacyStorageLocator.TryResolveRoot(environment);

        Assert.Equal(
            Path.Combine(@"C:\Users\shane\AppData\Roaming", "acdream", "plugins", "solrlabs.buffbot"),
            root);
    }

    [Fact]
    public void OnMacOsTheRootIsUnderApplicationSupportAcdreamConfigThenPlugins()
    {
        var environment = new FakePathEnvironment { IsMacOS = true }
            .WithFolder(Environment.SpecialFolder.UserProfile, "/Users/shane");

        string? root = LegacyStorageLocator.TryResolveRoot(environment);

        Assert.Equal(
            Path.Combine("/Users/shane", "Library", "Application Support", "acdream", "config", "plugins", "solrlabs.buffbot"),
            root);
    }

    [Fact]
    public void OnLinuxAConfiguredXdgConfigHomeWins()
    {
        var environment = new FakePathEnvironment()
            .WithEnvironmentVariable("XDG_CONFIG_HOME", "/home/shane/.config-custom")
            .WithFolder(Environment.SpecialFolder.UserProfile, "/home/shane");

        string? root = LegacyStorageLocator.TryResolveRoot(environment);

        Assert.Equal(
            Path.Combine("/home/shane/.config-custom", "acdream", "plugins", "solrlabs.buffbot"),
            root);
    }

    [Fact]
    public void OnLinuxWithNoXdgConfigHomeTheRootFallsBackToDotConfig()
    {
        var environment = new FakePathEnvironment()
            .WithFolder(Environment.SpecialFolder.UserProfile, "/home/shane");

        string? root = LegacyStorageLocator.TryResolveRoot(environment);

        Assert.Equal(
            Path.Combine("/home/shane", ".config", "acdream", "plugins", "solrlabs.buffbot"),
            root);
    }

    [Fact]
    public void ANullResultWhenTheOperatingSystemHasNoHomeFolderToOffer()
    {
        var environment = new FakePathEnvironment { IsMacOS = true };

        string? root = LegacyStorageLocator.TryResolveRoot(environment);

        Assert.Null(root);
    }
}
