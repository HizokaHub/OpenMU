// <copyright file="MobaPerfection.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The perfection jewel (2026-10-28): the loot of the golden goblins. It is a Jewel of Bless of the game which, in a MOBA match, is dragged onto one item of the
/// inventory (not an equipped one) and takes it to its maximum in one go: the item level of the third tier, the option level, luck, its skill and every excellent
/// option of the third tier (all the options of its kind, the Zen bonus aside). One jewel, one item, a single use. It works on the weapons, shields, set pieces,
/// wings and accessories of the shop.
/// </summary>
public static class MobaPerfection
{
    private static readonly MobaShopCategory[] Eligible =
    {
        MobaShopCategory.Weapons, MobaShopCategory.Offhand, MobaShopCategory.Sets, MobaShopCategory.SetsSustain, MobaShopCategory.Wings, MobaShopCategory.Accessories,
    };

    /// <summary>Gets the catalog entry of an item the jewel can improve, or <see langword="null"/> when it cannot.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The entry.</returns>
    public static MobaShopEntry? EntryOf(Item item)
        => item.Definition is { } definition
            ? MobaShopCatalog.Entries.FirstOrDefault(e => Eligible.Contains(e.Category) && e.Group == definition.Group && e.Number == definition.Number && e.Quantity == 1)
            : null;

    /// <summary>Takes the item to its maximum.</summary>
    /// <param name="item">The item (it is changed in place).</param>
    /// <returns>A message for the player when the item cannot be improved, otherwise <see langword="null"/>.</returns>
    public static string? TryMaximize(Item item)
    {
        if (EntryOf(item) is not { } entry || item.Definition is not { } definition)
        {
            return "[MOBA] La joya de perfección solo sirve en armas, escudos, piezas de set, alas y accesorios.";
        }

        var full = MobaShop.CreateItem(definition, entry with { Tier = MobaShopTier.T3, Level = null, Quantity = 1 });
        if (item.Level == full.Level && SameOptions(item, full))
        {
            return "[MOBA] Ese ítem ya está al máximo.";
        }

        var slot = item.ItemSlot;
        item.AssignValues(full);
        item.ItemSlot = slot;
        item.Durability = item.GetMaximumDurabilityOfOnePiece();
        return null;
    }

    private static bool SameOptions(Item a, Item b)
    {
        var left = a.ItemOptions.Select(o => (o.ItemOption?.OptionType, o.ItemOption?.Number, o.Level)).OrderBy(o => o.Number).ToList();
        var right = b.ItemOptions.Select(o => (o.ItemOption?.OptionType, o.ItemOption?.Number, o.Level)).OrderBy(o => o.Number).ToList();
        return left.SequenceEqual(right) && a.HasSkill == b.HasSkill;
    }
}
