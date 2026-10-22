// <copyright file="IMobaRoomChatPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Shows a line of the chat of the private room the player is in. The room panel writes with
/// <c>C2 hi lo D5 24 textLen text</c>.
/// </summary>
public interface IMobaRoomChatPlugIn : IViewPlugIn
{
    /// <summary>
    /// Adds a line to the chat of the room panel.
    /// </summary>
    /// <param name="name">The name of the player who wrote it (empty for a notice of the room).</param>
    /// <param name="text">The text.</param>
    /// <param name="isNotice">Whether the line is a notice of the room (somebody joined, was kicked...).</param>
    /// <returns>A task.</returns>
    ValueTask ShowChatLineAsync(string name, string text, bool isNotice);
}
