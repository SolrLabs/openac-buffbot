using AcDream.Plugin.Abstractions;
using SolrLabs.BuffBot.Components;
using Xunit;

namespace SolrLabs.BuffBot.Tests.Components;

/// <summary><see cref="PluginSpellInfo.School"/> is the magic skill id (31 Creature, 32 Item,
/// 33 Life, 34 War, 43 Void), not a 1-5 index — the lookup must be keyed by skill id.</summary>
public class CasterFormulaModeTests
{
    private static PluginInventoryItem Item(uint weenieClassId) =>
        new(0x80000001u, weenieClassId, "item", ItemType: 0, ContainerObjectId: 0x50000004u,
            WielderObjectId: 0u, ValidLocations: 0, EquippedLocation: 0u, Useability: 0, TargetType: 0,
            PublicFlags: 0, StackSize: 1, Structure: 0, MaximumStructure: 0, SpellId: 0, PetClass: 0,
            SummoningMastery: 0, ProcSpellId: 0, ProcSpellSelfTargeted: false, ProcSpellRate: 0,
            WeaponSkill: 0, DamageType: 0, Damage: 0, DamageVariance: 0, UseRequiresSkill: 0,
            UseRequiresSkillLevel: 0, UseRequiresSkillSpecialized: 0);

    [Theory]
    [InlineData(31u, 15268u)] // Creature Enchantment, Foci of Enchantment
    [InlineData(32u, 15269u)] // Item Enchantment, Foci of Artifice
    [InlineData(33u, 15270u)] // Life Magic, Foci of Verdancy
    [InlineData(34u, 15271u)] // War Magic, Foci of Strife
    [InlineData(43u, 43173u)] // Void Magic, Foci of Shadow
    public void AHeldFocusSelectsTheScarabOnlyFormulaForItsSkill(uint skillId, uint focusWeenieClassId)
    {
        Assert.True(CasterFormulaMode.UsesScarabOnlyFormula(skillId, [Item(focusWeenieClassId)], null));
    }

    [Fact]
    public void AFocusForAnotherSkillDoesNotCount()
    {
        Assert.False(CasterFormulaMode.UsesScarabOnlyFormula(33u, [Item(15268u)], null));
    }

    [Fact]
    public void TheSchoolNumberIsNotASkillId()
    {
        Assert.False(CasterFormulaMode.UsesScarabOnlyFormula(4u, [Item(15268u)], null));
    }
}
