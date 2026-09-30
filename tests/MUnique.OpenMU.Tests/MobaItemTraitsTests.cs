// <copyright file="MobaItemTraitsTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>Tests of the MOBA-only item options (anti-heal, CC resistance, cooldown reduction, gold).</summary>
[TestFixture]
public class MobaItemTraitsTests
{
    /// <summary>The weapon anti-heal grows 10 / 20 / 30 % with the tier.</summary>
    [Test]
    public void WeaponAntiHealGrowsWithTier()
    {
        var byType = MobaItemTraits.TooltipTraits.ToDictionary(t => t.ItemType);
        Assert.Multiple(() =>
        {
            Assert.That(byType[(5 * 512) + 5], Is.EqualTo(new GameLogic.Views.Moba.MobaItemTrait((5 * 512) + 5, 1, 10)));   // Legendary Staff, T1
            Assert.That(byType[(5 * 512) + 9].Percent, Is.EqualTo(20));   // Dragon Soul Staff, T2
            Assert.That(byType[(5 * 512) + 12].Percent, Is.EqualTo(30));  // Grand Viper Staff, T3
        });
    }

    /// <summary>Armor / pants give CC resistance, gloves / boots cooldown reduction, rings assist gold, pendants passive gold.</summary>
    [Test]
    public void EachPieceKindHasItsOwnTrait()
    {
        var byType = MobaItemTraits.TooltipTraits.ToDictionary(t => t.ItemType);
        Assert.Multiple(() =>
        {
            Assert.That(byType[(8 * 512) + 3].Kind, Is.EqualTo(2), "armor of the Legendary set");
            Assert.That(byType[(9 * 512) + 3].Kind, Is.EqualTo(2), "pants");
            Assert.That(byType[(10 * 512) + 3].Kind, Is.EqualTo(3), "gloves");
            Assert.That(byType[(11 * 512) + 3].Kind, Is.EqualTo(3), "boots");
            Assert.That(byType.ContainsKey((7 * 512) + 3), Is.False, "the helm has none");
            Assert.That(byType[(13 * 512) + 8].Kind, Is.EqualTo(4), "Ring of Ice");
            Assert.That(byType[(13 * 512) + 12].Kind, Is.EqualTo(5), "Pendant of Lightning");
        });
    }

    /// <summary>Healing is untouched for anything that isn't an anti-healed champion.</summary>
    [Test]
    public void HealingOfNonChampionsIsUnchanged()
        => Assert.That(MobaItemTraits.ScaleHealing(null, 100), Is.EqualTo(100));
}
