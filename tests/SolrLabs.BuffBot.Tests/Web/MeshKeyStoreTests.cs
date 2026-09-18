using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>The shared-secret file: 64 lowercase hex chars, created once, read back by every
/// later caller — including one that loses a real concurrent race to create it.</summary>
public sealed class MeshKeyStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "buffbot-mesh-key-tests-" + Guid.NewGuid());

    [Fact]
    public void CreatesASixtyFourCharacterLowercaseHexKey()
    {
        string key = MeshKeyStore.EnsureKey(Path.Combine(_directory, "mesh.key"));

        Assert.Equal(64, key.Length);
        Assert.Matches("^[0-9a-f]{64}$", key);
    }

    [Fact]
    public void ASecondCallReadsBackTheSameKeyRatherThanGeneratingANewOne()
    {
        string path = Path.Combine(_directory, "mesh.key");

        string first = MeshKeyStore.EnsureKey(path);
        string second = MeshKeyStore.EnsureKey(path);

        Assert.Equal(first, second);
    }

    [Fact]
    public void ConcurrentFirstCallersAllEndUpWithTheSameKey()
    {
        string path = Path.Combine(_directory, "mesh.key");

        var results = new string[8];
        Parallel.For(0, results.Length, i => results[i] = MeshKeyStore.EnsureKey(path));

        Assert.All(results, r => Assert.Equal(results[0], r));
    }

    /// <summary>Simultaneous key creation from several threads never hands one of them an empty
    /// or malformed key, whatever else it ends up returning.</summary>
    [Fact]
    public void ConcurrentFirstCallersNeverReturnAnEmptyOrMalformedKey()
    {
        string path = Path.Combine(_directory, "mesh.key");

        var results = new string[16];
        Parallel.For(0, results.Length, i => results[i] = MeshKeyStore.EnsureKey(path));

        Assert.All(results, r => Assert.True(MeshKeyStore.IsValidKey(r)));
    }

    [Fact]
    public void AMissingFileIsNotAValidKey()
    {
        Assert.False(MeshKeyStore.TryRead(Path.Combine(_directory, "nope.key"), out _));
    }

    [Fact]
    public void AnEmptyFileRetriesThenIsTreatedAsAbsent()
    {
        string path = Path.Combine(_directory, "mesh.key");
        Directory.CreateDirectory(_directory);
        File.WriteAllText(path, "");

        Assert.False(MeshKeyStore.TryRead(path, out _));
    }

    [Fact]
    public void WriteAtomicOverwritesWhateverWasThereWithAValidKey()
    {
        string path = Path.Combine(_directory, "mesh.key");
        MeshKeyStore.WriteAtomic(path, MeshKeyStore.GenerateHexKey());
        string second = MeshKeyStore.GenerateHexKey();

        MeshKeyStore.WriteAtomic(path, second);

        Assert.True(MeshKeyStore.TryRead(path, out string read));
        Assert.Equal(second, read);
    }

    /// <summary>WriteAtomic is the only path anything is ever persisted through, so it is the
    /// one place that must never let an empty key reach disk.</summary>
    [Fact]
    public void WriteAtomicRejectsAnEmptyKeyAndLeavesAnExistingValidFileUnchanged()
    {
        string path = Path.Combine(_directory, "mesh.key");
        string original = MeshKeyStore.GenerateHexKey();
        MeshKeyStore.WriteAtomic(path, original);

        Assert.Throws<ArgumentException>(() => MeshKeyStore.WriteAtomic(path, ""));

        Assert.True(MeshKeyStore.TryRead(path, out string read));
        Assert.Equal(original, read);
    }

    [Fact]
    public void WriteAtomicRejectsAMalformedKeyAndLeavesAnExistingValidFileUnchanged()
    {
        string path = Path.Combine(_directory, "mesh.key");
        string original = MeshKeyStore.GenerateHexKey();
        MeshKeyStore.WriteAtomic(path, original);

        Assert.Throws<ArgumentException>(() => MeshKeyStore.WriteAtomic(path, "not-a-valid-key"));

        Assert.True(MeshKeyStore.TryRead(path, out string read));
        Assert.Equal(original, read);
    }

    [Fact]
    public void FingerprintIsSixteenLowercaseHexCharsAndDependsOnTheWholeKey()
    {
        string a = MeshKeyStore.Fingerprint("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcd");
        string b = MeshKeyStore.Fingerprint("0123456789abcdef0123456789abcdef0123456789abcdef0123456789abce");

        Assert.Matches("^[0-9a-f]{16}$", a);
        Assert.NotEqual(a, b);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
