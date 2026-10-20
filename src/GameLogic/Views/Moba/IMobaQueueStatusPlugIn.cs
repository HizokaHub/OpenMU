// <copyright file="IMobaQueueStatusPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// What the client's queue panel shows. The panel is hidden when the status is <see cref="Hidden"/>.
/// </summary>
public enum MobaQueueStatusMode : byte
{
    /// <summary>No panel: the player is not searching (or a match was found and the ready check popup takes over).</summary>
    Hidden = 0,

    /// <summary>The open pool: solos and small parties, humans only.</summary>
    Open = 1,

    /// <summary>The pool of premade teams of five, humans only.</summary>
    Premade = 2,

    /// <summary>The pool which accepts bots: it shows the steps of the wait.</summary>
    WithBots = 3,

    /// <summary>The 3v3 search: humans only, then bots complete it after the wait.</summary>
    Trio = 4,

    /// <summary>The 1v1 search: humans only.</summary>
    Duel = 5,

    /// <summary>The ARAM search (5v5 in the middle lane).</summary>
    Aram = 6,
}

/// <summary>
/// The state of the player's search, for the queue panel of the client.
/// </summary>
/// <param name="Mode">What the panel shows.</param>
/// <param name="ElapsedSeconds">How long the player has been waiting.</param>
/// <param name="Waiting">How many players wait in the same pool.</param>
/// <param name="NextInSeconds">In how many seconds the next step of the wait comes; <see langword="null"/> if there is none.</param>
/// <param name="NextHumans">The humans needed at the next step.</param>
/// <param name="HumansNeeded">The humans needed now for a match with bots; 0 if it is too early for one.</param>
/// <param name="Stages">The steps of the wait: after how many seconds, how many humans a match with bots needs.</param>
public sealed record MobaQueueStatusInfo(
    MobaQueueStatusMode Mode,
    int ElapsedSeconds,
    int Waiting,
    int? NextInSeconds,
    int NextHumans,
    int HumansNeeded,
    IReadOnlyList<(int Seconds, int Humans)> Stages)
{
    /// <summary>Gets the status which hides the panel.</summary>
    public static MobaQueueStatusInfo Hidden { get; } = new(MobaQueueStatusMode.Hidden, 0, 0, null, 0, 0, []);
}

/// <summary>
/// Shows the queue panel of the client: how long the player waits, how many wait with them and, for the pool with bots,
/// the steps of the wait. The client's cancel button answers with the NPC-menu packet (<c>C1 06 D5 07 4 0</c>).
/// </summary>
public interface IMobaQueueStatusPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows, updates or hides the panel.
    /// </summary>
    /// <param name="status">The state of the search.</param>
    /// <returns>A task.</returns>
    ValueTask ShowQueueStatusAsync(MobaQueueStatusInfo status);
}
