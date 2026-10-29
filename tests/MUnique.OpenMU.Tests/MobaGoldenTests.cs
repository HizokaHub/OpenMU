// <copyright file="MobaGoldenTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using NUnit.Framework;

/// <summary>
/// Tests of the golden invasion numbers (see <see cref="MobaGolden"/>).
/// </summary>
[TestFixture]
public class MobaGoldenTests
{
    /// <summary>
    /// The goblins of the invasion follow the size of the match, and with the 30 % of the perfection jewel clearing most of them gives every champion 3 or more.
    /// </summary>
    /// <param name="champions">The champions of the match.</param>
    [TestCase(6)]
    [TestCase(8)]
    [TestCase(10)]
    public void TheGoblinsAreEnoughForEveryChampion(int champions)
    {
        var goblins = MobaGolden.GoblinBudgetFor(champions);
        var jewelsPerChampion = goblins * MobaGoldenLoot.PerfectionChance * 0.7 / champions;
        Assert.That(jewelsPerChampion, Is.GreaterThanOrEqualTo(3.0), "jewels per champion when 70 % of the goblins are killed");
        Assert.That(MobaGolden.GoblinBudgetFor(8), Is.GreaterThan(MobaGolden.GoblinBudgetFor(6)));
    }

    /// <summary>The small monsters of the invasion (the goblins) drop the perfection jewel.</summary>
    [Test]
    public void TheGoblinsDropThePerfectionJewel()
    {
        Assert.That(MobaGoldenLoot.ChancesOf(MobaGolden.Tier.Small), Does.Contain((MobaGoldenLoot.Kind.Perfection, MobaGoldenLoot.PerfectionChance)));
    }

    /// <summary>
    /// The Great Golden Dragon kills a champion in four hits.
    /// </summary>
    [Test]
    public void TheGreatDragonKillsInFourHits()
    {
        Assert.That(1.0 / MobaGolden.HitShareOf(MobaGolden.Tier.Great), Is.EqualTo(4.0).Within(0.01));
    }

    /// <summary>
    /// The higher the tier, the harder it hits, the more it takes to kill it and the more it pays.
    /// </summary>
    [Test]
    public void TheTiersGrow()
    {
        var tiers = new[] { MobaGolden.Tier.Small, MobaGolden.Tier.Medium, MobaGolden.Tier.Large, MobaGolden.Tier.Great };
        for (var i = 1; i < tiers.Length; i++)
        {
            Assert.That(MobaGolden.HitShareOf(tiers[i]), Is.GreaterThan(MobaGolden.HitShareOf(tiers[i - 1])));
            Assert.That(MobaGolden.StructureShareOf(tiers[i]), Is.GreaterThan(MobaGolden.StructureShareOf(tiers[i - 1])));
            Assert.That(MobaGolden.BaseHealthOf(tiers[i]), Is.GreaterThan(MobaGolden.BaseHealthOf(tiers[i - 1])));
            Assert.That(MobaGolden.RewardOf(tiers[i]).Gold, Is.GreaterThan(MobaGolden.RewardOf(tiers[i - 1]).Gold));
            Assert.That(MobaGolden.RewardOf(tiers[i]).SummonPoints, Is.GreaterThan(MobaGolden.RewardOf(tiers[i - 1]).SummonPoints));
        }
    }

    /// <summary>
    /// The dragon has more life than twice the Kundun of the jungle, so that five champions are needed to bring it down.
    /// </summary>
    [Test]
    public void TheGreatDragonIsTougherThanKundun()
    {
        Assert.That(MobaGolden.BaseHealthOf(MobaGolden.Tier.Great), Is.GreaterThan(MobaJungle.Info[MobaJungle.Kind.Kundun].BaseHealth * 2));
    }

    /// <summary>
    /// The invasion begins at minute 22 and nothing is reinforced before it.
    /// </summary>
    [Test]
    public void TheInvasionBeginsAtMinuteTwentyTwo()
    {
        Assert.That(MobaGolden.StartAfter, Is.EqualTo(TimeSpan.FromMinutes(22)));
        Assert.That(MobaGolden.FlyoverSeconds, Is.EqualTo(60));
    }

    /// <summary>
    /// Unrelated objects are no golden monsters, and a team-less object is nobody's enemy.
    /// </summary>
    [Test]
    public void NothingIsGoldenByDefault()
    {
        Assert.That(MobaGolden.IsGolden(null), Is.False);
        Assert.That(MobaGolden.IsGolden(new object()), Is.False);
        Assert.That(MobaTeams.AreEnemies(new object(), new object()), Is.False);
    }

    /// <summary>
    /// Two golden goblins bring a whole turret down in well under a minute, and a turret needs many hits to kill one goblin.
    /// </summary>
    [Test]
    public void TwoGoblinsBringATurretDown()
    {
        var hitsToKillTurret = 1.0 / MobaGolden.StructureShareOf(MobaGolden.Tier.Small);
        var seconds = hitsToKillTurret / 2 * MobaGolden.AttackSecondsOf(MobaGolden.Tier.Small);
        Assert.That(seconds, Is.LessThan(40));
        Assert.That(1.0 / MobaGolden.TurretShareOf(MobaGolden.Tier.Small), Is.GreaterThan(30));
    }

    /// <summary>
    /// Every kind of golden loot is a different item, and the Great Golden Dragon always drops a chest.
    /// </summary>
    [Test]
    public void TheLootIsDistinctAndTheDragonAlwaysDropsAChest()
    {
        var items = Enum.GetValues<MobaGoldenLoot.Kind>().Select(k => (MobaGoldenLoot.ItemOf(k).Group, MobaGoldenLoot.ItemOf(k).Number)).ToList();
        Assert.That(items.Distinct().Count(), Is.EqualTo(items.Count));
        Assert.That(MobaGoldenLoot.ChancesOf(MobaGolden.Tier.Great).Any(c => c.Kind == MobaGoldenLoot.Kind.Chest && c.Chance >= 1.0), Is.True);
        Assert.That(MobaGoldenLoot.KindOf(null), Is.Null);
    }

    /// <summary>
    /// Gold bonuses only exist in the first two tiers of the items.
    /// </summary>
    [Test]
    public void ItemsGiveNoGoldAtTierThree()
    {
        var traits = MobaItemTraits.TooltipTraits.Where(t => t.Kind is 4 or 5).ToList();
        Assert.That(traits, Is.Not.Empty);
        Assert.That(traits.All(t => t.Percent is 5 or 10), Is.True, "only the 5 % and 10 % gold traits of tiers 1 and 2 are left");
    }
}
