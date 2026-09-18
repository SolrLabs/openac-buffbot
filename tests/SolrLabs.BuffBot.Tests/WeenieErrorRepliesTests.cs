using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

public sealed class WeenieErrorRepliesTests
{
    [Theory]
    [InlineData(0x0401u, "I'm out of mana")]
    [InlineData(0x0400u, "I'm out of the reagents for that spell")]
    [InlineData(0x042Cu, "I lost track of you mid-cast")]
    [InlineData(0x0550u, "you moved out of range while I was casting")]
    [InlineData(0x03FCu, "I don't actually know that spell anymore")]
    public void MappedCodesReturnPlainText(uint weenieError, string expected) =>
        Assert.Equal(expected, WeenieErrorReplies.Describe(weenieError));

    [Fact]
    public void UnmappedCodeFallsBackToTheRawNumber() =>
        Assert.Equal("weenie error 1234", WeenieErrorReplies.Describe(1234));
}
