namespace SolrLabs.BuffBot.Portals;

/// <summary>Where a summoned portal faces relative to the caster's own recorded heading.</summary>
internal static class PortalHeading
{
    internal static float For(float recordedDegrees, PortalDirection direction)
    {
        float offset = direction switch
        {
            PortalDirection.Right => 90f, PortalDirection.Behind => 180f, PortalDirection.Left => 270f, _ => 0f,
        };
        float h = (recordedDegrees + offset) % 360f;
        return h < 0 ? h + 360f : h;
    }

    /// <summary>True once the two headings are within <paramref name="toleranceDegrees"/> of each
    /// other, wrapping correctly across the 0/360 seam.</summary>
    internal static bool IsWithin(float headingDegrees, float targetDegrees, float toleranceDegrees)
    {
        float diff = ((headingDegrees - targetDegrees + 540f) % 360f) - 180f;
        return MathF.Abs(diff) <= toleranceDegrees;
    }
}
