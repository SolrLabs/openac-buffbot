using SolrLabs.BuffBot.Components;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary>Host-free coverage of the one shared casting-reagent classifier — every Scarab,
/// every Taper and every Pea, and nothing else, tested against real names.</summary>
public sealed class CastingReagentsTests
{
    [Theory]
    [InlineData(690u, "Pyreal Scarab")] // Pyreal
    [InlineData(37155u, "Mana Scarab")] // Mana — no matching pea
    [InlineData(8897u, "Platinum Scarab")] // Platinum — no matching pea
    [InlineData(691u, "Lead Scarab")]
    [InlineData(689u, "Iron Scarab")]
    [InlineData(686u, "Copper Scarab")]
    [InlineData(688u, "Silver Scarab")]
    [InlineData(687u, "Gold Scarab")]
    [InlineData(7299u, "Diamond Scarab")]
    public void EveryScarabIsARecognisedReagent(uint weenieClassId, string name) =>
        Assert.True(CastingReagents.IsReagent(weenieClassId, name));

    [Fact]
    public void ThePrismaticTaperIsARecognisedReagent() =>
        Assert.True(CastingReagents.IsReagent(20631u, "Prismatic Taper"));

    [Theory]
    [InlineData("Lead Pea")]
    [InlineData("Iron Pea")]
    [InlineData("Copper Pea")]
    [InlineData("Silver Pea")]
    [InlineData("Gold Pea")]
    [InlineData("Pyreal Pea")]
    [InlineData("Prismatic Pea")]
    public void EveryPeaIsARecognisedReagentByName(string name) =>
        Assert.True(CastingReagents.IsReagent(weenieClassId: 0u, name));

    [Fact]
    public void APeaNameMatchIsCaseInsensitive() =>
        Assert.True(CastingReagents.IsReagent(weenieClassId: 0u, "copper pea"));

    [Fact]
    public void ATalismanIsNotAReagentEvenThoughItSharesTheSpellComponentFlag() =>
        Assert.False(CastingReagents.IsReagent(4242u, "Ivory Talisman"));

    [Fact]
    public void APowderedGemIsNotAReagent() =>
        Assert.False(CastingReagents.IsReagent(4243u, "Powdered Agate"));

    [Fact]
    public void AHerbIsNotAReagent() =>
        Assert.False(CastingReagents.IsReagent(4244u, "Frankincense"));

    [Fact]
    public void APotionIsNotAReagent() =>
        Assert.False(CastingReagents.IsReagent(4245u, "Minor Mana Potion"));

    [Fact]
    public void AnInkIsNotAReagent() =>
        Assert.False(CastingReagents.IsReagent(4246u, "Ink"));

    [Fact]
    public void AnUnrelatedItemIsNotAReagent() =>
        Assert.False(CastingReagents.IsReagent(1u, "Fine Aged Cheese"));
}
