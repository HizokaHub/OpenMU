// <copyright file="IMobaBannerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Shows a big banner text in the upper middle of the client's screen for a few seconds
/// ("¡COMIENZA LA PARTIDA!"), larger than the golden notice of the game.
/// </summary>
public interface IMobaBannerPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the banner.
    /// </summary>
    /// <param name="text">The text (UTF-8, up to about 90 characters).</param>
    /// <param name="durationMs">How long it stays, in milliseconds (it fades in and out).</param>
    /// <returns>A task.</returns>
    ValueTask ShowBannerAsync(string text, int durationMs);
}
