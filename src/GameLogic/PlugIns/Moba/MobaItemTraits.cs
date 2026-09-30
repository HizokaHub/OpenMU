// <copyright file="MobaItemTraits.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.DataModel;

/// <summary>
/// The MOBA-only options of shop pieces (they have no native MU attribute), read straight off the
/// equipped items like <see cref="MobaItemPower"/>: the tier of the piece decides the size.
/// <list type="bullet">
/// <item><description>Weapon: <b>anti-heal</b> - hits cut the healing the target receives (10 / 20 / 30 %) for a few seconds.</description></item>
/// <item><description>Armor and pants: <b>crowd-control resistance</b> (5 / 10 / 15 % each, so up to 30 %).</description></item>
/// <item><description>Gloves and boots: <b>cooldown reduction</b> (3 / 6 / 10 % each, so up to 20 %).</description></item>
/// <item><description>Rings: <b>gold per assist</b> (5 / 10 / 15 % each); pendant: <b>passive gold</b> (5 / 10 / 15 %).</description></item>
/// </list>
/// </summary>
public static class MobaItemTraits
{
    /// <summary>Seconds a hit of an anti-heal weapon keeps the target's healing reduced.</summary>
    public const double AntiHealSeconds = 3;

    private static readonly double[] AntiHealByTier = { 0, 0.10, 0.20, 0.30 };
    private static readonly double[] CcResistByTier = { 0, 0.05, 0.10, 0.15 };
    private static readonly double[] CooldownByTier = { 0, 0.03, 0.06, 0.10 };
    private static readonly double[] GoldByTier = { 0, 0.05, 0.10, 0.15 };

    private static readonly ConditionalWeakTable<Player, AntiHealState> AntiHeals = new();

    private static IReadOnlyList<Views.Moba.MobaItemTrait>? _tooltipTraits;

    /// <summary>
    /// Gets the trait of every catalog item (client type, kind, percent), sent to the client for the tooltips.
    /// Kinds: 1 anti-heal, 2 CC resistance, 3 cooldown reduction, 4 assist gold, 5 passive gold.
    /// </summary>
    public static IReadOnlyList<Views.Moba.MobaItemTrait> TooltipTraits => _tooltipTraits ??= BuildTooltipTraits();

    /// <summary>The fraction of healing a weapon hit removes.</summary>
    /// <param name="champion">The attacking champion.</param>
    /// <returns>0..0.30.</returns>
    public static double AntiHealOf(Player champion)
        => AntiHealByTier[Math.Max(
            MobaItemPower.TierIn(champion, InventoryConstants.RightHandSlot, MobaShopCategory.Weapons),
            MobaItemPower.TierIn(champion, InventoryConstants.LeftHandSlot, MobaShopCategory.Weapons))];

    /// <summary>The crowd-control duration reduction of the armor and pants.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.30.</returns>
    public static double CcResistOf(Player champion)
        => CcResistByTier[MobaItemPower.TierIn(champion, InventoryConstants.ArmorSlot, MobaShopCategory.Sets)]
           + CcResistByTier[MobaItemPower.TierIn(champion, InventoryConstants.PantsSlot, MobaShopCategory.Sets)];

    /// <summary>The skill cooldown reduction of the gloves and boots.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.20.</returns>
    public static double CooldownReductionOf(Player champion)
        => CooldownByTier[MobaItemPower.TierIn(champion, InventoryConstants.GlovesSlot, MobaShopCategory.Sets)]
           + CooldownByTier[MobaItemPower.TierIn(champion, InventoryConstants.BootsSlot, MobaShopCategory.Sets)];

    /// <summary>The extra gold from assists (rings).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.30.</returns>
    public static double AssistGoldBonusOf(Player champion)
        => GoldByTier[MobaItemPower.TierIn(champion, InventoryConstants.Ring1Slot, MobaShopCategory.Accessories)]
           + GoldByTier[MobaItemPower.TierIn(champion, InventoryConstants.Ring2Slot, MobaShopCategory.Accessories)];

    /// <summary>The extra passive gold (pendant).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.15.</returns>
    public static double PassiveGoldBonusOf(Player champion)
        => GoldByTier[MobaItemPower.TierIn(champion, InventoryConstants.PendantSlot, MobaShopCategory.Accessories)];

    /// <summary>Marks the victim of an anti-heal hit.</summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <param name="victim">The victim.</param>
    public static void ApplyAntiHeal(Player attacker, Player victim)
    {
        var fraction = AntiHealOf(attacker);
        if (fraction <= 0)
        {
            return;
        }

        var state = AntiHeals.GetOrCreateValue(victim);
        state.Fraction = Math.Max(state.Fraction, fraction);
        state.UntilUtc = DateTime.UtcNow.AddSeconds(AntiHealSeconds);
    }

    /// <summary>Scales an amount of healing the champion is about to receive by its active anti-heal.</summary>
    /// <param name="target">The healed champion (anything but a MOBA champion is returned unchanged).</param>
    /// <param name="amount">The healing amount.</param>
    /// <returns>The reduced healing.</returns>
    public static double ScaleHealing(IAttackable? target, double amount)
    {
        if (target is Player { IsMobaClone: true } champion
            && AntiHeals.TryGetValue(champion, out var state)
            && DateTime.UtcNow < state.UntilUtc)
        {
            return amount * (1 - state.Fraction);
        }

        return amount;
    }

    private static List<Views.Moba.MobaItemTrait> BuildTooltipTraits()
    {
        var traits = new Dictionary<ushort, Views.Moba.MobaItemTrait>();
        foreach (var entry in MobaShopCatalog.Entries)
        {
            var tier = (int)entry.Tier;
            (byte Kind, double[] Table)? trait = entry.Category switch
            {
                MobaShopCategory.Weapons when entry.Quantity == 1 => (1, AntiHealByTier),
                MobaShopCategory.Sets or MobaShopCategory.SetsSustain when entry.Group is 8 or 9 => (2, CcResistByTier),
                MobaShopCategory.Sets or MobaShopCategory.SetsSustain when entry.Group is 10 or 11 => (3, CooldownByTier),
                MobaShopCategory.Accessories => (entry.Number is 8 or 23 or 24 ? (byte)4 : (byte)5, GoldByTier),
                _ => null,
            };

            if (trait is { } t)
            {
                var type = (ushort)((entry.Group * 512) + entry.Number);
                traits.TryAdd(type, new Views.Moba.MobaItemTrait(type, t.Kind, (byte)Math.Round(t.Table[tier] * 100)));
            }
        }

        return traits.Values.ToList();
    }

    private sealed class AntiHealState
    {
        public double Fraction;

        public DateTime UntilUtc;
    }
}
