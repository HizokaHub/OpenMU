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

    // The hand slots: 0 ("left" in OpenMU) holds the weapon of every class but the Elf, 1 ("right") holds the shield / book, the bow of the
    // Elf, the Dark Raven and the starter weapon of the clone. Which hand holds what is NOT fixed (2026-11-15: the code used to read the weapon
    // from slot 1 and the shield from slot 0, so the weapon and shield bonuses only worked for the Elf and the weapon skill raised the Aegis):
    // always look in both hands for the item of the wanted catalog category (WeaponOf / OffhandOf).
    private static readonly byte[] HandSlots = { InventoryConstants.LeftHandSlot, InventoryConstants.RightHandSlot };

    private static readonly byte[] DefenseSlots =
    {
        InventoryConstants.HelmSlot, InventoryConstants.ArmorSlot, InventoryConstants.PantsSlot,
        InventoryConstants.GlovesSlot, InventoryConstants.BootsSlot, InventoryConstants.WingsSlot,
        InventoryConstants.PendantSlot, InventoryConstants.Ring1Slot, InventoryConstants.Ring2Slot,
    };

    private static readonly byte[] DefenseSlotsWithoutHelm = DefenseSlots.Where(s => s != InventoryConstants.HelmSlot).ToArray();

    private static readonly byte[] DefenseSlotsWithoutGloves = DefenseSlots.Where(s => s != InventoryConstants.GlovesSlot).ToArray();

    /// <summary>How itemized a champion's weapon slots are, 0 (empty/starter) to 1 (both hands T3).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The offense item fraction, 0..1.</returns>
    public static double OffenseFractionOf(Player champion)
    {
        // Elf (bow + arrows) and Rage Fighter (gloves) have no shield / book: only the weapon counts (the best one, whatever hand holds it).
        if (MobaPassives.FamilyOf(champion) is MobaFamily.Elf or MobaFamily.RageFighter)
        {
            return Math.Clamp(WeaponTierOf(champion) / MaxTierPoints, 0.0, 1.0);
        }

        return FractionOf(champion, HandSlots);
    }

    /// <summary>The equipped weapon of the shop catalog (the one of the highest tier if both hands hold one), whatever hand holds it.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The weapon and its catalog entry, or <c>null</c>.</returns>
    public static (Item Item, MobaShopEntry Entry)? WeaponOf(Player champion) => HandItemOf(champion, MobaShopCategory.Weapons);

    /// <summary>The equipped shield / book of the shop catalog, whatever hand holds it.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The off-hand item and its catalog entry, or <c>null</c>.</returns>
    public static (Item Item, MobaShopEntry Entry)? OffhandOf(Player champion) => HandItemOf(champion, MobaShopCategory.Offhand);

    /// <summary>The tier (0 = none, 1..3) of the champion's equipped weapon.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The weapon tier.</returns>
    public static int WeaponTierOf(Player champion) => WeaponOf(champion) is { } weapon ? (int)weapon.Entry.Tier : 0;

    /// <summary>The tier (0 = none, 1..3) of the champion's equipped off-hand item (shield / book).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The off-hand tier.</returns>
    public static int OffhandTierOf(Player champion) => OffhandOf(champion) is { } offhand ? (int)offhand.Entry.Tier : 0;

    private static (Item Item, MobaShopEntry Entry)? HandItemOf(Player champion, MobaShopCategory category)
    {
        var family = MobaPassives.FamilyOf(champion);
        (Item Item, MobaShopEntry Entry)? best = null;
        foreach (var slot in HandSlots)
        {
            if (champion.Inventory?.Items.FirstOrDefault(i => i.ItemSlot == slot) is not { Definition: { } definition } item)
            {
                continue;
            }

            // The same item can be sold to two classes with another role (a shield of resistance for one, of cooldown for the other): the entry of the own class wins.
            var entry = MobaShopCatalog.Entries
                .Where(e => e.Category == category && e.Quantity == 1 && e.Group == definition.Group && e.Number == definition.Number)
                .OrderByDescending(e => e.Families is { } families && families.Contains(family))
                .FirstOrDefault();
            if (entry is not null && (best is null || entry.Tier > best.Value.Entry.Tier))
            {
                best = (item, entry);
            }
        }

        return best;
    }

    /// <summary>How itemized a champion's armour/wings/accessory slots are, 0 to 1 (all T3).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The defense item fraction, 0..1.</returns>
    public static double DefenseFractionOf(Player champion) => FractionOf(champion, DefenseSlotsOf(champion));

    /// <summary>
    /// The defense slots a class can actually fill (2026-11-17, P3): the Magic Gladiator has no helm in its sets and the Rage Fighter has no gloves
    /// (they are its weapon), so those slots don't count in the denominator and both classes reach 100 % with their four pieces + wings + accessories.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The slots that count for the defense item fraction.</returns>
    public static byte[] DefenseSlotsOf(Player champion) => MobaPassives.FamilyOf(champion) switch
    {
        MobaFamily.MagicGladiator => DefenseSlotsWithoutHelm,
        MobaFamily.RageFighter => DefenseSlotsWithoutGloves,
        _ => DefenseSlots,
    };

    /// <summary>
    /// The shop tier (0 = none, 1..3) of the item equipped in a slot, only if it is a catalog item of
    /// the given category (e.g. the weapon slot must hold a weapon, not the Dark Raven).
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <param name="slot">The inventory slot.</param>
    /// <param name="category">The expected catalog category.</param>
    /// <returns>The tier, or 0.</returns>
    public static int TierIn(Player champion, byte slot, MobaShopCategory category)
    {
        var item = champion.Inventory?.Items.FirstOrDefault(i => i.ItemSlot == slot);
        if (item?.Definition is not { } definition)
        {
            return 0;
        }

        var entry = MobaShopCatalog.Entries.FirstOrDefault(e => (e.Category == category || (category == MobaShopCategory.Sets && e.Category == MobaShopCategory.SetsSustain)) && e.Group == definition.Group && e.Number == definition.Number);
        return entry is null ? 0 : (int)entry.Tier;
    }

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
