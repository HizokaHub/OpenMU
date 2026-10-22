// <copyright file="IMobaRoomPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// One player of a private room, as the room panel of the client shows it.
/// </summary>
/// <param name="Name">The character name.</param>
/// <param name="IsHost">Whether the player is the host of the room.</param>
/// <param name="IsMine">Whether the player is the one who receives the panel.</param>
/// <param name="BaseClass">The base class index of the character (the portrait the panel shows).</param>
/// <param name="IsReady">Whether the player said it is ready.</param>
/// <param name="HasSkills">Whether the player already picked its skills.</param>
public sealed record MobaRoomMember(string Name, bool IsHost, bool IsMine, byte BaseClass, bool IsReady, bool HasSkills);

/// <summary>
/// What the room panel of the client shows.
/// </summary>
/// <param name="Id">The identifier of the room.</param>
/// <param name="Name">The name the host gave the room.</param>
/// <param name="TeamSize">The places of every team.</param>
/// <param name="MidOnly">Whether only the middle lane is played.</param>
/// <param name="FillWithBots">Whether the free places are filled with bots.</param>
/// <param name="IsHost">Whether the receiving player is the host (it gets the host buttons).</param>
/// <param name="Locked">Whether the room has a password.</param>
/// <param name="Blue">The blue team.</param>
/// <param name="Red">The red team.</param>
public sealed record MobaRoomInfo(int Id, string Name, int TeamSize, bool MidOnly, bool FillWithBots, bool IsHost, bool Locked, IReadOnlyList<MobaRoomMember> Blue, IReadOnlyList<MobaRoomMember> Red);

/// <summary>
/// Shows the panel of the private room of the player. The buttons answer with <c>C1 05 D5 1C action</c>
/// (1 change team, 2 bots, 3 start, 4 leave, 5 close the room, 6 ready, 7 skills, 8 invite the party) and, for a player of the room, with
/// <c>C1 len D5 22 action nameLen name</c> (1 move, 2 kick, 3 make host).
/// </summary>
public interface IMobaRoomPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows or updates the panel; <see langword="null"/> hides it (the player left the room or the match starts).
    /// </summary>
    /// <param name="room">The room, or <see langword="null"/>.</param>
    /// <returns>A task.</returns>
    ValueTask ShowRoomAsync(MobaRoomInfo? room);
}
