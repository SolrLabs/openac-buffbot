using System.Text;
using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Settings;

/// <summary>Plugin-owned fallback for <see cref="IPluginStorage"/>, reached for only when the host's own storage reports itself unavailable. Rooted one level below <see cref="MeshKeyStore.BaseDirectory"/>, in <c>storage/</c>, so the two never collide. Every write goes through <see cref="AtomicFile.Write"/>. Keys are relative paths, e.g. <c>settings/1342177284</c>; a rooted key or one that climbs out via <c>..</c> is refused.</summary>
internal sealed class PluginFileStorage : IPluginStorage
{
    internal static string DefaultRoot() => Path.Combine(MeshKeyStore.BaseDirectory(), "storage");

    private readonly string _root;

    internal PluginFileStorage(string root) => _root = Path.GetFullPath(root);

    public bool IsAvailable
    {
        get
        {
            try
            {
                Directory.CreateDirectory(_root);
                return true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public string? ReadText(string key)
    {
        if (!TryResolve(key, out string path))
            return null;

        try
        {
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public IReadOnlyList<string> List(string prefix)
    {
        if (!TryResolveDirectory(prefix, out string directory) || !Directory.Exists(directory))
            return Array.Empty<string>();

        return Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(static key => key, StringComparer.Ordinal)
            .ToArray();
    }

    public void WriteText(string key, string content)
    {
        if (!TryResolve(key, out string path))
            throw new ArgumentException($"'{key}' is not a valid plugin storage key.", nameof(key));

        AtomicFile.Write(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(content));
    }

    public bool Delete(string key)
    {
        if (!TryResolve(key, out string path) || !File.Exists(path))
            return false;

        try
        {
            File.Delete(path);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Resolves a relative key beneath <see cref="_root"/>, refusing a rooted key or one that climbs out via <c>..</c>.</summary>
    private bool TryResolve(string key, out string path)
    {
        path = "";
        if (string.IsNullOrEmpty(key) || Path.IsPathRooted(key))
            return false;

        string combined = Path.GetFullPath(Path.Combine(_root, key));
        string relative = Path.GetRelativePath(_root, combined);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return false;
        }

        path = combined;
        return true;
    }

    private bool TryResolveDirectory(string prefix, out string directory)
    {
        if (prefix.Length == 0)
        {
            directory = _root;
            return true;
        }

        return TryResolve(prefix, out directory);
    }
}
