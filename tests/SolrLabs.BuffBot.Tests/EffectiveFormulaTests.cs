using SolrLabs.BuffBot.Spells;

namespace SolrLabs.BuffBot.Tests;

public sealed class EffectiveFormulaTests
{
    // Diamond's own formula component id (0x6E) — tier VII, power 7.
    private const uint DiamondScarabComponentId = 0x6Eu;

    // Mana's own formula component id (0xC1) — power 10, the strongest defined.
    private const uint ManaScarabComponentId = 0xC1u;

    private const uint PrismaticTaperComponentId = 0xBCu;

    [Fact]
    public void PowerOneYieldsOneTaper()
    {
        IReadOnlyList<uint> result = EffectiveFormula.ScarabOnlyFormula([1u]);

        Assert.Equal([1u, PrismaticTaperComponentId], result);
    }

    /// <summary>A tier-VII formula (Diamond, power 7) — the switch's own <c>3 or 4 or 7 =&gt; 3</c>
    /// case — keeps the scarab and adds exactly three Prismatic Tapers.</summary>
    [Fact]
    public void PowerSevenYieldsScarabPlusThreeTapers()
    {
        IReadOnlyList<uint> result = EffectiveFormula.ScarabOnlyFormula([DiamondScarabComponentId]);

        Assert.Equal(
            [DiamondScarabComponentId, PrismaticTaperComponentId, PrismaticTaperComponentId, PrismaticTaperComponentId],
            result);
    }

    /// <summary>The strongest power component wins, not the first.</summary>
    [Fact]
    public void TheStrongestPowerComponentPresentSetsTheTaperCount()
    {
        IReadOnlyList<uint> result = EffectiveFormula.ScarabOnlyFormula([1u, ManaScarabComponentId]);

        Assert.Equal(1u, result[0]);
        Assert.Equal(ManaScarabComponentId, result[1]);
        Assert.Equal(4, result.Count(id => id == PrismaticTaperComponentId));
    }

    /// <summary>A non-power component is dropped, and never counts toward the taper count.
    /// </summary>
    [Fact]
    public void NonPowerComponentsAreDropped()
    {
        IReadOnlyList<uint> result = EffectiveFormula.ScarabOnlyFormula([1u, 500u, 501u]);

        Assert.DoesNotContain(500u, result);
        Assert.DoesNotContain(501u, result);
        Assert.Equal([1u, PrismaticTaperComponentId], result);
    }

    /// <summary>Never more than 8 entries total, matching the client's own 8-slot formula — eight
    /// power components alone already fill every slot, so no taper is appended at all.</summary>
    [Fact]
    public void NeverExceedsEightEntriesTotal()
    {
        IReadOnlyList<uint> result = EffectiveFormula.ScarabOnlyFormula(
            [1u, 2u, 3u, 4u, 5u, 6u, DiamondScarabComponentId, ManaScarabComponentId]);

        Assert.Equal(8, result.Count);
        Assert.DoesNotContain(PrismaticTaperComponentId, result);
    }

    /// <summary>A 0 entry ends the formula early, exactly as it does for the raw one — nothing
    /// past it is examined.</summary>
    [Fact]
    public void AZeroEntryEndsTheFormulaEarly()
    {
        IReadOnlyList<uint> result = EffectiveFormula.ScarabOnlyFormula([1u, 0u, ManaScarabComponentId]);

        Assert.Equal([1u, PrismaticTaperComponentId], result);
    }
}
