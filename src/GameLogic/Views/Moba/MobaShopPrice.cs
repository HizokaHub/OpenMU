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
