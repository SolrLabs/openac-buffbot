using SolrLabs.BuffBot.Portals;

namespace SolrLabs.BuffBot.Tests.Portals;

/// <summary>Host-free coverage of <see cref="PortalHeading"/>.</summary>
public sealed class PortalHeadingTests
{
    [Fact]
    public void RightWrapsPastZero()
    {
        Assert.Equal(30f, PortalHeading.For(300f, PortalDirection.Right));
    }

    [Fact]
    public void LeftFromZeroIsTwoSeventy()
    {
        Assert.Equal(270f, PortalHeading.For(0f, PortalDirection.Left));
    }

    [Fact]
    public void FrontLeavesTheRecordedHeadingUnchanged()
    {
        Assert.Equal(359.5f, PortalHeading.For(359.5f, PortalDirection.Front));
    }
}
