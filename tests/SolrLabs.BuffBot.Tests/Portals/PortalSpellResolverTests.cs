using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Portals;

namespace SolrLabs.BuffBot.Tests.Portals;

/// <summary>Host-free coverage of <see cref="PortalSpellResolver"/>.</summary>
public sealed class PortalSpellResolverTests
{
    private const uint SummonPortalIii = 1637u;
    private const uint SummonPortalIi = 158u;
    private const uint SummonPortalI = 157u;
    private const uint UnrelatedSpellId = 2709u;

    private sealed class FakeCatalog : ISpellCatalog
    {
        private readonly Dictionary<uint, PluginSpellInfo> known = [];

        internal FakeCatalog Knows(uint spellId, string name)
        {
            known[spellId] = new PluginSpellInfo(
                SpellId: spellId, Name: name, Family: 0, Tier: 0,
                Difficulty: 0, ManaCost: 0, DurationSeconds: 0f, School: 0, Description: string.Empty,
                IsSelfTargeted: false, IsBeneficial: false);
            return this;
        }

        public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; } = [];

        public bool IsKnown(uint spellId) => known.ContainsKey(spellId);

        public bool TryGet(uint spellId, out PluginSpellInfo info) => known.TryGetValue(spellId, out info);
    }

    [Fact]
    public void OnlyTierIAndIiiKnownReturnsThemHighestLevelFirst()
    {
        var catalog = new FakeCatalog().Knows(SummonPortalIii, "Portal III").Knows(SummonPortalI, "Portal I");

        IReadOnlyList<PluginSpellInfo> resolved = PortalSpellResolver.Resolve(catalog, PortalTieSlot.Primary);

        Assert.Equal([SummonPortalIii, SummonPortalI], resolved.Select(s => s.SpellId));
    }

    [Fact]
    public void NothingKnownResolvesToAnEmptyList()
    {
        var catalog = new FakeCatalog();

        Assert.Empty(PortalSpellResolver.Resolve(catalog, PortalTieSlot.Primary));
        Assert.Empty(PortalSpellResolver.Resolve(catalog, PortalTieSlot.Secondary));
    }

    [Fact]
    public void AKnownSpellOutsideBothTiersIsNeverReturned()
    {
        var catalog = new FakeCatalog()
            .Knows(SummonPortalIii, "Portal III").Knows(SummonPortalIi, "Portal II").Knows(SummonPortalI, "Portal I")
            .Knows(UnrelatedSpellId, "Unrelated");

        IReadOnlyList<PluginSpellInfo> primary = PortalSpellResolver.Resolve(catalog, PortalTieSlot.Primary);
        IReadOnlyList<PluginSpellInfo> secondary = PortalSpellResolver.Resolve(catalog, PortalTieSlot.Secondary);

        Assert.DoesNotContain(primary, s => s.SpellId == UnrelatedSpellId);
        Assert.DoesNotContain(secondary, s => s.SpellId == UnrelatedSpellId);
    }
}
