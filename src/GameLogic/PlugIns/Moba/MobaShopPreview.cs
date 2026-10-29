// <copyright file="MobaShopPreview.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// The "if you buy it" block of the shop panel (2026-10-28) and the undo of the last purchase. The preview shows, for an item of the shop, how a few stats of the champion
/// would change if it wore the item in place of the one it wears in that slot. The numbers are the raw bonuses of the item (base stats of its level and its options),
/// worked out in a throw-away attribute system so the live stats of the champion are never touched; the MOBA bonuses of the pieces (see <see cref="MobaArchetype"/>) are
/// not in it.
/// </summary>
public static class MobaShopPreview
{
    /// <summary>The seconds after a purchase during which it can be undone.</summary>
    public const int UndoSeconds = 120;

    private static readonly ConditionalWeakTable<Player, Purchase> LastPurchases = new();

    /// <summary>The stats of the preview, in the order they are shown.</summary>
    public enum Stat : byte
    {
        /// <summary>Maximum health.</summary>
        Health = 1,

        /// <summary>Maximum mana.</summary>
        Mana = 2,

        /// <summary>Base defense.</summary>
        Defense = 3,

        /// <summary>Physical damage (the average of the minimum and the maximum).</summary>
        PhysicalDamage = 4,

        /// <summary>Wizardry damage (the average of the minimum and the maximum).</summary>
        MagicDamage = 5,

        /// <summary>Maximum shield.</summary>
        Shield = 6,

        /// <summary>Attack speed.</summary>
        AttackSpeed = 7,
    }

    /// <summary>The lines of the preview of the item of a slot of the shop.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="item">The item of the shop.</param>
    /// <returns>The lines (only the stats which change).</returns>
    public static IReadOnlyList<MobaShopPreviewLine> LinesFor(Player player, Item item)
    {
        var slots = item.Definition?.ItemSlot?.ItemSlots;
        if (slots is null || slots.Count == 0 || player.Attributes is not { } live)
        {
            return Array.Empty<MobaShopPreviewLine>();
        }

        var slotNumber = slots.First();

        try
        {
            var slot = (byte)slotNumber;
            var worn = player.Inventory?.GetItem(slot);
            var added = DeltaOf(player, item, slot);
            var removed = worn is null ? new Dictionary<Stat, double>() : DeltaOf(player, worn, slot);
            var lines = new List<MobaShopPreviewLine>();
            foreach (var stat in Enum.GetValues<Stat>())
            {
                var change = added.GetValueOrDefault(stat) - removed.GetValueOrDefault(stat);
                var before = LiveValue(live, stat);
                if (Math.Abs(change) >= 1 && before >= 0)
                {
                    lines.Add(new MobaShopPreviewLine((byte)stat, (uint)Math.Round(before), (uint)Math.Round(Math.Max(0, before + change))));
                }
            }

            return lines.Take(6).ToList();
        }
        catch
        {
            return Array.Empty<MobaShopPreviewLine>();
        }
    }

    /// <summary>Remembers what a champion bought last, so the purchase can be undone.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="item">The item added to the inventory.</param>
    /// <param name="price">What was paid.</param>
    public static void NoteBought(Player player, Item item, long price)
    {
        LastPurchases.Remove(player);
        LastPurchases.Add(player, new Purchase(item, price, DateTime.UtcNow));
    }

    /// <summary>Forgets the last purchase (it cannot be undone any more).</summary>
    /// <param name="player">The champion.</param>
    public static void Forget(Player player) => LastPurchases.Remove(player);

    /// <summary>Takes back the last purchase: the item leaves the inventory and the whole price returns.</summary>
    /// <param name="player">The champion.</param>
    /// <returns>A task.</returns>
    public static async ValueTask UndoLastAsync(Player player)
    {
        if (!player.IsMobaClone || player.Inventory is not { } inventory)
        {
            return;
        }

        if (!LastPurchases.TryGetValue(player, out var purchase) || (DateTime.UtcNow - purchase.At).TotalSeconds > UndoSeconds)
        {
            await player.ShowBlueMessageAsync("[Tienda] No hay una compra reciente que deshacer (se puede durante 2 minutos, una sola vez).").ConfigureAwait(false);
            return;
        }

        if (!inventory.Items.Contains(purchase.Item))
        {
            LastPurchases.Remove(player);
            await player.ShowBlueMessageAsync("[Tienda] Ese ítem ya no está en tu inventario.").ConfigureAwait(false);
            return;
        }

        if (purchase.Item.ItemSlot <= InventoryConstants.LastEquippableItemSlotIndex)
        {
            await player.ShowBlueMessageAsync("[Tienda] Quítate el ítem antes de deshacer la compra.").ConfigureAwait(false);
            return;
        }

        if (!player.TryAddMoney((int)Math.Min(purchase.Price, int.MaxValue)))
        {
            await player.ShowBlueMessageAsync("[Tienda] No cabe el oro de vuelta.").ConfigureAwait(false);
            return;
        }

        var name = purchase.Item.Definition?.Name;
        LastPurchases.Remove(player);
        await inventory.RemoveItemAsync(purchase.Item).ConfigureAwait(false);
        await player.PersistenceContext.DeleteAsync(purchase.Item).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<Views.Inventory.IUpdateMoneyPlugIn>(p => p.UpdateMoneyAsync()).ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[Tienda] Compra deshecha: {name} (+{purchase.Price:N0} de oro).").ConfigureAwait(false);
    }

    private static double LiveValue(IAttributeSystem attributes, Stat stat) => stat switch
    {
        Stat.Health => attributes[Stats.MaximumHealth],
        Stat.Mana => attributes[Stats.MaximumMana],
        Stat.Defense => attributes[Stats.DefenseBase],
        Stat.PhysicalDamage => (attributes[Stats.MinimumPhysBaseDmg] + attributes[Stats.MaximumPhysBaseDmg]) / 2.0,
        Stat.MagicDamage => (attributes[Stats.MinimumWizBaseDmg] + attributes[Stats.MaximumWizBaseDmg]) / 2.0,
        Stat.Shield => attributes[Stats.MaximumShield],
        _ => attributes[Stats.AttackSpeed],
    };

    // What an item adds to the stats, worked out in an attribute system of its own (a copy of the item stands in the slot, so the factory accepts it).
    private static Dictionary<Stat, double> DeltaOf(Player player, Item item, byte slot)
    {
        var standIn = new TemporaryItem();
        standIn.AssignValues(item);
        standIn.ItemSlot = slot;
        var system = new AttributeSystem(Array.Empty<IAttribute>(), Array.Empty<IAttribute>(), Array.Empty<AttributeRelationship>());
        var wrappers = player.GameContext.ItemPowerUpFactory.GetPowerUps(standIn, system).ToList();
        try
        {
            return new Dictionary<Stat, double>
            {
                [Stat.Health] = system[Stats.MaximumHealth],
                [Stat.Mana] = system[Stats.MaximumMana],
                [Stat.Defense] = system[Stats.DefenseBase],
                [Stat.PhysicalDamage] = (system[Stats.MinimumPhysBaseDmg] + system[Stats.MaximumPhysBaseDmg] + system[Stats.MinimumPhysBaseDmgByWeapon] + system[Stats.MaximumPhysBaseDmgByWeapon]
                                         + system[Stats.MinPhysBaseDmgByRightWeapon] + system[Stats.MaxPhysBaseDmgByRightWeapon]) / 2.0,
                [Stat.MagicDamage] = (system[Stats.MinimumWizBaseDmg] + system[Stats.MaximumWizBaseDmg]) / 2.0,
                [Stat.Shield] = system[Stats.MaximumShield],
                [Stat.AttackSpeed] = system[Stats.AttackSpeed],
            };
        }
        finally
        {
            foreach (var wrapper in wrappers)
            {
                wrapper.Dispose();
            }
        }
    }

    private sealed record Purchase(Item Item, long Price, DateTime At);
}
