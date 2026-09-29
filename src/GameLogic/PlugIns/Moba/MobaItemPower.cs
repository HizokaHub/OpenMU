// <copyright file="MobaItemPower.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel;

/// <summary>
/// The item-driven "B" side of the stat/item blends used across MOBA combat (damage in
/// <see cref="MobaSkillDamage"/>, mitigation in <see cref="MobaDefense"/>): how much of a
/// champion's equipped gear is MOBA-shop T1/T2/T3, expressed as a 0..1 fraction of "fully
/// itemized" (every relevant slot at T3). Read directly off the inventory instead of the
/// vanilla attribute system, so it stays accurate regardless of which excellent options a
/// given item roll happens to carry.
/// </summary>
public static class MobaItemPower
{
    private const double MaxTierPoints = 3.0; // T3

    private static readonly byte[] OffenseSlots = { InventoryConstants.LeftHandSlot, InventoryConstants.RightHandSlot };

    private static readonly byte[] DefenseSlots =
    {
        InventoryConstants.HelmSlot, InventoryConstants.ArmorSlot, InventoryConstants.PantsSlot,
        InventoryConstants.GlovesSlot, InventoryConstants.BootsSlot, InventoryConstants.WingsSlot,
        InventoryConstants.PendantSlot, InventoryConstants.Ring1Slot, InventoryConstants.Ring2Slot,
    };

    /// <summary>How itemized a champion's weapon slots are, 0 (empty/starter) to 1 (both hands T3).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The offense item fraction, 0..1.</returns>
    public static double OffenseFractionOf(Player champion) => FractionOf(champion, OffenseSlots);

    /// <summary>How itemized a champion's armour/wings/accessory slots are, 0 to 1 (all T3).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The defense item fraction, 0..1.</returns>
    public static double DefenseFractionOf(Player champion) => FractionOf(champion, DefenseSlots);

    private static double FractionOf(Player champion, byte[] slots)
    {
        if (slots.Length == 0 || champion.Inventory is not { } inventory)
        {
            return 0;
        }

        var sum = 0.0;
        foreach (var slot in slots)
        {
            var item = inventory.Items.FirstOrDefault(i => i.ItemSlot == slot);
            sum += TierPointsOf(item);
        }

        return Math.Clamp(sum / (slots.Length * MaxTierPoints), 0.0, 1.0);
    }

    private static double TierPointsOf(Item? item)
    {
        if (item?.Definition is not { } definition)
        {
            return 0;
        }

        var tier = MobaShopCatalog.Entries
            .FirstOrDefault(e => e.Group == definition.Group && e.Number == definition.Number)
            ?.Tier;

        return tier switch
        {
            MobaShopTier.T1 => 1,
            MobaShopTier.T2 => 2,
            MobaShopTier.T3 => 3,
            _ => 0,
        };
    }
}
