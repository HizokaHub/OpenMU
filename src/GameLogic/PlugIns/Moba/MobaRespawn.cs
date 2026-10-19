// <copyright file="MobaRespawn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Respawn rules of a MOBA champion (humans and bots alike): the wait grows linearly with
/// the match clock, and the champion comes back next to its team's shop instead of the
/// map's default spawn gate.
/// </summary>
[PlugIn]
[Display(Name = nameof(MobaRespawn), Description = "Respawns MOBA champions next to their team's shop.")]
[Guid("5D2B7E94-1C6A-4F38-A0E5-8B9C3D4F6A12")]
public class MobaRespawn : IPlayerSpawnGateSelectionPlugIn
{
    /// <summary>The wait at the start of the match, in seconds.</summary>
    public const double BaseSeconds = 6;

    /// <summary>Seconds added to the wait for every minute of match time.</summary>
    public const double SecondsPerMinute = 0.5;

    /// <summary>The longest possible wait, in seconds.</summary>
    public const double MaxSeconds = 40;

    /// <summary>
    /// Gets the respawn wait for a death after the given match time.
    /// </summary>
    /// <param name="matchElapsed">The time since the match started.</param>
    /// <returns>The wait.</returns>
    public static TimeSpan DelayAt(TimeSpan matchElapsed)
        => TimeSpan.FromSeconds(Math.Clamp(BaseSeconds + (SecondsPerMinute * matchElapsed.TotalMinutes), BaseSeconds, MaxSeconds));

    /// <summary>
    /// Gets the respawn wait of a death happening now.
    /// </summary>
    /// <returns>The wait.</returns>
    public static TimeSpan CurrentDelay() => DelayAt(MobaMatchTickPlugIn.MatchElapsed);

    /// <inheritdoc />
    public ValueTask SelectSpawnGateAsync(Player player, SpawnGateSelectionArgs args)
    {
        if (args.Gate is not null
            || !player.IsMobaClone
            || player.CurrentMap?.Definition is not { } map
            || map.Number != MobaCloneFactory.ArenaMapNumber
            || MobaRecall.BaseOf(player) is not { } point)
        {
            return ValueTask.CompletedTask;
        }

        args.Gate = new ExitGate
        {
            Map = map,
            X1 = (byte)point.X,
            X2 = (byte)point.X,
            Y1 = (byte)point.Y,
            Y2 = (byte)point.Y,
        };

        return ValueTask.CompletedTask;
    }
}
