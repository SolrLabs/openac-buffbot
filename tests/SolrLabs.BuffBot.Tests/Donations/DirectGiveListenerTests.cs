using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Tests.Donations;

public sealed class DirectGiveListenerTests
{
    private const uint GiverId = 42u;

    private static PluginChatMessage SystemChat(string text) =>
        new(Sequence: 1, SenderObjectId: 0, Kind: (int)ChatKind.System, Sender: string.Empty, text, ChannelName: "System");

    private static PluginWorldObject Player(uint objectId, string name) =>
        new(objectId, WeenieClassId: 1, name, PluginObjectClass.Unknown, ItemType: 0, ContainerObjectId: 0, WielderObjectId: 0);

    [Fact]
    public void ARefusedGiveResolvesTheGiverAndProducesOneRefusal()
    {
        var listener = new DirectGiveListener();
        PluginWorldObject[] objects = [Player(GiverId, "probe")];

        DirectGiveOutcome outcome = listener.Process(
            [SystemChat("probe tries to give you a Prismatic Pea.")], objects);

        DirectGiveRefusal refusal = Assert.Single(outcome.Refusals);
        Assert.Equal(GiverId, refusal.GiverObjectId);
        Assert.Equal("probe", refusal.GiverName);
        Assert.False(outcome.WarnOperatorAboutLandedGive);
    }

    [Fact]
    public void ARefusedGiveFromAGiverNotCapturedThisTickProducesNoRefusal()
    {
        var listener = new DirectGiveListener();

        DirectGiveOutcome outcome = listener.Process(
            [SystemChat("probe tries to give you a Prismatic Pea.")], capturedObjects: []);

        Assert.Empty(outcome.Refusals);
    }

    [Fact]
    public void ALandedGiveWarnsTheOperatorOnceEvenAcrossManyTicks()
    {
        var listener = new DirectGiveListener();
        PluginChatMessage landed = SystemChat("probe gives you a Prismatic Pea.");

        DirectGiveOutcome first = listener.Process([landed], capturedObjects: []);
        DirectGiveOutcome second = listener.Process([landed], capturedObjects: []);
        DirectGiveOutcome third = listener.Process([SystemChat("probe gives you 5 Iron Scarabs.")], capturedObjects: []);

        Assert.True(first.WarnOperatorAboutLandedGive);
        Assert.False(second.WarnOperatorAboutLandedGive);
        Assert.False(third.WarnOperatorAboutLandedGive);
        Assert.Empty(first.Refusals);
    }

    [Fact]
    public void ALandedGiveNeverProducesATellToTheGiver()
    {
        var listener = new DirectGiveListener();
        PluginWorldObject[] objects = [Player(GiverId, "probe")];

        DirectGiveOutcome outcome = listener.Process(
            [SystemChat("probe gives you a Prismatic Pea.")], objects);

        Assert.Empty(outcome.Refusals);
    }

    [Fact]
    public void AnUnrelatedSystemMessageProducesNeitherARefusalNorAWarning()
    {
        var listener = new DirectGiveListener();

        DirectGiveOutcome outcome = listener.Process([SystemChat("Trade Complete!")], capturedObjects: []);

        Assert.Empty(outcome.Refusals);
        Assert.False(outcome.WarnOperatorAboutLandedGive);
    }
}
