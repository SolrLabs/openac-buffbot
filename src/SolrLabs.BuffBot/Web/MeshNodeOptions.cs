using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Timing;

namespace SolrLabs.BuffBot.Web;

/// <summary>Everything <see cref="MeshNode"/> needs that a test wants to swap out: port, key file, opener file, clock, and every interval or window are constructor-injectable, so a test suite never binds the production port, touches the production key file, or waits out a production window.</summary>
internal sealed record MeshNodeOptions(
    int Port,
    string KeyPath,
    string LinkFilePath,
    IClock Clock,
    byte[] PageBytes,
    Action<string> LogInfo,
    TimeSpan? HeartbeatInterval = null,
    TimeSpan? StaleAfter = null,
    TimeSpan? RemoveAfter = null,
    TimeSpan? MaxJitter = null,
    TimeSpan? Grace = null,
    TimeSpan? Tolerance = null,
    // This node's own contributor ledger, read fresh on every request rather than carried in MeshStatus, since the ledger has no bound.
    Func<uint, IReadOnlyList<Contributor>>? ReadContributors = null);
