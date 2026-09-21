using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests.Spells;

/// <summary>Host-free coverage of <see cref="BeneficialSpellCatalog"/>: the fix for OpenAC
/// 8b2c5147, which narrowed <see cref="ISpellCatalog.KnownSelfBuffs"/> to spells the caster can
/// target on itself and silently dropped every "... Other" line a buff profile needs.</summary>
public sealed class BeneficialSpellCatalogTests
{
    private const uint FocusSelfId = 1;
    private const uint StrengthOtherId = 2;

    private static readonly PluginSpellInfo FocusSelf = new(
        SpellId: FocusSelfId, Name: "Focus Self VI", Family: 10, Tier: 6,
        Difficulty: 0, ManaCost: 0, DurationSeconds: 1200f, School: 0, Description: string.Empty,
        IsSelfTargeted: true, IsBeneficial: true);

    private static readonly PluginSpellInfo StrengthOther = new(
        SpellId: StrengthOtherId, Name: "Strength Other I", Family: 20, Tier: 1,
        Difficulty: 0, ManaCost: 0, DurationSeconds: 120f, School: 0, Description: string.Empty,
        IsSelfTargeted: false, IsBeneficial: true);

    /// <summary>v0.1.11/v0.1.12: KnownSelfBuffs alone; TryGet is every constructor argument
    /// needs, so All and IsKnown are both overridden.</summary>
    private sealed class V0112StyleCatalog : ISpellCatalog
    {
        internal V0112StyleCatalog(params PluginSpellInfo[] knownSelfBuffs) => KnownSelfBuffs = knownSelfBuffs;

        public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; }

        public bool TryGet(uint spellId, out PluginSpellInfo info)
        {
            foreach (PluginSpellInfo spell in KnownSelfBuffs)
            {
                if (spell.SpellId != spellId)
                    continue;
                info = spell;
                return true;
            }
            info = default;
            return false;
        }
    }

    private sealed class Dev2StyleCatalog : ISpellCatalog
    {
        public IReadOnlyList<PluginSpellInfo> KnownSelfBuffs { get; } = [FocusSelf];

        public IReadOnlyList<PluginSpellInfo> All { get; } = [FocusSelf, StrengthOther];

        public bool IsKnown(uint spellId) => spellId is FocusSelfId or StrengthOtherId;

        public bool TryGet(uint spellId, out PluginSpellInfo info)
        {
            foreach (PluginSpellInfo spell in All)
            {
                if (spell.SpellId != spellId)
                    continue;
                info = spell;
                return true;
            }
            info = default;
            return false;
        }
    }

    [Fact]
    public void ADev2StyleCatalogUnionsTheOtherLineAllAloneReports()
    {
        var catalog = new Dev2StyleCatalog();

        IReadOnlyList<PluginSpellInfo> resolved = BeneficialSpellCatalog.Resolve(catalog);

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, spell => spell.SpellId == FocusSelfId);
        Assert.Contains(resolved, spell => spell.SpellId == StrengthOtherId);
    }

    /// <summary>Pre-8b2c5147: the "Other" lines already live in KnownSelfBuffs, so the union
    /// still resolves and never doubles an entry both sides agree on.</summary>
    [Fact]
    public void AV0112StyleCatalogWithOtherLinesAlreadyInKnownSelfBuffsResolvesWithNoDuplicates()
    {
        var catalog = new V0112StyleCatalog(FocusSelf, StrengthOther);

        IReadOnlyList<PluginSpellInfo> resolved = BeneficialSpellCatalog.Resolve(catalog);

        Assert.Equal(2, resolved.Count);
        Assert.Contains(resolved, spell => spell.SpellId == FocusSelfId);
        Assert.Contains(resolved, spell => spell.SpellId == StrengthOtherId);
    }

    /// <summary>A host that never overrides <see cref="ISpellCatalog.All"/> — its interface
    /// default is empty — leaves the result exactly as <c>KnownSelfBuffs</c> reported it.</summary>
    [Fact]
    public void AnEmptyAllLeavesKnownSelfBuffsUnchanged()
    {
        var catalog = new V0112StyleCatalog(FocusSelf, StrengthOther);

        IReadOnlyList<PluginSpellInfo> resolved = BeneficialSpellCatalog.Resolve(catalog);

        Assert.Same(catalog.KnownSelfBuffs, resolved);
    }

    /// <summary>Before the spellbook has arrived both lists are empty; the result must stay
    /// empty rather than getting stuck on some earlier non-empty cache.</summary>
    [Fact]
    public void NothingLearnedYetResolvesToAnEmptyList()
    {
        var catalog = new V0112StyleCatalog();

        Assert.Empty(BeneficialSpellCatalog.Resolve(catalog));
    }
}
