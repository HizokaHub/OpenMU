// <copyright file="IMobaChannelPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Tells the client that the champion started (or stopped) channelling an action such as the
/// recall, so the HUD can draw the channel bar. The server stays authoritative.
/// </summary>
public interface IMobaChannelPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows or cancels the channel bar.
    /// </summary>
    /// <param name="kind">The kind of channel: 1 = recall, 2 = teleport to minion.</param>
    /// <param name="durationMs">The channel duration in milliseconds; 0 cancels the bar.</param>
    ValueTask ShowChannelAsync(byte kind, int durationMs);
}
