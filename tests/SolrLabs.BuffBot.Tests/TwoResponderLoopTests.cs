using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Guard;
using SolrLabs.BuffBot.Policy;
using SolrLabs.BuffBot.Requests;
using SolrLabs.BuffBot.Tests.Timing;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests;

public sealed class TwoResponderLoopTests
{
    private const uint BotAId = 1;
    private const string BotAName = "buffbot";
    private const uint BotBId = 2;
    private const string BotBName = "probe";
    private const int MaxExchanges = 20;

    [Fact]
    public void TwoBotsAnsweringEachOtherStopWithinAHandfulOfMessages()
    {
        Responder botA = NewResponder();
        Responder botB = NewResponder();

        string? pending = "buff";
        bool aIsSending = true; // A's text is what's currently in flight, addressed to B
        int exchanged = 0;

        while (pending is not null && exchanged < MaxExchanges)
        {
            exchanged++;

            Responder receiver = aIsSending ? botB : botA;
            string incomingSenderName = aIsSending ? BotAName : BotBName;
            uint incomingSenderId = aIsSending ? BotAId : BotBId;

            var tell = new PluginChatMessage(
                Sequence: (ulong)exchanged,
                SenderObjectId: incomingSenderId,
                Kind: (int)ChatKind.Tell,
                Sender: incomingSenderName,
                Text: pending,
                ChannelName: string.Empty);

            // A Responder always replies to the tell's own sender, so the reply this produces
            // is addressed back to whoever just sent it — that reply becomes the next hop.
            string? reply = receiver.Reply(tell, exchanged);
            pending = reply is null ? null : StripTellPrefix(reply, incomingSenderName);
            aIsSending = !aIsSending;
        }

        Assert.True(
            exchanged < MaxExchanges,
            $"the exchange never stopped itself within {MaxExchanges} messages");
    }

    [Fact]
    public void TheExactIncidentReplyIsDroppedRatherThanMisunderstood()
    {
        // Two bots running this vocabulary must drop each other's replies as bot-shaped, or
        // they answer each other forever.
        Responder probesBot = NewResponder();

        var closingReply = new PluginChatMessage(
            Sequence: 1,
            SenderObjectId: BotAId,
            Kind: (int)ChatKind.Tell,
            Sender: BotAName,
            Text: "All set: cast Strength Other.",
            ChannelName: string.Empty);

        string? probesReply = probesBot.Reply(closingReply, tellsAnswered: 1);

        Assert.Null(probesReply);
    }

    private static Responder NewResponder() =>
        new(
            DefaultVocabulary.Table,
            new AccessPolicy(AccessMode.Free),
            new RequestQueue(5),
            "0.1.0",
            new LoopGuard(new FakeClock(), _ => { }, _ => { }));

    private static string StripTellPrefix(string tellCommand, string recipientName)
    {
        string prefix = $"/tell {recipientName}, ";
        Assert.StartsWith(prefix, tellCommand);
        return tellCommand[prefix.Length..];
    }
}
