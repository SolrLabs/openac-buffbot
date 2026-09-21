using SolrLabs.BuffBot.Portals;

namespace SolrLabs.BuffBot.Tests.Settings;

public sealed class PortalTieTests
{
    [Fact]
    public void ClampedTrimsLeadingAndTrailingWhitespace() =>
        Assert.Equal(
            "Temple of Enlightenment",
            new PortalTie("  Temple of Enlightenment  ", PortalDirection.Front).Clamped().Description);

    [Fact]
    public void ClampedCollapsesANewlineIntoASpace() =>
        Assert.Equal("a b", new PortalTie("a\nb", PortalDirection.Front).Clamped().Description);

    [Fact]
    public void ClampedCapsAtOneHundredSixtyCharacters()
    {
        string tooLong = new string('x', 200);

        PortalTie clamped = new PortalTie(tooLong, PortalDirection.Front).Clamped();

        Assert.Equal(160, clamped.Description.Length);
        Assert.Equal(new string('x', 160), clamped.Description);
    }

    [Fact]
    public void IsOfferedIsFalseForAnEmptyOrWhitespaceOnlyDescription()
    {
        Assert.False(PortalTie.Empty.IsOffered);
        Assert.False(new PortalTie("   ", PortalDirection.Front).IsOffered);
    }

    [Fact]
    public void IsOfferedIsTrueOnceTheClampedDescriptionIsNonEmpty() =>
        Assert.True(new PortalTie("Holtburg", PortalDirection.Left).IsOffered);

    [Theory]
    [InlineData("front", "front")]
    [InlineData("right", "right")]
    [InlineData("behind", "behind")]
    [InlineData("left", "left")]
    [InlineData("sideways", "front")]
    [InlineData(null, "front")]
    public void ParseFallsBackToFrontOnAnythingUnrecognized(string? text, string expectedWireWord) =>
        Assert.Equal(expectedWireWord, PortalDirectionText.ToText(PortalDirectionText.Parse(text)));

    [Fact]
    public void ToTextRoundTripsEveryDirection()
    {
        Assert.Equal("front", PortalDirectionText.ToText(PortalDirection.Front));
        Assert.Equal("right", PortalDirectionText.ToText(PortalDirection.Right));
        Assert.Equal("behind", PortalDirectionText.ToText(PortalDirection.Behind));
        Assert.Equal("left", PortalDirectionText.ToText(PortalDirection.Left));
    }
}
