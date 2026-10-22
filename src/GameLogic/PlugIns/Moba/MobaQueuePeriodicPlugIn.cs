// <copyright file="MobaQueuePeriodicPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Keeps the queue alive: puts the queue master into Lorencia of every game server and runs the queue's clock.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: queue upkeep", Description = "Spawns the queue master NPC in Lorencia and runs the MOBA queue (matches, ready checks).")]
[Guid("2D7B4F96-3E18-4A5C-B0E9-6F1A8C5D3B70")]
public class MobaQueuePeriodicPlugIn : IPeriodicTaskPlugIn
{
    /// <inheritdoc />
    public void ForceStart()
    {
        // Nothing to force; the task is cheap and runs on every periodic tick.
    }

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        MobaQueuePenalties.Bind(gameContext);
        await MobaQueueNpc.EnsureAsync(gameContext).ConfigureAwait(false);
        await MobaQueueService.TickAsync().ConfigureAwait(false);
        await MobaRooms.TickAsync().ConfigureAwait(false);
    }
}
