using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class LegacyStorageMigrationTests
{
    [Fact]
    public void MigratesEveryLegacyKeyIntoAnEmptyHostStoreAndSetsTheMarker()
    {
        var host = new FakeStorage();
        var legacy = new FakeLegacySource();
        legacy.Set("settings/1342177284", "{\"selfBuffUpkeep\":true}");
        legacy.Set("enabled/1342177284", "true");
        legacy.Set("contributors/1342177284", "{\"version\":1,\"donors\":[]}");

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.Equal("{\"selfBuffUpkeep\":true}", host.ReadText("settings/1342177284"));
        Assert.Equal("true", host.ReadText("enabled/1342177284"));
        Assert.Equal("{\"version\":1,\"donors\":[]}", host.ReadText("contributors/1342177284"));
        Assert.NotNull(host.ReadText(LegacyStorageMigration.MarkerKey));
    }

    [Fact]
    public void NeverOverwritesAKeyTheHostAlreadyHasAValueFor()
    {
        var host = new FakeStorage();
        host.WriteText("enabled/1342177284", "false");
        var legacy = new FakeLegacySource();
        legacy.Set("enabled/1342177284", "true");

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.Equal("false", host.ReadText("enabled/1342177284"));
    }

    [Fact]
    public void AnEmptyStringInHostStorageStillCountsAsSet()
    {
        var host = new FakeStorage();
        host.WriteText("settings/1342177284", "");
        var legacy = new FakeLegacySource();
        legacy.Set("settings/1342177284", "{\"selfBuffUpkeep\":true}");

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.Equal("", host.ReadText("settings/1342177284"));
    }

    [Fact]
    public void MigratesOneKeyForACharacterWhileLeavingAnAlreadySetSiblingKeyAlone()
    {
        var host = new FakeStorage();
        host.WriteText("settings/1342177284", "{\"selfBuffUpkeep\":false}");
        var legacy = new FakeLegacySource();
        legacy.Set("settings/1342177284", "{\"selfBuffUpkeep\":true}");
        legacy.Set("enabled/1342177284", "true");

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.Equal("{\"selfBuffUpkeep\":false}", host.ReadText("settings/1342177284"));
        Assert.Equal("true", host.ReadText("enabled/1342177284"));
    }

    [Fact]
    public void ASecondRunAfterTheMarkerIsSetWritesNothing()
    {
        var host = new FakeStorage();
        var legacy = new FakeLegacySource();
        legacy.Set("settings/1342177284", "{}");
        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });
        host.WrittenKeys.Clear();

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.Empty(host.WrittenKeys);
    }

    [Fact]
    public void NoLegacySourceIsAQuietNoOpThatLeavesTheMarkerUnset()
    {
        var host = new FakeStorage();

        LegacyStorageMigration.Run(host, legacySource: null, _ => { }, _ => { });

        Assert.Empty(host.WrittenKeys);
        Assert.Null(host.ReadText(LegacyStorageMigration.MarkerKey));
    }

    [Fact]
    public void ALegacySourceThatReportsItselfUnavailableIsAQuietNoOpThatLeavesTheMarkerUnset()
    {
        var host = new FakeStorage();
        var legacy = new FakeLegacySource { IsAvailable = false };

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.Empty(host.WrittenKeys);
        Assert.Null(host.ReadText(LegacyStorageMigration.MarkerKey));
    }

    [Fact]
    public void HostStorageReportingUnavailableIsANoOpThatNeverThrows()
    {
        var host = new FakeStorage { IsAvailable = false };
        var legacy = new FakeLegacySource();
        legacy.Set("settings/1342177284", "{}");

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.Empty(host.WrittenKeys);
    }

    [Fact]
    public void AListingFailureIsCaughtLoggedOnceAndLeavesTheMarkerUnset()
    {
        var host = new FakeStorage();
        var legacy = new FakeLegacySource { ThrowOnList = true };
        var warnings = new List<string>();

        LegacyStorageMigration.Run(host, legacy, _ => { }, warnings.Add);

        Assert.Empty(host.WrittenKeys);
        Assert.Null(host.ReadText(LegacyStorageMigration.MarkerKey));
        Assert.Single(warnings);
    }

    [Fact]
    public void AReadFailureMidRunIsCaughtAndLeavesTheMarkerUnsetSoALaterStartRetries()
    {
        var host = new FakeStorage();
        var legacy = new FakeLegacySource { ThrowOnRead = true };
        legacy.Set("settings/1342177284", "{}");
        var warnings = new List<string>();

        LegacyStorageMigration.Run(host, legacy, _ => { }, warnings.Add);

        Assert.Null(host.ReadText("settings/1342177284"));
        Assert.Null(host.ReadText(LegacyStorageMigration.MarkerKey));
        Assert.Single(warnings);
    }

    [Fact]
    public void LogsOneLineNamingHowManyKeysMovedPerNamespace()
    {
        var host = new FakeStorage();
        var legacy = new FakeLegacySource();
        legacy.Set("settings/1", "{}");
        legacy.Set("settings/2", "{}");
        legacy.Set("enabled/1", "true");
        var infoLines = new List<string>();

        LegacyStorageMigration.Run(host, legacy, infoLines.Add, _ => { });

        string line = Assert.Single(infoLines);
        Assert.Contains("2 settings", line);
        Assert.Contains("1 enabled", line);
    }

    [Fact]
    public void NothingToMigrateStillSetsTheMarkerSoAnEmptyLegacyFolderIsNotRescannedForever()
    {
        var host = new FakeStorage();
        var legacy = new FakeLegacySource();

        LegacyStorageMigration.Run(host, legacy, _ => { }, _ => { });

        Assert.NotNull(host.ReadText(LegacyStorageMigration.MarkerKey));
    }
}
