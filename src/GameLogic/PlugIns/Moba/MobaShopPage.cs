// <copyright file="MobaShopPage.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// One option of the vendor menu: a merchant grid showing one or more catalog categories.
/// </summary>
/// <param name="Name">The menu text.</param>
/// <param name="Categories">The catalog categories shown on the page, in display order.</param>
/// <param name="Tier">When set, only the entries of that tier are shown.</param>
/// <param name="PackTogether">Whether the categories share rows (each item goes to the first free place of the grid) instead of starting on a fresh row; for the page
/// whose categories do not fit otherwise (the client draws them in sections by item type, not by position).</param>
public sealed record MobaShopPage(string Name, IReadOnlyList<MobaShopCategory> Categories, MobaShopTier? Tier = null, bool PackTogether = false);
