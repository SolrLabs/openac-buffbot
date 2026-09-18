using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class CharacterEnablementTests
{
    [Fact]
    public void DefaultsToDisabledForACharacterNeverSeenBefore()
    {
        var enablement = new CharacterEnablement(new FakeStorage());

        Assert.False(enablement.IsEnabled(1234));
    }

    [Fact]
    public void EnablingPersistsAcrossANewInstanceOverTheSameStorage()
    {
        var storage = new FakeStorage();
        new CharacterEnablement(storage).SetEnabled(1234, enabled: true);

        var reopened = new CharacterEnablement(storage);

        Assert.True(reopened.IsEnabled(1234));
    }

    [Fact]
    public void DisablingPersistsToo()
    {
        var storage = new FakeStorage();
        var enablement = new CharacterEnablement(storage);
        enablement.SetEnabled(1234, enabled: true);

        enablement.SetEnabled(1234, enabled: false);

        Assert.False(enablement.IsEnabled(1234));
    }

    [Fact]
    public void EachCharacterHasItsOwnSetting()
    {
        var storage = new FakeStorage();
        var enablement = new CharacterEnablement(storage);
        enablement.SetEnabled(1111, enabled: true);

        Assert.True(enablement.IsEnabled(1111));
        Assert.False(enablement.IsEnabled(2222));
    }

    [Fact]
    public void UnavailableStorageReadsBackAsDisabledAndIgnoresWrites()
    {
        var storage = new FakeStorage { IsAvailable = false };
        var enablement = new CharacterEnablement(storage);

        enablement.SetEnabled(1234, enabled: true);

        Assert.False(enablement.IsEnabled(1234));
    }
}
