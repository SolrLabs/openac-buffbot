using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Donations;

namespace SolrLabs.BuffBot.Tests.Donations;

internal sealed class FakeTradeView : ITradeView
{
    public bool IsOpen { get; set; }
    public uint PartnerObjectId { get; set; }
    public List<uint> MyItemIds { get; } = [];
    public List<uint> PartnerItemIds { get; } = [];
    public bool MyAccepted { get; set; }
    public bool PartnerAccepted { get; set; }

    public IReadOnlyList<uint> MyItems => MyItemIds;
    public IReadOnlyList<uint> PartnerItems => PartnerItemIds;

    public int AcceptCalls { get; private set; }
    public int ResetCalls { get; private set; }
    public int EndCalls { get; private set; }

    public PluginTradeCommandResult Accept()
    {
        AcceptCalls++;
        return new PluginTradeCommandResult(PluginTradeCommandStatus.Sent);
    }

    public PluginTradeCommandResult Reset()
    {
        ResetCalls++;
        return new PluginTradeCommandResult(PluginTradeCommandStatus.Sent);
    }

    public PluginTradeCommandResult End()
    {
        EndCalls++;
        return new PluginTradeCommandResult(PluginTradeCommandStatus.Sent);
    }
}

internal sealed class FakeResolver
{
    private readonly Dictionary<uint, (uint wcid, string name, int stack)> _known = [];

    internal FakeResolver Set(uint objectId, uint weenieClassId, string name, int stack = 1)
    {
        _known[objectId] = (weenieClassId, name, stack);
        return this;
    }

    internal void Forget(uint objectId) => _known.Remove(objectId);

    internal (uint wcid, string name, int stack)? Resolve(uint objectId) =>
        _known.TryGetValue(objectId, out var value) ? value : null;
}
