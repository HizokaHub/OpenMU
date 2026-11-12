// <copyright file="GameContextEventNotificationExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Notifications about the events of the classic game (Chaos Castle, Blood Castle, Devil Square, Castle Siege).
/// </summary>
public static class GameContextEventNotificationExtensions
{
    /// <summary>
    /// Sends the notice of a classic event to every player except those in a MOBA match: the arena has none of those events,
    /// and the golden notice kept being drawn over the shop and the match.
    /// </summary>
    /// <param name="context">The game context.</param>
    /// <param name="message">The message.</param>
    /// <returns>The task.</returns>
    public static ValueTask SendEventNotificationAsync(this IGameContext context, string message)
    {
        var sendingMessage = message.TrimStart('!');
        return context.ForEachPlayerAsync(player => player.IsMobaClone
            ? Task.CompletedTask
            : player.InvokeViewPlugInAsync<IShowMessagePlugIn>(p => p.ShowMessageAsync(sendingMessage, MessageType.GoldenCenter)).AsTask());
    }
}
