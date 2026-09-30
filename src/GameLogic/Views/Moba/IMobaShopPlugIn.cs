// <copyright file="IMobaShopPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// View plugin for the MOBA shop: a text menu shown in the NPC dialogue window and the
/// server-side prices shown in the item tooltips of the merchant window.
/// </summary>
public interface IMobaShopPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows a menu with text options in the NPC dialogue window.
    /// The client answers with the index of the chosen option.
    /// </summary>
    /// <param name="menuId">The identifier of the menu, echoed back by the client.</param>
    /// <param name="title">The title (NPC name).</param>
    /// <param name="text">The text the NPC says.</param>
    /// <param name="options">The options, shown by name.</param>
    ValueTask ShowMenuAsync(byte menuId, string title, string text, IReadOnlyList<string> options);

    /// <summary>
    /// Sends the prices the client shows in the tooltips while the merchant window is open.
    /// </summary>
    /// <param name="prices">The prices.</param>
    /// <param name="sellPercent">The percentage of the price paid back when selling.</param>
    ValueTask ShowPricesAsync(IReadOnlyCollection<MobaShopPrice> prices, byte sellPercent);

    /// <summary>
    /// Sends the MOBA-only options of the shop items (see <see cref="MobaItemTrait"/>) the client
    /// adds to the item tooltips.
    /// </summary>
    /// <param name="traits">The traits, one per item type.</param>
    ValueTask ShowItemTraitsAsync(IReadOnlyCollection<MobaItemTrait> traits);

    /// <summary>
    /// Sends the MOBA options of every known shield / book (shop ones and rolled drops), which can differ per item.
    /// </summary>
    /// <param name="shields">The shield options.</param>
    /// <returns>The task.</returns>
    ValueTask ShowShieldOptionsAsync(IReadOnlyCollection<MobaShieldOption> shields);
}
