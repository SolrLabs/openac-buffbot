namespace SolrLabs.BuffBot.Components;

/// <summary>One reagent the bot's resolved spells need. <see cref="Stock"/> sums every owned
/// stack sharing this weenie class id, 0 if held nowhere.</summary>
internal readonly record struct ComponentUsage(
    uint WeenieClassId,
    string Name,
    int Stock,
    int UsedBy);

/// <summary>Sampled on its own cadence; never rebuilt inside a tick.</summary>
internal readonly record struct ComponentReport(
    bool Available, IReadOnlyList<ComponentUsage> Items, bool CatalogAvailable = true)
{
    internal static readonly ComponentReport Unavailable = // the item surface can't answer right now
        new(false, Array.Empty<ComponentUsage>());

    internal static readonly ComponentReport Empty = // both surfaces answered; nothing is needed
        new(true, Array.Empty<ComponentUsage>());

    internal static readonly ComponentReport CatalogUnavailable = // surface answered, catalog resolved nothing
        new(true, Array.Empty<ComponentUsage>(), CatalogAvailable: false);
}
