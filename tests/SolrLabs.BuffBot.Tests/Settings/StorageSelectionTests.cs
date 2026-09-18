using SolrLabs.BuffBot;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class StorageSelectionTests
{
    [Fact]
    public void PicksHostStorageWhenItReportsAvailable()
    {
        var hostStorage = new FakeStorage { IsAvailable = true };
        var fallback = new FakeStorage { IsAvailable = true };

        var chosen = BuffBotPlugin.SelectStorage(hostStorage, fallback, _ => { });

        Assert.Same(hostStorage, chosen);
    }

    [Fact]
    public void FallsBackWhenHostStorageReportsUnavailable()
    {
        var hostStorage = new FakeStorage { IsAvailable = false };
        var fallback = new FakeStorage { IsAvailable = true };

        var chosen = BuffBotPlugin.SelectStorage(hostStorage, fallback, _ => { });

        Assert.Same(fallback, chosen);
    }

    [Fact]
    public void LogsOnceExplainingTheFallback()
    {
        var hostStorage = new FakeStorage { IsAvailable = false };
        var fallback = new FakeStorage { IsAvailable = true };
        var messages = new List<string>();

        BuffBotPlugin.SelectStorage(hostStorage, fallback, messages.Add);

        string message = Assert.Single(messages);
        Assert.Contains("host provides no plugin storage", message);
    }

    [Fact]
    public void NeverLogsWhenHostStorageIsUsed()
    {
        var hostStorage = new FakeStorage { IsAvailable = true };
        var fallback = new FakeStorage { IsAvailable = true };
        var messages = new List<string>();

        BuffBotPlugin.SelectStorage(hostStorage, fallback, messages.Add);

        Assert.Empty(messages);
    }
}
