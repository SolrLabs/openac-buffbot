using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Portals;

namespace SolrLabs.BuffBot.Tests.Portals;

/// <summary>Host-free coverage of the one place <see cref="IPortalFacing"/> touches <see
/// cref="INavigationAutomation"/>.</summary>
public sealed class HostPortalFacingTests
{
    [Fact]
    public void CurrentHeadingReadsTheSnapshotsHeadingWhenAvailable()
    {
        var navigation = new FakeNavigation
        {
            Snapshot = new PluginNavigationSnapshot(
                IsAvailable: true, IsPortalSpace: false, LocalObjectId: 1,
                Position: new PluginNavigationPosition(
                    CellId: 0, EastWest: 0, NorthSouth: 0, Elevation: 0, HeadingDegrees: 77f, IsOutdoor: true),
                IsMoving: false, IsAirborne: false),
        };

        Assert.Equal(77f, new HostPortalFacing(navigation).CurrentHeading);
    }

    [Fact]
    public void CurrentHeadingIsNullWhenTheSnapshotHasNoPosition()
    {
        var navigation = new FakeNavigation { Snapshot = default };

        Assert.Null(new HostPortalFacing(navigation).CurrentHeading);
    }

    [Theory]
    [InlineData(PluginNavigationCommandStatus.Accepted, true)]
    [InlineData(PluginNavigationCommandStatus.Rejected, true)]
    [InlineData(PluginNavigationCommandStatus.Held, true)]
    [InlineData(PluginNavigationCommandStatus.Unavailable, false)]
    public void FaceSucceedsForEveryOutcomeExceptUnavailable(PluginNavigationCommandStatus status, bool expected)
    {
        var navigation = new FakeNavigation { FaceHeadingResult = status };

        Assert.Equal(expected, new HostPortalFacing(navigation).Face(180f));
        Assert.Equal(180f, navigation.LastFacedDegrees);
    }

    private sealed class FakeNavigation : INavigationAutomation
    {
        internal PluginNavigationCommandStatus FaceHeadingResult { get; set; } =
            PluginNavigationCommandStatus.Accepted;

        internal float? LastFacedDegrees { get; private set; }

        public PluginNavigationSnapshot Snapshot { get; set; }

        public bool TryGetObject(uint objectId, out PluginNavigationObject value)
        {
            value = default;
            return false;
        }

        public PluginNavigationCommandStatus SetMovementIntent(in PluginMovementIntent intent) =>
            PluginNavigationCommandStatus.Unavailable;

        public PluginNavigationCommandStatus ClearMovementIntent() => PluginNavigationCommandStatus.Unavailable;

        public PluginNavigationCommandStatus FaceHeading(float headingDegrees)
        {
            LastFacedDegrees = headingDegrees;
            return FaceHeadingResult;
        }
    }
}
