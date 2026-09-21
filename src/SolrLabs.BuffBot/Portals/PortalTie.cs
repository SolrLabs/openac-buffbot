namespace SolrLabs.BuffBot.Portals;

/// <summary>Relative to the caster's own facing once a requester lands.</summary>
internal enum PortalDirection
{
    Front,
    Right,
    Behind,
    Left,
}

/// <summary>An operator's per-character tie: where a recall or a summon lands, and which way the requester should expect to be facing.
/// <see cref="Clamped"/> is the only thing that touches the raw <see cref="Description"/>; everything else treats it as already clamped.</summary>
internal sealed record PortalTie(string Description, PortalDirection Direction)
{
    internal const int MaxDescriptionLength = 160;

    internal static readonly PortalTie Empty = new("", PortalDirection.Front);

    /// <summary>True once there is something to tell a requester — an empty description offers
    /// nothing, regardless of direction.</summary>
    internal bool IsOffered => Clamped().Description.Length > 0;

    /// <summary>Trims the description, then collapses any newline into a space, then caps the
    /// result at <see cref="MaxDescriptionLength"/>.</summary>
    internal PortalTie Clamped()
    {
        string collapsed = Description.Trim().Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ');
        if (collapsed.Length > MaxDescriptionLength)
            collapsed = collapsed[..MaxDescriptionLength];
        return this with { Description = collapsed };
    }
}

/// <summary>The lower-case word a <see cref="PortalDirection"/> uses everywhere outside this
/// process — persisted storage and the console's wire.</summary>
internal static class PortalDirectionText
{
    internal static string ToText(PortalDirection direction) => direction switch
    {
        PortalDirection.Right => "right",
        PortalDirection.Behind => "behind",
        PortalDirection.Left => "left",
        _ => "front",
    };

    /// <summary>An unknown or missing word parses as <see cref="PortalDirection.Front"/> rather
    /// than throwing.</summary>
    internal static PortalDirection Parse(string? text) => text switch
    {
        "right" => PortalDirection.Right,
        "behind" => PortalDirection.Behind,
        "left" => PortalDirection.Left,
        _ => PortalDirection.Front,
    };
}
