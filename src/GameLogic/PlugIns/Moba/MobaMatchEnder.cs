// <copyright file="MobaMatchEnder.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Ends a MOBA match when a nexus falls: announces the winner, stops the wave rhythm,
/// clears the arena (creeps + structures) and, after a short delay, reconnects every
/// participant as their real character. Test scaffolding for Fase 2 - a real match
/// context will own this.
/// </summary>
public static class MobaMatchEnder
{
    /// <summary>Seconds between the victory announcement and reconnecting the players.</summary>
    private const int EjectDelaySeconds = 8;

    private static readonly ConcurrentDictionary<ushort, byte> Ending = new();

    /// <summary>Whether a match on the given map is already ending (guards a double nexus death).</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns><see langword="true"/> if end is in progress.</returns>
    public static bool IsEnding(ushort mapId) => Ending.ContainsKey(mapId);

    /// <summary>
    /// Ends the match on <paramref name="map"/>. <paramref name="losingTeam"/> is the team
    /// whose nexus was destroyed; the other team wins.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="losingTeam">The team that lost its nexus.</param>
    public static async ValueTask EndMatchAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam)
    {
        if (!Ending.TryAdd(map.MapId, 0))
        {
            return;
        }

        var winner = losingTeam == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;

        MobaWavePeriodicSpawner.Stop(map.MapId);
        await MobaStructureSpawner.RemoveTurretsAsync(map).ConfigureAwait(false);
        await MobaStructureSpawner.RemoveNexusesAsync(map).ConfigureAwait(false);
        await MobaWaveSpawner.DespawnAllCreepsAsync(map).ConfigureAwait(false);

        var players = map.GetAttackablesInRange(new Point(128, 128), 400).OfType<Player>().ToList();
        foreach (var player in players)
        {
            await player.ShowBlueMessageAsync($"[MOBA] {losingTeam} nexus destroyed - {winner} team wins! Returning to town in {EjectDelaySeconds}s...").ConfigureAwait(false);
        }

        // Reconnect the participants a few seconds later, so the win message lands first.
        _ = new Timer(
            _ => _ = EjectParticipantsAsync(map),
            null,
            TimeSpan.FromSeconds(EjectDelaySeconds),
            Timeout.InfiniteTimeSpan);
    }

    private static async Task EjectParticipantsAsync(GameMap map)
    {
        try
        {
            var players = map.GetAttackablesInRange(new Point(128, 128), 400).OfType<Player>().ToList();
            foreach (var player in players)
            {
                if (player.Account is not { } account || !MobaMatchRegistry.IsInMatch(account.GetId()))
                {
                    continue;
                }

                var clone = MobaMatchRegistry.Leave(account.GetId());
                if (clone is not null && !ReferenceEquals(clone, player.SelectedCharacter))
                {
                    MobaCloneFactory.DetachClone(player, clone);
                }

                await player.DisconnectAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            Ending.TryRemove(map.MapId, out _);
        }
    }
}
