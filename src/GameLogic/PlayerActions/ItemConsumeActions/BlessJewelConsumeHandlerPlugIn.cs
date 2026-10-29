// -----------------------------------------------------------------------
// <copyright file="BlessJewelConsumeHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>
// -----------------------------------------------------------------------

namespace MUnique.OpenMU.GameLogic.PlayerActions.ItemConsumeActions;

using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Consume handler for upgrading items up to level 6 using the Jewel of Bless.
/// </summary>
[Guid("E95A0292-B3B4-4E8C-AC5A-7F3DB4F01A37")]
[PlugIn]
[Display(Name = nameof(PlugInResources.BlessJewelConsumeHandlerPlugIn_Name), Description = nameof(PlugInResources.BlessJewelConsumeHandlerPlugIn_Description), ResourceType = typeof(PlugInResources))]
public class BlessJewelConsumeHandlerPlugIn : UpgradeItemLevelJewelConsumeHandlerPlugIn<BlessJewelConsumeHandlerPlugInConfiguration>
{
    /// <inheritdoc />
    public override ItemIdentifier Key => ItemConstants.JewelOfBless;

    /// <inheritdoc />
    public override object CreateDefaultConfig()
    {
        return new BlessJewelConsumeHandlerPlugInConfiguration
        {
            MaximumLevel = 5,
            MinimumLevel = 0,
            SuccessRatePercentage = 100,
            SuccessRateBonusWithLuckPercentage = 0,
            ResetToLevel0WhenFailMinLevel = 0,
        };
    }

    /// <inheritdoc/>
    public override async ValueTask<bool> ConsumeItemAsync(Player player, Item item, Item? targetItem, FruitUsage fruitUsage)
    {
        if (!player.IsMobaClone)
        {
            return await base.ConsumeItemAsync(player, item, targetItem, fruitUsage).ConfigureAwait(false);
        }

        // MOBA match: the jewel of the golden goblins, which takes one item of the inventory to its maximum (see MobaPerfection).
        if (player.PlayerState.CurrentState != PlayerState.EnteredWorld)
        {
            return false;
        }

        if (targetItem is null || targetItem.ItemSlot <= InventoryConstants.LastEquippableItemSlotIndex)
        {
            await player.ShowBlueMessageAsync("[MOBA] Arrastra la joya sobre un ítem de tu inventario (sin equipar; equípalo después).").ConfigureAwait(false);
            return false;
        }

        if (PlugIns.Moba.MobaPerfection.TryMaximize(targetItem) is { } refusal)
        {
            await player.ShowBlueMessageAsync(refusal).ConfigureAwait(false);
            return false;
        }

        await this.ConsumeSourceItemAsync(player, item).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<IItemUpgradedPlugIn>(p => p.ItemUpgradedAsync(targetItem)).ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[MOBA] ¡Perfección! {targetItem.Definition?.Name} llegó a su máximo.").ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    protected override bool ModifyItem(Item item, IContext persistenceContext)
    {
        if (this.Configuration?.RepairTargetItems.Contains(item.Definition!) is true
            && item.Durability < item.GetMaximumDurabilityOfOnePiece())
        {
            item.Durability = item.GetMaximumDurabilityOfOnePiece();
            return true;
        }

        return base.ModifyItem(item, persistenceContext);
    }
}