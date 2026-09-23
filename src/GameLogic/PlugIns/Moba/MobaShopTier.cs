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
    /// <summary>Cheap: plain +0 item.</summary>
    T1 = 1,

    /// <summary>Medium: +7, luck, option, 2 excellent options.</summary>
    T2 = 2,

    /// <summary>Expensive: +11, luck, max option, 4 excellent options.</summary>
    T3 = 3,
}
