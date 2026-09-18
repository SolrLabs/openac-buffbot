namespace SolrLabs.BuffBot.Components;

/// <summary>One reagent flagged low: a scarab colour or the Prismatic Taper — peas and the Splitting Tool are named directly on <see cref="ContributionSummary"/> instead.</summary>
internal readonly record struct ContributionNeed(string Name, int Stock, string? PeaAlternative);

/// <summary>What `contribute` answers with. <see cref="InventoryReadable"/> false means the item surface is down; <see cref="ReagentCatalogAvailable"/> false (headless host) means <see cref="LowReagents"/> is unknowable, but <see cref="NeedsSplittingTool"/> still isn't.</summary>
internal readonly record struct ContributionSummary(
    bool InventoryReadable,
    bool ReagentCatalogAvailable,
    IReadOnlyList<ContributionNeed> LowReagents,
    bool NeedsSplittingTool)
{
    internal static readonly ContributionSummary Unavailable =
        new(false, false, Array.Empty<ContributionNeed>(), false);
}
