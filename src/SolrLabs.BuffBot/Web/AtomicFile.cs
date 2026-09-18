namespace SolrLabs.BuffBot.Web;

/// <summary><see cref="Write"/> renames a temp file over the target, so a concurrent reader never
/// sees a torn file. <see cref="TryCreate"/> instead uses OS-level exclusivity for the one place more than one writer can race.</summary>
internal static class AtomicFile
{
    internal static void Write(string path, byte[] content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory ?? string.Empty, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        File.WriteAllBytes(tempPath, content);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>Leaves an existing target untouched and reports failure instead of overwriting it. A loser's own bytes never land anywhere on disk.</summary>
    internal static bool TryCreate(string path, byte[] content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.Write(content);
        }
        catch (IOException)
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return true;
    }
}
