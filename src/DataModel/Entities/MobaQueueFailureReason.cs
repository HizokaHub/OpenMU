// <copyright file="MobaQueueFailureReason.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Why a player failed the ready check of the MOBA queue.
/// </summary>
public enum MobaQueueFailureReason
{
    /// <summary>
    /// The player declined the match found (the decline button, or /mobadecline).
    /// </summary>
    Declined = 0,

    /// <summary>
    /// The player did not answer in time.
    /// </summary>
    TimedOut = 1,

    /// <summary>
    /// The player left the queue while the ready check was running.
    /// </summary>
    LeftQueue = 2,

    /// <summary>
    /// The player disconnected while the ready check was running.
    /// </summary>
    Disconnected = 3,
}
