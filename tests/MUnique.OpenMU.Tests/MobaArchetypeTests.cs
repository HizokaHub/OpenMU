// <copyright file="MobaArchetypeTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the bonuses of the set pieces by class archetype and by set profile.
/// </summary>
[TestFixture]
public class MobaArchetypeTests
{
    private static double Sum(MobaFamily family, MobaPieceProfile profile, int tier, MobaArchetypeTrait trait)
    {
        var total = 0.0;
        for (byte key = MobaArchetype.FirstPieceGroup; key <= MobaArchetype.LastPieceGroup; key++)
        {
            total += MobaArchetype.PieceBonusesOf(family, key, tier, profile).Where(b => b.Trait == trait).Sum(b => b.Value);
        }

        return total;
    }

    /// <summary>
    /// A full third tier recovery set of a Blade Knight is about life, regeneration and durability; the one of a Soul Master about mana.
    /// </summary>
    [Test]
    public void RecoverySetsFollowTheStyleOfTheClass()
    {
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Recovery, 3, MobaArchetypeTrait.HealthPool), Is.EqualTo(0.12).Within(1e-9));
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Recovery, 3, MobaArchetypeTrait.Durability), Is.EqualTo(0.10).Within(1e-9));
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Recovery, 3, MobaArchetypeTrait.HealthRegen), Is.EqualTo(0.014).Within(1e-9));
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Recovery, 3, MobaArchetypeTrait.ManaPool), Is.EqualTo(0));
        Assert.That(Sum(MobaFamily.Wizard, MobaPieceProfile.Recovery, 3, MobaArchetypeTrait.ManaPool), Is.EqualTo(0.20).Within(1e-9));
        Assert.That(Sum(MobaFamily.Wizard, MobaPieceProfile.Recovery, 3, MobaArchetypeTrait.ManaRegen), Is.EqualTo(0.24).Within(1e-9));
        Assert.That(Sum(MobaFamily.Elf, MobaPieceProfile.Recovery, 3, MobaArchetypeTrait.HealPower), Is.EqualTo(0.20).Within(1e-9));
    }

    /// <summary>
    /// The attack set gives skill power and critical hits to every class, so a bruiser may choose to hit instead of to endure.
    /// </summary>
    [Test]
    public void AttackSetsGiveSkillPowerAndCriticalHitsToEveryClass()
    {
        foreach (var family in Enum.GetValues<MobaFamily>())
        {
            Assert.That(Sum(family, MobaPieceProfile.Attack, 3, MobaArchetypeTrait.SpellPower), Is.EqualTo(0.10).Within(1e-9), $"{family}");
            Assert.That(Sum(family, MobaPieceProfile.Attack, 3, MobaArchetypeTrait.CritChance), Is.EqualTo(0.06).Within(1e-9), $"{family}");
            Assert.That(Sum(family, MobaPieceProfile.Attack, 3, MobaArchetypeTrait.Durability), Is.EqualTo(0), $"{family}");
        }
    }

    /// <summary>
    /// A balanced set gives half of each and the bonus grows by a third per tier.
    /// </summary>
    [Test]
    public void BalancedSetsGiveHalfOfEachAndGrowWithTheTier()
    {
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Balanced, 3, MobaArchetypeTrait.Durability), Is.EqualTo(0.05).Within(1e-9));
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Balanced, 3, MobaArchetypeTrait.SpellPower), Is.EqualTo(0.05).Within(1e-9));
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Recovery, 1, MobaArchetypeTrait.HealthPool), Is.EqualTo(0.04).Within(1e-9));
        Assert.That(Sum(MobaFamily.Knight, MobaPieceProfile.Recovery, 0, MobaArchetypeTrait.HealthPool), Is.EqualTo(0));
    }

    /// <summary>
    /// Where a class is offered two sets of a tier the first one recovers and the second one attacks; a class with one set gets the balanced one.
    /// </summary>
    [Test]
    public void TwoSetsOfATierAreRecoveryAndAttack()
    {
        // Wizard tier 1: Legendary (3) and Eclipse (35). Magic Gladiator has a single set per tier. Knight tier 2: Dark Phoenix (17) and Black Dragon (16).
        Assert.That(MobaArchetype.SetProfileOf(MobaFamily.Wizard, 3), Is.EqualTo(MobaPieceProfile.Recovery));
        Assert.That(MobaArchetype.SetProfileOf(MobaFamily.Wizard, 35), Is.EqualTo(MobaPieceProfile.Attack));
        Assert.That(MobaArchetype.SetProfileOf(MobaFamily.Knight, 17), Is.EqualTo(MobaPieceProfile.Recovery));
        Assert.That(MobaArchetype.SetProfileOf(MobaFamily.Knight, 16), Is.EqualTo(MobaPieceProfile.Attack));
        Assert.That(MobaArchetype.SetProfileOf(MobaFamily.MagicGladiator, 15), Is.EqualTo(MobaPieceProfile.Balanced));
        Assert.That(MobaArchetype.SetProfileOf(MobaFamily.Summoner, 43), Is.EqualTo(MobaPieceProfile.Balanced), "the third tier of the Summoner has one set");
    }

    /// <summary>
    /// The second weapon of the third tier is the attack weapon; the weapons of the first tiers are balanced.
    /// </summary>
    [Test]
    public void TheSecondWeaponOfTheThirdTierIsTheAttackOne()
    {
        Assert.That(MobaArchetype.WeaponProfileOf(MobaFamily.Elf, 4, 24), Is.EqualTo(MobaPieceProfile.Balanced));
        Assert.That(MobaArchetype.WeaponProfileOf(MobaFamily.Elf, 4, 21), Is.EqualTo(MobaPieceProfile.Attack));
        Assert.That(MobaArchetype.WeaponProfileOf(MobaFamily.Elf, 4, 6), Is.EqualTo(MobaPieceProfile.Balanced));
    }

    /// <summary>
    /// Every set piece sold to a class with archetype bonuses carries a tooltip line for it.
    /// </summary>
    [Test]
    public void ShopPiecesCarryTheirArchetypeTraitsInTheTooltip()
    {
        var traits = MobaItemTraits.TooltipTraits;
        Assert.That(traits.Any(t => t.Kind == (byte)MobaArchetypeTrait.Durability), Is.True);
        Assert.That(traits.Any(t => t.Kind == (byte)MobaArchetypeTrait.ManaPool), Is.True);
        Assert.That(traits.Any(t => t.Kind == (byte)MobaArchetypeTrait.HealPower), Is.True);
        Assert.That(traits.Count(t => t.Kind == (byte)MobaArchetypeTrait.CritChance), Is.GreaterThan(0));
    }
}
