using System.Security.Cryptography;
using System.Text;

namespace SolrLabs.BuffBot.Web;

/// <summary>Host and Origin pin every request to this loopback port regardless of path, and the bearer token gates <c>/api/</c> and <c>/mesh/heartbeat</c> on top of that. Pure string and byte comparisons — no socket, no clock.</summary>
internal static class MeshAccessControl
{
    internal static bool HostAllowed(string? host, int port) =>
        host is not null
        && (string.Equals(host, $"127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, $"localhost:{port}", StringComparison.OrdinalIgnoreCase));

    /// <summary>Absent is allowed — a same-origin browser navigation or a plain HTTP client may
    /// send no <c>Origin</c> at all; only a <em>present but wrong</em> one is refused.</summary>
    internal static bool OriginAllowed(string? origin, int port) =>
        origin is null
        || string.Equals(origin, $"http://127.0.0.1:{port}", StringComparison.OrdinalIgnoreCase)
        || string.Equals(origin, $"http://localhost:{port}", StringComparison.OrdinalIgnoreCase);

    /// <summary>Never authorizes against a key that is not itself <see cref="MeshKeyStore.IsValidKey"/> — an empty or malformed <paramref name="key"/> means every presented token is refused.</summary>
    internal static bool TokenValid(string? authorizationHeader, string key)
    {
        if (!MeshKeyStore.IsValidKey(key))
            return false;

        const string prefix = "Bearer ";
        if (authorizationHeader is null || !authorizationHeader.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        byte[] presented = Encoding.ASCII.GetBytes(authorizationHeader[prefix.Length..]);
        byte[] expected = Encoding.ASCII.GetBytes(key);
        return presented.Length == expected.Length && CryptographicOperations.FixedTimeEquals(presented, expected);
    }
}
