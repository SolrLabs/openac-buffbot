using System.Text;

namespace SolrLabs.BuffBot.Settings;

/// <summary>A place values might already exist, distinct from <see cref="AcDream.Plugin.Abstractions.IPluginStorage"/> in one way on purpose: a failure to read is never folded into "nothing here", so a caller can tell an empty source from one it could not get to.</summary>
internal interface ILegacyStorageSource
{
    bool IsAvailable { get; }

    IReadOnlyList<string> ListKeys();

    string? ReadText(string key);
}

/// <summary>Reads the one-file-per-key tree a pre-single-root client's file-backed plugin storage wrote. Never writes to it and never deletes anything under it — every call here is a read.</summary>
internal sealed class LegacyStorageSource : ILegacyStorageSource
{
    private readonly string _root;

    internal LegacyStorageSource(string root) => _root = Path.GetFullPath(root);

    public bool IsAvailable => Directory.Exists(_root);

    public IReadOnlyList<string> ListKeys()
    {
        if (!Directory.Exists(_root))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
    }

    public string? ReadText(string key)
    {
        string path = Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }
}
