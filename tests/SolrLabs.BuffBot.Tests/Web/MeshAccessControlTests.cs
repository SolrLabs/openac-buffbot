using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>The Host/Origin matrix every request answers to, independent of any socket.</summary>
public sealed class MeshAccessControlTests
{
    private const int Port = 8347;

    [Theory]
    [InlineData("127.0.0.1:8347", true)]
    [InlineData("localhost:8347", true)]
    [InlineData("LOCALHOST:8347", true)]
    [InlineData("127.0.0.1:1234", false)]
    [InlineData("example.com:8347", false)]
    [InlineData(null, false)]
    public void HostIsAllowedOnlyForThisLoopbackPort(string? host, bool expected) =>
        Assert.Equal(expected, MeshAccessControl.HostAllowed(host, Port));

    [Theory]
    [InlineData(null, true)] // absent is allowed
    [InlineData("http://127.0.0.1:8347", true)]
    [InlineData("http://localhost:8347", true)]
    [InlineData("http://127.0.0.1:1234", false)]
    [InlineData("https://127.0.0.1:8347", false)]
    [InlineData("http://evil.example:8347", false)]
    public void OriginIsAllowedOnlyAbsentOrForThisLoopbackPort(string? origin, bool expected) =>
        Assert.Equal(expected, MeshAccessControl.OriginAllowed(origin, Port));
}
