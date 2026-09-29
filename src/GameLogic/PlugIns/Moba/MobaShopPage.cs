// <copyright file="MobaShopPage.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// One option of the vendor menu: a merchant grid showing one or more catalog categories.
/// </summary>
/// <param name="Name">The menu text.</param>
/// <param name="Categories">The catalog categories shown on the page, in display order.</param>
public sealed record MobaShopPage(string Name, IReadOnlyList<MobaShopCategory> Categories);
