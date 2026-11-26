// <copyright file="MobaDualWield.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel;

/// <summary>
/// What the Blade Knight and the Magic Gladiator get for fighting with two weapons instead of weapon and shield (2026-11-25): more damage against
/// champions, by the tier of the weaker of the two weapons. Without it the shield was strictly better: a second weapon counts in the item term like a
/// shield of the same tier and adds no trait, while the shield gives crowd-control resistance, cooldown reduction and the Aegis barrier.
/// The values start at 0 and are tuned with mirror duels (two weapons against shield) until each side wins 45-55 %.
/// The Rage Fighter (two gloves is its only way to fight) is balanced by its class values and gets nothing here.
/// </summary>
public static class MobaDualWield
{
    private const int Tiers = 3;

    private static readonly double[] KnightBonus = new double[Tiers + 1];
    private static readonly double[] GladiatorBonus = new double[Tiers + 1];

    /// <summary>The extra damage of a family with two weapons whose weaker one is of the tier.</summary>
    /// <param name="family">The family.</param>
    /// <param name="tier">The weapon tier, 1..3.</param>
    /// <returns>The bonus as a fraction (0 for any other family or tier).</returns>
    public static double BonusOf(MobaFamily family, int tier)
        => TableOf(family) is { } table && tier is >= 1 and <= Tiers ? table[tier] : 0;

    /// <summary>Sets the bonus of a family.</summary>
    /// <param name="family">The family (Knight or Magic Gladiator).</param>
    /// <param name="value">The bonus as a fraction.</param>
    /// <param name="tier">The weapon tier 1..3, or 0 for all of them.</param>
    /// <returns><see langword="false"/> if the family has no two-weapon option.</returns>
    public static bool SetBonus(MobaFamily family, double value, int tier = 0)
    {
        if (TableOf(family) is not { } table)
        {
            return false;
        }

        for (var i = 1; i <= Tiers; i++)
        {
            if (tier <= 0 || tier == i)
            {
                table[i] = value;
            }
        }

        return true;
    }

    /// <summary>The damage factor of an attacker: above 1 only for a Blade Knight or Magic Gladiator with a shop weapon in each hand.</summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <returns>1 + the bonus.</returns>
    public static double DamageFactorOf(Player attacker)
    {
        var family = MobaPassives.FamilyOf(attacker);
        if (TableOf(family) is null)
        {
            return 1.0;
        }

        var tier = Math.Min(
            MobaItemPower.TierIn(attacker, InventoryConstants.LeftHandSlot, MobaShopCategory.Weapons),
            MobaItemPower.TierIn(attacker, InventoryConstants.RightHandSlot, MobaShopCategory.Weapons));
        return 1.0 + BonusOf(family, tier);
    }

    private static double[]? TableOf(MobaFamily family) => family switch
    {
        MobaFamily.Knight => KnightBonus,
        MobaFamily.MagicGladiator => GladiatorBonus,
        _ => null,
    };
}
