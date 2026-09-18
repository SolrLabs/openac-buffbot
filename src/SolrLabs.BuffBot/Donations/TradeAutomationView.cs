using AcDream.Plugin.Abstractions;

namespace SolrLabs.BuffBot.Donations;

/// <summary>The production adapter from the real <see cref="ITradeAutomation"/> surface to <see cref="ITradeView"/>. Every member is a straight pass-through.</summary>
internal sealed class TradeAutomationView : ITradeView
{
    private readonly ITradeAutomation _trade;

    internal TradeAutomationView(ITradeAutomation trade) => _trade = trade;

    public bool IsOpen => _trade.IsOpen;
    public uint PartnerObjectId => _trade.PartnerObjectId;
    public IReadOnlyList<uint> MyItems => _trade.MyItems;
    public IReadOnlyList<uint> PartnerItems => _trade.PartnerItems;
    public bool MyAccepted => _trade.MyAccepted;
    public bool PartnerAccepted => _trade.PartnerAccepted;
    public PluginTradeCommandResult Accept() => _trade.Accept();
    public PluginTradeCommandResult Reset() => _trade.Reset();
    public PluginTradeCommandResult End() => _trade.End();
}
