// <copyright file="IMobaReadyCheckPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Drives the client's "match found" popup (the ready check of the MOBA queue): a countdown, how many
/// players accepted, and the accept / decline buttons. The client answers with the NPC-menu packet
/// (<c>C1 06 D5 07 menuId optionIndex</c>, menu 3: option 0 accepts, option 1 declines).
/// </summary>
public interface IMobaReadyCheckPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows, updates or hides the popup.
    /// </summary>
    /// <param name="status">What the popup shows.</param>
    /// <param name="remainingMs">The time left to answer, in milliseconds.</param>
    /// <param name="windowMs">The whole time the players have to answer, in milliseconds (for the progress bar).</param>
    /// <param name="accepted">How many players accepted so far.</param>
    /// <param name="total">How many players the match has.</param>
    /// <returns>A task.</returns>
    ValueTask ShowReadyCheckAsync(MobaReadyCheckStatus status, int remainingMs, int windowMs, int accepted, int total);
}
