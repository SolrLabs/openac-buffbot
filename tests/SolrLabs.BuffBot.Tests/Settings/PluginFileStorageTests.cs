using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class PluginFileStorageTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "buffbot-plugin-storage-tests-" + Guid.NewGuid());

    [Fact]
    public void IsAvailableCreatesTheRootDirectory()
    {
        var storage = new PluginFileStorage(_root);

        Assert.True(storage.IsAvailable);
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public void WritingThenReadingBackReturnsTheSameContent()
    {
        var storage = new PluginFileStorage(_root);

        storage.WriteText("settings/1342177284", "{\"a\":1}");

        Assert.Equal("{\"a\":1}", storage.ReadText("settings/1342177284"));
    }

    [Fact]
    public void WritingTwiceOverwritesRatherThanAppending()
    {
        var storage = new PluginFileStorage(_root);
        storage.WriteText("enabled/1234", "false");

        storage.WriteText("enabled/1234", "true");

        Assert.Equal("true", storage.ReadText("enabled/1234"));
    }

    [Fact]
    public void ReadingAMissingKeyReturnsNullRatherThanThrowing()
    {
        var storage = new PluginFileStorage(_root);

        Assert.Null(storage.ReadText("settings/9999"));
    }

    [Fact]
    public void ListReturnsOnlyKeysUnderTheGivenPrefix()
    {
        var storage = new PluginFileStorage(_root);
        storage.WriteText("settings/1111", "a");
        storage.WriteText("settings/2222", "b");
        storage.WriteText("enabled/1111", "true");

        IReadOnlyList<string> keys = storage.List("settings");

        Assert.Equal(new[] { "settings/1111", "settings/2222" }, keys);
    }

    [Fact]
    public void ListOnAMissingPrefixReturnsEmptyRatherThanThrowing()
    {
        var storage = new PluginFileStorage(_root);

        Assert.Empty(storage.List("nope"));
    }

    [Fact]
    public void DeleteRemovesTheFileAndReportsTrue()
    {
        var storage = new PluginFileStorage(_root);
        storage.WriteText("settings/1234", "x");

        Assert.True(storage.Delete("settings/1234"));
        Assert.Null(storage.ReadText("settings/1234"));
    }

    [Fact]
    public void DeletingAMissingKeyReportsFalseRatherThanThrowing()
    {
        var storage = new PluginFileStorage(_root);

        Assert.False(storage.Delete("settings/1234"));
    }

    [Fact]
    public void WriteTextRejectsARootedKey()
    {
        var storage = new PluginFileStorage(_root);

        Assert.Throws<ArgumentException>(() => storage.WriteText(RootedKey(), "x"));
    }

    [Fact]
    public void WriteTextRejectsAKeyThatEscapesTheRootViaDotDot()
    {
        var storage = new PluginFileStorage(_root);

        Assert.Throws<ArgumentException>(() => storage.WriteText("../escaped", "x"));
    }

    [Fact]
    public void ReadTextOnARootedOrEscapingKeyReadsBackAsAbsentRatherThanThrowing()
    {
        var storage = new PluginFileStorage(_root);

        Assert.Null(storage.ReadText(RootedKey()));
        Assert.Null(storage.ReadText("../escaped"));
    }

    [Fact]
    public void AtomicWriteLeavesNoTemporaryFileBehind()
    {
        var storage = new PluginFileStorage(_root);

        storage.WriteText("settings/1234", "{\"a\":1}");

        Assert.Empty(Directory.EnumerateFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    private static string RootedKey() =>
        OperatingSystem.IsWindows() ? "C:\\escaped" : "/escaped";

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
