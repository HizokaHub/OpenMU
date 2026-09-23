// <copyright file="MobaShopEntry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// One entry of the catalog.
/// </summary>
/// <param name="Category">The category.</param>
/// <param name="Families">The champion families which see the entry; <c>null</c> = everyone the item qualifies for.</param>
/// <param name="Group">The item group.</param>
/// <param name="Number">The item number.</param>
/// <param name="Tier">The tier.</param>
/// <param name="Level">A fixed item level, overriding the tier level (e.g. potion variants); <c>null</c> = by tier.</param>
/// <param name="Quantity">The stack size for stackable items.</param>
public sealed record MobaShopEntry(MobaShopCategory Category, MobaFamily[]? Families, byte Group, short Number, MobaShopTier Tier, byte? Level = null, byte Quantity = 1);
