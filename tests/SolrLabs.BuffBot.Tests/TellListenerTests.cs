using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;

namespace SolrLabs.BuffBot.Tests;

public sealed class TellListenerTests
{
    private const uint OwnObjectId = 0x50000999u;
    private const uint OtherObjectId = 0x50001234u;
    private const uint NpcObjectId = 0x7EA30005u;

    [Fact]
    public void ExtractTellsReturnsOnlyKindThreeMessages()
    {
        var listener = new TellListener();
        PluginChatMessage tell = Message(1, kind: 3, text: "hey");
        PluginChatMessage say = Message(2, kind: 0, text: "hi all");

        IReadOnlyList<PluginChatMessage> result =
            listener.ExtractTells([say, tell], OwnObjectId);

        Assert.Single(result);
        Assert.Equal(tell, result[0]);
    }

    [Fact]
    public void ExtractTellsAdvancesSequencePastEverythingSeenIncludingNonTells()
    {
        var listener = new TellListener();
        PluginChatMessage say = Message(5, kind: 0, text: "hi all");
        PluginChatMessage tell = Message(3, kind: 3, text: "hey");

        listener.ExtractTells([say, tell], OwnObjectId);

        Assert.Equal(5ul, listener.LastSequence);
    }

    [Fact]
    public void EmptyCaptureLeavesSequenceAlone()
    {
        var listener = new TellListener();
        PluginChatMessage tell = Message(7, kind: 3, text: "hey");
        listener.ExtractTells([tell], OwnObjectId);

        listener.ExtractTells(Array.Empty<PluginChatMessage>(), OwnObjectId);

        Assert.Equal(7ul, listener.LastSequence);
    }

    [Fact]
    public void TellsFromTheBotsOwnCharacterAreExcluded()
    {
        var listener = new TellListener();
        PluginChatMessage ownTell = Message(1, kind: 3, text: "hey", senderObjectId: OwnObjectId);
        PluginChatMessage othersTell = Message(2, kind: 3, text: "hey", senderObjectId: OtherObjectId);

        IReadOnlyList<PluginChatMessage> result =
            listener.ExtractTells([ownTell, othersTell], OwnObjectId);

        Assert.Single(result);
        Assert.Equal(othersTell, result[0]);
    }

    [Fact]
    public void OwnTellsStillAdvanceTheSequence()
    {
        var listener = new TellListener();
        PluginChatMessage ownTell = Message(4, kind: 3, text: "hey", senderObjectId: OwnObjectId);

        listener.ExtractTells([ownTell], OwnObjectId);

        Assert.Equal(4ul, listener.LastSequence);
    }

    [Fact]
    public void ANpcDialogueLineArrivingAsATellIsDroppedSilently()
    {
        var listener = new TellListener();
        PluginChatMessage npcTell = Message(1, kind: 3, text: "I have nothing for you.", senderObjectId: NpcObjectId);

        IReadOnlyList<PluginChatMessage> result = listener.ExtractTells([npcTell], OwnObjectId);

        Assert.Empty(result);
    }

    [Fact]
    public void ANpcTellStillAdvancesTheSequence()
    {
        var listener = new TellListener();
        PluginChatMessage npcTell = Message(6, kind: 3, text: "I have nothing for you.", senderObjectId: NpcObjectId);

        listener.ExtractTells([npcTell], OwnObjectId);

        Assert.Equal(6ul, listener.LastSequence);
    }

    [Fact]
    public void ATellWithAZeroSenderIsDroppedSilently()
    {
        var listener = new TellListener();
        PluginChatMessage zeroSenderTell = Message(1, kind: 3, text: "hey", senderObjectId: 0);

        IReadOnlyList<PluginChatMessage> result = listener.ExtractTells([zeroSenderTell], OwnObjectId);

        Assert.Empty(result);
    }

    [Fact]
    public void APlayerTellIsStillAnswered()
    {
        var listener = new TellListener();
        PluginChatMessage playerTell = Message(1, kind: 3, text: "buff", senderObjectId: OtherObjectId);

        IReadOnlyList<PluginChatMessage> result = listener.ExtractTells([playerTell], OwnObjectId);

        Assert.Single(result);
        Assert.Equal(playerTell, result[0]);
    }

    private static PluginChatMessage Message(
        ulong sequence, int kind, string text, uint senderObjectId = OtherObjectId) =>
        new(
            Sequence: sequence,
            SenderObjectId: senderObjectId,
            Kind: kind,
            Sender: "Someone",
            Text: text,
            ChannelName: string.Empty);
}
