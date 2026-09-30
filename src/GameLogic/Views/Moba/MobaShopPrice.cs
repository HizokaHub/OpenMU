// <copyright file="MobaShopPrice.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// The server-side price of one kind of item in the MOBA shop. The client identifies an
/// item by these properties (not by slot), so the price follows the item when it moves
/// between the shop, the inventory and the equipment.
/// </summary>
/// <param name="ItemType">The client item type (group * 512 + number).</param>
/// <param name="Level">The item level (+N).</param>
/// <param name="OptionLevel">The level of the normal option (0 = none).</param>
/// <param name="HasLuck">Whether the item has the luck option.</param>
/// <param name="ExcellentCount">The number of excellent options.</param>
/// <param name="PerUnit">Whether <paramref name="Price"/> is per unit of a stack (multiplied by the durability).</param>
/// <param name="Price">The buy price.</param>
public readonly record struct MobaShopPrice(ushort ItemType, byte Level, byte OptionLevel, bool HasLuck, byte ExcellentCount, bool PerUnit, uint Price);

/// <summary>
/// A MOBA-only option of a shop item (anti-heal, crowd-control resistance, cooldown reduction, assist / passive gold),
/// which the client shows as an extra line in the item tooltip.
/// </summary>
/// <param name="ItemType">The client item type (group * 512 + number).</param>
/// <param name="Kind">The trait kind: 1 anti-heal, 2 CC resistance, 3 cooldown reduction, 4 assist gold, 5 passive gold.</param>
/// <param name="Percent">The size of the trait in percent.</param>
public readonly record struct MobaItemTrait(ushort ItemType, byte Kind, byte Percent);

/// <summary>
/// The MOBA options of one kind of shield / book (identified like a price, by type, level, option level, luck and
/// excellent count), shown by the client as tooltip lines.
/// </summary>
/// <param name="ItemType">The client item type (group * 512 + number).</param>
/// <param name="Level">The item level (+N).</param>
/// <param name="OptionLevel">The level of the normal option.</param>
/// <param name="HasLuck">Whether the item has the luck option.</param>
/// <param name="ExcellentCount">The number of excellent options.</param>
/// <param name="Options">The options as (kind, percent); kinds as in <see cref="MobaItemTrait"/>.</param>
public readonly record struct MobaShieldOption(ushort ItemType, byte Level, byte OptionLevel, bool HasLuck, byte ExcellentCount, IReadOnlyList<(byte Kind, byte Percent)> Options);
