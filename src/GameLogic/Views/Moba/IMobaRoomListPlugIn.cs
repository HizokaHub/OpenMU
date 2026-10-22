// <copyright file="IMobaRoomListPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// One room of the list the room browser of the client shows.
/// </summary>
/// <param name="Id">The identifier of the room.</param>
/// <param name="Name">The name the host gave the room.</param>
/// <param name="HostName">The character name of the host.</param>
/// <param name="TeamSize">The places of every team.</param>
/// <param name="MidOnly">Whether only the middle lane is played.</param>
/// <param name="FillWithBots">Whether the free places are filled with bots.</param>
/// <param name="Locked">Whether the room has a password.</param>
/// <param name="Players">How many humans are in the room.</param>
/// <param name="AgeSeconds">How long ago the room was created.</param>
/// <param name="IdleSeconds">How long ago something happened in the room (the room closes after a while without news).</param>
public sealed record MobaRoomListEntry(int Id, string Name, string HostName, int TeamSize, bool MidOnly, bool FillWithBots, bool Locked, int Players, int AgeSeconds, int IdleSeconds);

/// <summary>
/// Shows the list of the open private rooms in the room browser of the client. The browser asks for a fresh list with
/// <c>C1 04 D5 1E</c>, creates a room with <c>C1 len D5 20 teamSize flags nameLen name</c> (flags: 1 middle lane only, 2 bots)
/// and joins one with <c>C1 08 D5 21 id(u32)</c>.
/// </summary>
public interface IMobaRoomListPlugIn : IViewPlugIn
{
    /// <summary>
    /// Sends the list of rooms.
    /// </summary>
    /// <param name="rooms">The rooms, the newest first.</param>
    /// <param name="open">Whether the browser opens (the player asked for it) or only refreshes if it is open already.</param>
    /// <returns>A task.</returns>
    ValueTask ShowRoomListAsync(IReadOnlyList<MobaRoomListEntry> rooms, bool open);
}
