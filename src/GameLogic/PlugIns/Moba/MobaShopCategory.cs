// <copyright file="MobaShopCategory.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The categories of the MOBA shop, in menu order.
/// </summary>
public enum MobaShopCategory
{
    /// <summary>Weapons (and ammunition).</summary>
    Weapons,

    /// <summary>Armor sets (helm, armor, pants, gloves, boots).</summary>
    Sets,

    /// <summary>Wings and capes.</summary>
    Wings,

    /// <summary>Rings and pendants.</summary>
    Accessories,

    /// <summary>Potions and buff consumables.</summary>
    Consumables,
}
