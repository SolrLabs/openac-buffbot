using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Chat;
using SolrLabs.BuffBot.Donations;
using SolrLabs.BuffBot.Vocabulary;

namespace SolrLabs.BuffBot.Tests.Donations;

public sealed class TradeDonationSessionTests
{
    private const uint LeadScarabWeenieId = 691u; // ContributionAdvisor.ScarabsWithPeas
    private const uint SplittingToolWeenieId = 8283u;
    private const uint PartnerId = 12345u;

    private static readonly IReadOnlyList<PluginChatMessage> NoChat = Array.Empty<PluginChatMessage>();

    private static PluginChatMessage Chat(string text) =>
        new(Sequence: 1, SenderObjectId: 0, Kind: 0, Sender: string.Empty, text, ChannelName: string.Empty);

    private static (FakeTradeView View, FakeResolver Resolver, TradeDonationSession Session) NewSession(
        bool botHoldsSplittingTool = false)
    {
        var view = new FakeTradeView { IsOpen = true, PartnerObjectId = PartnerId };
        var resolver = new FakeResolver().Set(PartnerId, 0u, "Archer");
        var session = new TradeDonationSession(view, resolver.Resolve, () => botHoldsSplittingTool);
        return (view, resolver, session);
    }

    [Fact]
    public void ItemsStagedOnTheBotsOwnSideEndTheTradeWithoutATell()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab");
        view.MyItemIds.Add(1u);

        session.Tick(1d, NoChat);

        Assert.Equal(1, view.EndCalls);
        Assert.Empty(session.DrainReplies());
    }

    [Fact]
    public void AWantedItemIsAcceptedOnceThePartnerHasAcceptedAndNeverBefore()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab", stack: 5);
        view.PartnerItemIds.Add(1u);

        session.Tick(1d, NoChat);
        Assert.Equal(0, view.AcceptCalls);

        view.PartnerAccepted = true;
        session.Tick(1d, NoChat);

        Assert.Equal(1, view.AcceptCalls);
    }

    [Fact]
    public void AnAcceptIsNeverRepeatedWhileTheBotsOwnSideIsStillAccepted()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab");
        view.PartnerItemIds.Add(1u);
        view.PartnerAccepted = true;

        session.Tick(1d, NoChat);
        Assert.Equal(1, view.AcceptCalls);

        view.MyAccepted = true; // the client's own optimistic flip, the same shape combat-mode uses
        session.Tick(1d, NoChat);
        session.Tick(1d, NoChat);

        Assert.Equal(1, view.AcceptCalls);
    }

    [Fact]
    public void AnUnwantedItemIsResetAndTheUnwantedNameIsTold()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, 4001u, "Yellow Wand");
        view.PartnerItemIds.Add(1u);

        session.Tick(1d, NoChat);

        Assert.Equal(1, view.ResetCalls);
        Assert.Equal(0, view.AcceptCalls);
        IReadOnlyList<string> replies = session.DrainReplies();
        Assert.Equal(
            "I can't use Yellow Wand. Please add only casting reagents, Splitting Tools, or trade notes of 10,000+.",
            Assert.Single(replies));
    }

    /// <summary>A Reset that has not yet propagated (this fake never auto-clears the stage on its
    /// own) must not be repeated tick after tick for the exact same still-unwanted stage.</summary>
    [Fact]
    public void ResetIsIssuedOnceForTheSameUnwantedStageEvenAcrossSeveralTicks()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, 4001u, "Yellow Wand");
        view.PartnerItemIds.Add(1u);

        session.Tick(1d, NoChat);
        session.Tick(1d, NoChat);
        session.Tick(1d, NoChat);

        Assert.Equal(1, view.ResetCalls);
        Assert.Single(session.DrainReplies());
    }

    [Fact]
    public void AnUnresolvedItemThatResolvesWantedWithinTheWaitIsAcceptedOnceThePartnerAccepts()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        view.PartnerItemIds.Add(1u); // not yet resolvable
        view.PartnerAccepted = true;

        session.Tick(1d, NoChat); // stage appears, unresolved, age 0s -> waits
        Assert.Equal(0, view.ResetCalls);
        Assert.Equal(0, view.AcceptCalls);

        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab"); // resolves at the 1s mark
        session.Tick(1d, NoChat);

        Assert.Equal(0, view.ResetCalls);
        Assert.Equal(1, view.AcceptCalls);
    }

    [Fact]
    public void AnItemStillUnresolvedAtTwoSecondsIsTreatedUnwanted()
    {
        (FakeTradeView view, FakeResolver _, TradeDonationSession session) = NewSession();
        view.PartnerItemIds.Add(1u); // never resolves

        session.Tick(1d, NoChat); // age 0s -> waits
        session.Tick(1d, NoChat); // age 1s -> waits
        Assert.Equal(0, view.ResetCalls);

        session.Tick(1d, NoChat); // age 2s -> reset

        Assert.Equal(1, view.ResetCalls);
        Assert.Contains("I can't use", Assert.Single(session.DrainReplies()));
    }

    [Fact]
    public void ASplittingToolIsWantedWhileTheBotHoldsNoneAndItsOwnWordIsLeftOutOfTheReplyWhenOneIsHeld()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession(botHoldsSplittingTool: false);
        resolver.Set(1u, SplittingToolWeenieId, "Splitting Tool");
        view.PartnerItemIds.Add(1u);
        view.PartnerAccepted = true;

        session.Tick(1d, NoChat);

        Assert.Equal(1, view.AcceptCalls);
        Assert.Equal(0, view.ResetCalls);
    }

    [Fact]
    public void ASplittingToolIsUnwantedWhileTheBotAlreadyHoldsOneAndTheReplyDropsItFromTheWantedList()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession(botHoldsSplittingTool: true);
        resolver.Set(1u, SplittingToolWeenieId, "Splitting Tool");
        view.PartnerItemIds.Add(1u);

        session.Tick(1d, NoChat);

        Assert.Equal(1, view.ResetCalls);
        string reply = Assert.Single(session.DrainReplies());
        Assert.Equal(
            "I can't use Splitting Tool. Please add only casting reagents or trade notes of 10,000+.",
            reply);
    }

    [Fact]
    public void CompletionThanksTheDonorAndRecordsOneDonationWithEveryAcceptedItem()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab", stack: 3);
        view.PartnerItemIds.Add(1u);
        view.PartnerAccepted = true;

        session.Tick(1d, NoChat);
        Assert.Equal(1, view.AcceptCalls);

        session.Tick(1d, [Chat("Trade Complete!")]);

        Assert.Equal(DefaultReplies.DonationThanks, Assert.Single(session.DrainReplies()));
        CompletedDonation donation = Assert.Single(session.DrainCompletedDonations());
        Assert.Equal("Archer", donation.DonorName);
        Assert.Equal(PartnerId, donation.DonorObjectId);
        DonatedItem item = Assert.Single(donation.Items);
        Assert.Equal("Lead Scarab", item.Name);
        Assert.Equal(LeadScarabWeenieId, item.WeenieClassId);
        Assert.Equal(3, item.Count);
    }

    [Fact]
    public void ClosingOnAnAcceptanceRecordsTheDonationWithoutTheServerText()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab");
        view.PartnerItemIds.Add(1u);
        view.PartnerAccepted = true;

        session.Tick(1d, NoChat);
        Assert.Equal(1, view.AcceptCalls);

        view.IsOpen = false;
        session.Tick(1d, NoChat);

        CompletedDonation donation = Assert.Single(session.DrainCompletedDonations());
        Assert.Equal("Lead Scarab", Assert.Single(donation.Items).Name);
        Assert.Equal(DefaultReplies.DonationThanks, Assert.Single(session.DrainReplies()));
    }

    /// <summary>The server empties the window when the trade goes through, one tick before it
    /// closes; that must record the donation, not look like a re-stage.</summary>
    [Fact]
    public void TheWindowEmptyingOnAnAcceptanceRecordsTheDonation()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab");
        view.PartnerItemIds.Add(1u);
        view.PartnerAccepted = true;

        session.Tick(1d, NoChat);
        Assert.Equal(1, view.AcceptCalls);

        view.PartnerItemIds.Clear();
        session.Tick(1d, NoChat);

        CompletedDonation donation = Assert.Single(session.DrainCompletedDonations());
        Assert.Equal("Lead Scarab", Assert.Single(donation.Items).Name);
        Assert.Equal(DefaultReplies.DonationThanks, Assert.Single(session.DrainReplies()));
    }

    /// <summary>A reset empties the window too, but clears both acceptances first.</summary>
    [Fact]
    public void AResetEmptyingTheWindowRecordsNothing()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab");
        view.PartnerItemIds.Add(1u);

        session.Tick(1d, NoChat);

        view.PartnerItemIds.Clear();
        view.PartnerAccepted = false;
        session.Tick(1d, NoChat);
        session.DrainReplies();

        view.IsOpen = false;
        session.Tick(1d, NoChat);

        Assert.Empty(session.DrainCompletedDonations());
    }

    [Fact]
    public void ClosingWithNothingAcceptedRecordsNoDonation()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab");
        view.PartnerItemIds.Add(1u);

        session.Tick(1d, NoChat);
        Assert.Equal(0, view.AcceptCalls);

        view.IsOpen = false;
        session.Tick(1d, NoChat);

        Assert.Empty(session.DrainCompletedDonations());
        Assert.Empty(session.DrainReplies());
    }

    /// <summary>Adding any item clears both acceptances, so a late add restarts classification.
    /// </summary>
    [Fact]
    public void APartnerAddAfterBothSidesAcceptedRestartsClassificationAndRecordsEveryItemOnCompletion()
    {
        (FakeTradeView view, FakeResolver resolver, TradeDonationSession session) = NewSession();
        resolver.Set(1u, LeadScarabWeenieId, "Lead Scarab", stack: 1);
        view.PartnerItemIds.Add(1u);
        view.PartnerAccepted = true;

        session.Tick(1d, NoChat);
        Assert.Equal(1, view.AcceptCalls);

        // ACE has already cleared both acceptances the instant the item was added.
        resolver.Set(2u, LeadScarabWeenieId, "Lead Scarab", stack: 2);
        view.PartnerItemIds.Add(2u);
        view.PartnerAccepted = false;
        view.MyAccepted = false;

        session.Tick(1d, NoChat); // reclassifies; not accepted again yet, the partner has not re-accepted
        Assert.Equal(1, view.AcceptCalls);

        view.PartnerAccepted = true;
        session.Tick(1d, NoChat);
        Assert.Equal(2, view.AcceptCalls);

        session.Tick(1d, [Chat("Trade Complete!")]);

        CompletedDonation donation = Assert.Single(session.DrainCompletedDonations());
        Assert.Equal(2, donation.Items.Count);
    }

    [Fact]
    public void NinetySecondsWithNothingNewStagedAndNoAcceptEndsTheTradeWithATell()
    {
        (FakeTradeView view, FakeResolver _, TradeDonationSession session) = NewSession();

        for (int i = 0; i < 89; i++)
            session.Tick(1d, NoChat);
        Assert.Equal(0, view.EndCalls);

        session.Tick(1d, NoChat);

        Assert.Equal(1, view.EndCalls);
        Assert.Equal(DefaultReplies.DonationStallClosing, Assert.Single(session.DrainReplies()));
    }

    [Fact]
    public void ThreeSpaceRefusalsEndTheTradeWithATell()
    {
        (FakeTradeView view, FakeResolver _, TradeDonationSession session) = NewSession();
        var refusal = Chat("You do not have enough free slots to complete the trade!");

        session.Tick(1d, [refusal]);
        session.Tick(1d, [refusal]);
        Assert.Equal(0, view.EndCalls);

        session.Tick(1d, [refusal]);

        Assert.Equal(1, view.EndCalls);
        Assert.Equal(DefaultReplies.DonationNoSpace, Assert.Single(session.DrainReplies()));
    }
}
