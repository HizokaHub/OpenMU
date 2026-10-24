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
}
