// <copyright file="MobaPing.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The pings of the team: a champion marks a tile with one of the <see cref="MobaPingKind"/> and every human of its team sees the mark (on the ground
/// and on the minimap). The client sends <c>C1 07 D5 35 kind x y</c>; the server checks it, applies the <see cref="MobaPingLimiter"/> and tells the team
/// (<see cref="IMobaPingPlugIn"/>, <c>C1 len D5 33</c>). The enemy never gets it.
/// </summary>
public static class MobaPing
{
    private static readonly Point ArenaCentre = new(128, 128);

    private static readonly ConditionalWeakTable<Player, MobaPingLimiter> Limiters = new();

    /// <summary>Whether a ping kind number is one of the known kinds.</summary>
    /// <param name="kind">The number.</param>
    /// <returns><c>true</c> for 1 to 10.</returns>
    public static bool IsKnownKind(byte kind) => kind >= (byte)MobaPingKind.Generic && kind <= (byte)MobaPingKind.EnemyVision;

    /// <summary>Handles a ping request of a champion.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="kind">The kind number.</param>
    /// <param name="x">The tile X.</param>
    /// <param name="y">The tile Y.</param>
    /// <returns>A task.</returns>
    public static async ValueTask HandleAsync(Player player, byte kind, byte x, byte y)
    {
        var team = MobaTeams.GetTeam(player);
        if (!player.IsMobaClone || player is MobaBotPlayer || team == MobaTeam.None || player.CurrentMap is not { } map || !IsKnownKind(kind))
        {
            return;
        }

        if (!Limiters.GetOrCreateValue(player).TryAcquire(DateTime.UtcNow))
        {
            await player.ShowBlueMessageAsync("Debes esperar antes de volver a hacer ping.").ConfigureAwait(false);
            return;
        }

        var allies = map.GetAttackablesInRange(ArenaCentre, 400)
            .OfType<Player>()
            .Where(p => p.IsMobaClone && p is not MobaBotPlayer && MobaTeams.GetTeam(p) == team)
            .ToList();
        foreach (var ally in allies)
        {
            var senderKey = player.GetId(ally);
            await ally.InvokeViewPlugInAsync<IMobaPingPlugIn>(p => p.ShowPingAsync((MobaPingKind)kind, (byte)team, x, y, senderKey, player.Name)).ConfigureAwait(false);
        }
    }
}
