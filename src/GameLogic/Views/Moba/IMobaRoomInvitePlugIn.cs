// <copyright file="IMobaRoomInvitePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Shows the invitation to a private room: a small popup with the name of the room and who invites, which the player accepts or
/// ignores. Accepting joins the room with <c>C1 len D5 21 id passwordLen password</c> (an invited player needs no password).
/// </summary>
public interface IMobaRoomInvitePlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the invitation.
    /// </summary>
    /// <param name="roomId">The identifier of the room.</param>
    /// <param name="roomName">The name of the room.</param>
    /// <param name="fromName">The character name of who invites.</param>
    /// <param name="seconds">For how long the invitation stays.</param>
    /// <returns>A task.</returns>
    ValueTask ShowInviteAsync(int roomId, string roomName, string fromName, int seconds);
}
