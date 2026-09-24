using SolrLabs.BuffBot.Settings;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class LegacyStorageSourceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "buffbot-legacy-storage-tests-" + Guid.NewGuid());

    [Fact]
    public void IsUnavailableWhenTheDirectoryDoesNotExist()
    {
        var source = new LegacyStorageSource(_root);

        Assert.False(source.IsAvailable);
        Assert.Empty(source.ListKeys());
        Assert.Null(source.ReadText("settings/1"));
    }

    [Fact]
    public void ListsEveryFileUnderTheRootAsASlashSeparatedKey()
    {
        WriteLegacyFile("settings/1342177284", "{}");
        WriteLegacyFile("enabled/1342177284", "true");

        var source = new LegacyStorageSource(_root);

        Assert.True(source.IsAvailable);
        Assert.Equal(new[] { "enabled/1342177284", "settings/1342177284" }, source.ListKeys());
    }

    [Fact]
    public void ReadsBackWhatWasWritten()
    {
        WriteLegacyFile("contributors/1342177284", "{\"version\":1}");

        var source = new LegacyStorageSource(_root);

        Assert.Equal("{\"version\":1}", source.ReadText("contributors/1342177284"));
    }

    [Fact]
    public void ReadingAMissingKeyReturnsNullRatherThanThrowing()
    {
        Directory.CreateDirectory(_root);

        var source = new LegacyStorageSource(_root);

        Assert.Null(source.ReadText("settings/9999"));
    }

    private void WriteLegacyFile(string key, string content)
    {
        string path = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
