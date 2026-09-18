using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class HeadlessRelogPersistenceTests : IDisposable
{
    private const uint CharacterObjectId = 1342177284u;

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "buffbot-relog-persistence-tests-" + Guid.NewGuid());

    [Fact]
    public void EnablementSurvivesARelogThroughTheFallback()
    {
        new CharacterEnablement(new PluginFileStorage(_root)).SetEnabled(CharacterObjectId, enabled: true);

        bool enabledAfterRelog = new CharacterEnablement(new PluginFileStorage(_root)).IsEnabled(CharacterObjectId);

        Assert.True(enabledAfterRelog);
    }

    [Fact]
    public void SettingsSurviveARelogThroughTheFallback()
    {
        BuffBotSettings saved = BuffBotSettings.Default with
        {
            RefusalRangeMeters = 40d,
            RepliesPerSenderPerMinute = 4,
            IntakePaused = true,
        };
        new BuffBotSettingsStore(new PluginFileStorage(_root)).Save(CharacterObjectId, saved);

        BuffBotSettings loadedAfterRelog =
            new BuffBotSettingsStore(new PluginFileStorage(_root)).Load(CharacterObjectId);

        Assert.Equal(saved, loadedAfterRelog);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
