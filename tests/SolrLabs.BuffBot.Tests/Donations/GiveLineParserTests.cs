using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Tests.Donations;

/// <summary>Host-free coverage of the "&lt;Name&gt; gives you &lt;item&gt;." chat line: <see
/// cref="ChatKind.System"/>, no sender.</summary>
public sealed class GiveLineParserTests
{
    private static PluginChatMessage Message(string text, int kind = (int)ChatKind.System, uint senderObjectId = 0u) =>
        new(Sequence: 1, senderObjectId, kind, Sender: string.Empty, text, ChannelName: "System");

    [Fact]
    public void ParsesASingularGiftWithAnArticle()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("probe gives you a Fine Aged Cheese."));

        Assert.NotNull(line);
        Assert.Equal("probe", line!.Value.GiverName);
        Assert.Equal("Fine Aged Cheese", line.Value.ItemName);
        Assert.Equal(1, line.Value.Count);
    }

    [Fact]
    public void ParsesACountedStack()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("probe gives you 5 Pyreal Scarabs."));

        Assert.NotNull(line);
        Assert.Equal("probe", line!.Value.GiverName);
        Assert.Equal("Pyreal Scarabs", line.Value.ItemName);
        Assert.Equal(5, line.Value.Count);
    }

    [Fact]
    public void ParsesACountWithAThousandsSeparator()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("probe gives you 10,000 Pyreals."));

        Assert.NotNull(line);
        Assert.Equal("Pyreals", line!.Value.ItemName);
        Assert.Equal(10_000, line.Value.Count);
    }

    [Fact]
    public void ParsesAnAdminNameCarryingThePlusPrefix()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("+Archer gives you a Diamond."));

        Assert.NotNull(line);
        Assert.Equal("+Archer", line!.Value.GiverName);
        Assert.Equal("Diamond", line.Value.ItemName);
    }

    [Fact]
    public void ParsesAMultiWordGiverName()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("Dereth Dweller gives you a Splitting Tool."));

        Assert.NotNull(line);
        Assert.Equal("Dereth Dweller", line!.Value.GiverName);
        Assert.Equal("Splitting Tool", line.Value.ItemName);
    }

    [Fact]
    public void ParsesAnItemStartingWithAn()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("probe gives you an Iron Key."));

        Assert.NotNull(line);
        Assert.Equal("Iron Key", line!.Value.ItemName);
    }

    [Fact]
    public void ARefusedGiveIsNotALandedGive()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("probe tries to give you a Fine Aged Cheese."));

        Assert.Null(line);
    }

    [Fact]
    public void ATellIsNeverParsedAsALandedGive()
    {
        GiveLine? line = GiveLineParser.TryParse(
            Message("probe gives you a Fine Aged Cheese.", kind: (int)ChatKind.Tell, senderObjectId: 42u));

        Assert.Null(line);
    }

    [Fact]
    public void ASystemMessageWithASenderIsNotALandedGive()
    {
        GiveLine? line = GiveLineParser.TryParse(
            Message("probe gives you a Fine Aged Cheese.", senderObjectId: 42u));

        Assert.Null(line);
    }

    [Fact]
    public void AnUnrelatedSystemMessageIsNotALandedGive()
    {
        GiveLine? line = GiveLineParser.TryParse(Message("You cast Strength Other."));

        Assert.Null(line);
    }

    [Fact]
    public void ParsesTheGiverNameOutOfARefusedGive()
    {
        string? giverName =
            GiveLineParser.TryParseTriesToGiveGiverName(Message("probe tries to give you a Fine Aged Cheese."));

        Assert.Equal("probe", giverName);
    }

    [Fact]
    public void ALandedGiveHasNoTriesToGiveGiverName()
    {
        string? giverName =
            GiveLineParser.TryParseTriesToGiveGiverName(Message("probe gives you a Fine Aged Cheese."));

        Assert.Null(giverName);
    }
}
