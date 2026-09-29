// <copyright file="MobaShopTier.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The price tier of a shop entry. The tier decides how the item is upgraded
/// (level, luck, option, excellent options); the price itself comes from the formula.
/// </summary>
public enum MobaShopTier
{
    /// <summary>Cheap: +13, luck, option +8, 3 excellent options (top-priority picks).</summary>
    T1 = 1,

    /// <summary>Medium: +14, luck, option +12, 4 excellent options.</summary>
    T2 = 2,

    /// <summary>Expensive: +15, luck, option +16, 6 excellent options (all of them - "full").</summary>
    T3 = 3,
}
