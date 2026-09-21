using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Portals;

/// <summary>The one place <see cref="IPortalFacing"/> touches <see cref="IPluginHost"/>.</summary>
internal sealed class HostPortalFacing : IPortalFacing
{
    private readonly INavigationAutomation _navigation;

    internal HostPortalFacing(INavigationAutomation navigation)
    {
        _navigation = navigation;
    }

    public float? CurrentHeading
    {
        get
        {
            PluginNavigationSnapshot snapshot = _navigation.Snapshot;
            return snapshot.IsAvailable ? snapshot.Position.HeadingDegrees : null;
        }
    }

    public bool Face(float degrees) => _navigation.FaceHeading(degrees) != PluginNavigationCommandStatus.Unavailable;
}
