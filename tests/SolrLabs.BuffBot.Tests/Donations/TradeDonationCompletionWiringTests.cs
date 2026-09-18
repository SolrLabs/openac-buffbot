using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Stats;

namespace SolrLabs.BuffBot.Tests.Donations;

public sealed class TradeDonationCompletionWiringTests
{
    private const uint LeadScarabWeenieId = 691u;
    private const uint PartnerId = 12345u;
    private const uint CharacterId = 999u;

    private static PluginChatMessage Chat(string text) =>
        new(Sequence: 1, SenderObjectId: 0, Kind: 0, Sender: string.Empty, text, ChannelName: string.Empty);

    [Fact]
    public void ACompletedDonationIsAppendedToTheLedgerAndFoldedIntoBotStats()
    {
        var view = new FakeTradeView { IsOpen = true, PartnerObjectId = PartnerId };
        var resolver = new FakeResolver().Set(PartnerId, 0u, "Archer").Set(1u, LeadScarabWeenieId, "Lead Scarab", stack: 5);
        var session = new TradeDonationSession(view, resolver.Resolve, botHoldsSplittingTool: () => false);

        view.PartnerItemIds.Add(1u);
        view.PartnerAccepted = true;
        session.Tick(1d, Array.Empty<PluginChatMessage>());
        Assert.Equal(1, view.AcceptCalls);

        session.Tick(1d, [Chat("Trade Complete!")]);
        CompletedDonation donation = Assert.Single(session.DrainCompletedDonations());

        var storage = new FakeStorage();
        var ledger = new ContributorLedger(storage, warn: _ => { });
        var stats = new BotStats();

        // The exact two calls PumpTradeDonations makes once a donation drains.
        ledger.Append(CharacterId, donation);
        int itemCount = 0;
        foreach (DonatedItem item in donation.Items)
            itemCount += item.Count;
        stats.RecordDonation(itemCount);

        Contributor contributor = Assert.Single(ledger.Load(CharacterId));
        Assert.Equal(PartnerId, contributor.DonorObjectId);
        Assert.Equal("Archer", contributor.DonorName);
        ContributorEntry entry = Assert.Single(contributor.Entries);
        Assert.Equal("Lead Scarab", entry.ItemName);
        Assert.Equal(5, entry.Count);

        BotStatsSnapshot snapshot = stats.Snapshot();
        Assert.Equal(1, snapshot.DonationsCompleted);
        Assert.Equal(5, snapshot.DonationItemsReceived);
    }

    /// <summary>A trade that closes without ever completing drains nothing — nothing is ever
    /// appended or counted for it (Behavior §2, "closed without completing").</summary>
    [Fact]
    public void ATradeThatClosesWithNothingAcceptedAppendsNothing()
    {
        var view = new FakeTradeView { IsOpen = true, PartnerObjectId = PartnerId };
        var resolver = new FakeResolver().Set(PartnerId, 0u, "Archer").Set(1u, LeadScarabWeenieId, "Lead Scarab");
        var session = new TradeDonationSession(view, resolver.Resolve, botHoldsSplittingTool: () => false);

        view.PartnerItemIds.Add(1u);
        session.Tick(1d, Array.Empty<PluginChatMessage>());

        view.IsOpen = false;
        session.Tick(1d, Array.Empty<PluginChatMessage>());

        Assert.Empty(session.DrainCompletedDonations());

        var ledger = new ContributorLedger(new FakeStorage(), warn: _ => { });
        Assert.Empty(ledger.Load(CharacterId));
    }
}
