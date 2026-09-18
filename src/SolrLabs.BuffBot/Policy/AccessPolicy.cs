using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Policy;

internal enum AccessMode
{
    Free,
    Paid,
    Fellowship,
}

internal readonly record struct AccessRequest(string Sender, string Text);

/// <summary>Allowed, or refused with a reason drawn from <see cref="DefaultReplies"/>.</summary>
internal sealed record AccessDecision(bool IsAllowed, string? Reason)
{
    internal static AccessDecision Allowed() => new(true, null);

    internal static AccessDecision Refused(string reason) => new(false, reason);
}

/// <summary><see cref="AccessMode.Free"/> always allows; the other two modes refuse every request.</summary>
internal sealed class AccessPolicy
{
    private readonly AccessMode _mode;

    internal AccessPolicy(AccessMode mode) => _mode = mode;

    internal AccessDecision Evaluate(AccessRequest request) => _mode switch
    {
        AccessMode.Free => AccessDecision.Allowed(),
        AccessMode.Paid => AccessDecision.Refused(DefaultReplies.PaidNotAvailable),
        AccessMode.Fellowship => AccessDecision.Refused(DefaultReplies.FellowshipNotAvailable),
        _ => throw new InvalidOperationException($"Unknown access mode: {_mode}."),
    };
}
