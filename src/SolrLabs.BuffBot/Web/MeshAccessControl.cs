namespace SolrLabs.BuffBot.Web;

/// <summary>Host and Origin pin every request to this loopback port regardless of path — the
/// whole access boundary. Pure string comparisons — no socket, no clock.</summary>
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
}
