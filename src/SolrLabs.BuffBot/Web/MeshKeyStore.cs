using System.Security.Cryptography;
using System.Text;

namespace SolrLabs.BuffBot.Web;

/// <summary>The shared secret every node on the mesh proves it knows: 64 lowercase hex chars, held at <see cref="DefaultPath"/>. <see cref="EnsureKey"/> is the one caller still allowed to invent a value out of nothing; every other caller only ever reads (<see cref="TryRead"/>) or overwrites in place (<see cref="WriteAtomic"/>).</summary>
internal static class MeshKeyStore
{
    private const int KeyBytes = 32;
    private const int ReadAttempts = 5;
    private static readonly TimeSpan ReadRetryDelay = TimeSpan.FromMilliseconds(20);

    internal static string DefaultPath() => Path.Combine(BaseDirectory(), "mesh.key");

    internal static string BaseDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "solrlabs.buffbot");

    internal static string GenerateHexKey()
    {
        Span<byte> raw = stackalloc byte[KeyBytes];
        RandomNumberGenerator.Fill(raw);
        return Convert.ToHexString(raw).ToLowerInvariant();
    }

    internal static bool IsValidKey(string? candidate)
    {
        if (candidate is null || candidate.Length != 64)
            return false;
        foreach (char c in candidate)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                return false;
        return true;
    }

    /// <summary>The first 16 chars of lowercase-hex SHA-256 over the key, short enough to eyeball without exposing the key itself.</summary>
    internal static string Fingerprint(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(key))).ToLowerInvariant()[..16];

    /// <summary>Retries briefly on missing or invalid content before treating it as absent, closing the race where a reader gets there between another process's temp-file write and its rename.</summary>
    internal static bool TryRead(string path, out string key)
    {
        for (int attempt = 0; attempt < ReadAttempts; attempt++)
        {
            if (attempt > 0)
                Thread.Sleep(ReadRetryDelay);

            string? candidate = TryReadOnce(path);
            if (IsValidKey(candidate))
            {
                key = candidate!;
                return true;
            }
        }

        key = string.Empty;
        return false;
    }

    private static string? TryReadOnce(string path)
    {
        try
        {
            return File.ReadAllText(path).Trim();
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

    /// <summary>Refuses a key that is not itself <see cref="IsValidKey"/> — the only path anything is ever persisted through, so this stops an empty or malformed key from ever reaching disk.</summary>
    internal static void WriteAtomic(string path, string key)
    {
        if (!IsValidKey(key))
            throw new ArgumentException("The mesh key must be 64 lowercase hex characters.", nameof(key));
        AtomicFile.Write(path, Encoding.ASCII.GetBytes(key));
    }

    /// <summary>Creates the key file if this is the first process to get there, otherwise reads whatever is already on disk. <see cref="AtomicFile.TryCreate"/> makes the race safe: exactly one process's bytes ever land at <paramref name="path"/>, and every process that loses reads the winner's file back.</summary>
    internal static string EnsureKey(string path)
    {
        if (TryRead(path, out string existing))
            return existing;

        string generated = GenerateHexKey();
        if (AtomicFile.TryCreate(path, Encoding.ASCII.GetBytes(generated)))
            return generated;

        return TryRead(path, out string final) ? final : generated;
    }
}
