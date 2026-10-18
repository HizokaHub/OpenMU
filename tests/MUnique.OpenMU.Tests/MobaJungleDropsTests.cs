// <copyright file="MobaJungleDropsTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the drop levels and the EXP of the jungle.
/// </summary>
[TestFixture]
public class MobaJungleDropsTests
{
    /// <summary>
    /// Every kind drops in the levels the design asks for.
    /// </summary>
    [Test]
    public void KindsDropTheRequestedLevels()
    {
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.Mutant), Is.EqualTo((6, 9)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.BloodyDeathRider), Is.EqualTo((6, 9)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.SpiderEgg2), Is.EqualTo((6, 7)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.Spider), Is.EqualTo((7, 9)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.Medusa), Is.EqualTo((9, 10)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.GodOfDarkness), Is.EqualTo((9, 10)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.DarkPhoenix), Is.EqualTo((9, 10)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.Kundun), Is.EqualTo((11, 12)));
        Assert.That(MobaJungleDrops.LevelsOf(MobaJungle.Kind.Selupan), Is.EqualTo((11, 12)));
    }

    /// <summary>
    /// The creep and camp level roll stays between +6 and +9.
    /// </summary>
    [Test]
    public void CreepLevelRollIsSixToNine()
    {
        for (var i = 0; i < 500; i++)
        {
            Assert.That(MobaCreepDrops.RollCreepLevel(), Is.InRange(6, 9));
        }
    }

    /// <summary>
    /// The more damage a champion did to a boss, the better its item: the top damager gets the higher level and all the excellent options.
    /// </summary>
    [Test]
    public void MoreDamageMeansABetterItem()
    {
        var top = MobaJungleDrops.QualityOf(MobaJungle.Kind.Kundun, 1000, 1000, 6);
        var half = MobaJungleDrops.QualityOf(MobaJungle.Kind.Kundun, 500, 1000, 6);
        var low = MobaJungleDrops.QualityOf(MobaJungle.Kind.Kundun, 100, 1000, 6);
        Assert.That(top, Is.EqualTo((12, 6)));
        Assert.That(half.Level, Is.EqualTo(12));
        Assert.That(low.Level, Is.EqualTo(11));
        Assert.That(top.Excellent, Is.GreaterThan(half.Excellent));
        Assert.That(half.Excellent, Is.GreaterThan(low.Excellent));
        Assert.That(low.Excellent, Is.GreaterThanOrEqualTo(1));
        Assert.That(MobaJungleDrops.QualityOf(MobaJungle.Kind.Medusa, 100, 1000, 3).Level, Is.EqualTo(9));
        Assert.That(MobaJungleDrops.QualityOf(MobaJungle.Kind.Medusa, 900, 1000, 3).Level, Is.EqualTo(10));
    }

    /// <summary>
    /// Only the bosses split their drops by damage; eggs, spiders and camps roll their own.
    /// </summary>
    [Test]
    public void OnlyBossesSplitByDamage()
    {
        Assert.That(MobaJungleDrops.SplitsByDamage(MobaJungle.Kind.Kundun), Is.True);
        Assert.That(MobaJungleDrops.SplitsByDamage(MobaJungle.Kind.Selupan), Is.True);
        Assert.That(MobaJungleDrops.SplitsByDamage(MobaJungle.Kind.Medusa), Is.True);
        Assert.That(MobaJungleDrops.SplitsByDamage(MobaJungle.Kind.Spider), Is.False);
        Assert.That(MobaJungleDrops.SplitsByDamage(MobaJungle.Kind.SpiderEgg1), Is.False);
        Assert.That(MobaJungleDrops.SplitsByDamage(MobaJungle.Kind.Mutant), Is.False);
    }

    /// <summary>
    /// The EXP of the camps grows with the champion's level the way the EXP of a level does, and is boosted over the table.
    /// </summary>
    [Test]
    public void CampExpGrowsWithTheLevel()
    {
        var level1 = MobaJungle.ScaledExp(100, 1);
        var level20 = MobaJungle.ScaledExp(100, 20);
        Assert.That(level1, Is.EqualTo((long)Math.Round(100 * MobaJungle.ExpBoost)));
        Assert.That(level20, Is.GreaterThan(level1));
        Assert.That(MobaJungle.ScaledExp(100, 30), Is.GreaterThan(0));
    }

    /// <summary>
    /// The allied creeps see half as far as a champion.
    /// </summary>
    [Test]
    public void CreepsSeeSevenTiles()
    {
        Assert.That(MobaVision.CreepRadius, Is.EqualTo(7));
        Assert.That(MobaVision.CreepRadius * 2, Is.EqualTo(MobaVision.UnitRadius));
    }
}
