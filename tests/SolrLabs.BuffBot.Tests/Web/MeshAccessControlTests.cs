using SolrLabs.BuffBot.Web;

namespace SolrLabs.BuffBot.Tests.Web;

/// <summary>The Host/Origin/token matrix every request answers to, independent of any
/// socket.</summary>
public sealed class MeshAccessControlTests
{
    private const int Port = 8347;
    private const string Key = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

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

    [Fact]
    public void TokenIsValidWithTheExactBearerKey() =>
        Assert.True(MeshAccessControl.TokenValid($"Bearer {Key}", Key));

    [Fact]
    public void TokenIsInvalidWithNoAuthorizationHeader() =>
        Assert.False(MeshAccessControl.TokenValid(null, Key));

    [Fact]
    public void TokenIsInvalidWithoutTheBearerPrefix() =>
        Assert.False(MeshAccessControl.TokenValid(Key, Key));

    [Fact]
    public void TokenIsInvalidWithTheWrongKey() =>
        Assert.False(MeshAccessControl.TokenValid("Bearer wrong-key", Key));

    [Fact]
    public void TokenIsInvalidWithAKeyOfDifferentLength() =>
        Assert.False(MeshAccessControl.TokenValid("Bearer short", Key));

    /// <summary>An empty expected key must never authorize anything, including the one
    /// presented token that would otherwise match it byte-for-byte.</summary>
    [Fact]
    public void TokenIsInvalidWhenTheExpectedKeyIsEmpty() =>
        Assert.False(MeshAccessControl.TokenValid("Bearer ", ""));

    [Fact]
    public void TokenIsInvalidWhenTheExpectedKeyIsSixtyThreeCharacters()
    {
        string shortKey = new string('a', 63);
        Assert.False(MeshAccessControl.TokenValid($"Bearer {shortKey}", shortKey));
    }

    [Fact]
    public void TokenIsInvalidWhenTheExpectedKeyIsUppercaseHex()
    {
        string upperKey = new string('A', 64);
        Assert.False(MeshAccessControl.TokenValid($"Bearer {upperKey}", upperKey));
    }
}
