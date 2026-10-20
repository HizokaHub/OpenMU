// <copyright file="MobaReadyCheckStatus.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// What the client's "match found" popup shows.
/// </summary>
public enum MobaReadyCheckStatus : byte
{
    /// <summary>No popup (the check ended: the match starts, failed, or the player left the queue).</summary>
    Hidden = 0,

    /// <summary>The player is asked to accept or decline.</summary>
    Asking = 1,

    /// <summary>The player accepted and waits for the others.</summary>
    Accepted = 2,
}
