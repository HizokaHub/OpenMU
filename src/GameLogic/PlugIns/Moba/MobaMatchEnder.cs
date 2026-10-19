// <copyright file="MobaMatchEnder.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;

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

    private static readonly ConcurrentDictionary<GameMap, byte> Ending = new();

    // Keeps the one-shot eject timers alive until they fire (a discarded Timer can be
    // collected before its callback runs).
    private static readonly ConcurrentDictionary<GameMap, Timer> EjectTimers = new();

    /// <summary>Whether a match on the given map is already ending (guards a double nexus death).</summary>
    /// <param name="map">The map.</param>
    /// <returns><see langword="true"/> if end is in progress.</returns>
    public static bool IsEnding(GameMap map) => Ending.ContainsKey(map);

    /// <summary>
    /// Ends the match on <paramref name="map"/>. <paramref name="losingTeam"/> is the team
    /// whose nexus was destroyed; the other team wins.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="losingTeam">The team that lost its nexus.</param>
    public static async ValueTask EndMatchAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam)
    {
        if (!Ending.TryAdd(map, 0))
        {
            return;
        }

        var winner = losingTeam == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;

        MobaWavePeriodicSpawner.Stop(map);
        await MobaStructureSpawner.RemoveTurretsAsync(map).ConfigureAwait(false);
        await MobaStructureSpawner.RemoveNexusesAsync(map).ConfigureAwait(false);
        await MobaWaveSpawner.DespawnAllCreepsAsync(map).ConfigureAwait(false);
        await MobaJungle.StopAsync(map).ConfigureAwait(false);
        MobaBotObjectives.Reset(MobaArena.Of(map));
        MobaMatchPhase.Reset(MobaArena.Of(map));

        // Test bots (/mobafight, /mobabot) have no real owner to eject: remove them with the match, or they idle in the empty arena.
        await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);

        foreach (var player in await GetArenaPlayersAsync(map, gameContext).ConfigureAwait(false))
        {
            var playerTeam = MobaTeams.GetTeam(player);
            var winnerEs = winner == MobaTeam.Blue ? "AZUL" : "ROJO";
            var banner = playerTeam == MobaTeam.None
                ? $"GANA EL EQUIPO {winnerEs}"
                : playerTeam == winner ? "VICTORIA" : "DERROTA";

            await player.InvokeViewPlugInAsync<IShowMessagePlugIn>(p => p.ShowMessageAsync(banner, MessageType.GoldenCenter)).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[MOBA] Nexo {(losingTeam == MobaTeam.Blue ? "azul" : "rojo")} destruido - gana el equipo {winnerEs}. Volvés a pueblo en {EjectDelaySeconds}s...").ConfigureAwait(false);
        }

        // Reconnect the participants a few seconds later, so the win message lands first.
        var timer = new Timer(
            _ => _ = EjectParticipantsAsync(map, gameContext),
            null,
            TimeSpan.FromSeconds(EjectDelaySeconds),
            Timeout.InfiniteTimeSpan);
        EjectTimers[map] = timer;
    }

    private static async Task<IReadOnlyList<Player>> GetArenaPlayersAsync(GameMap map, IGameContext gameContext)
    {
        var all = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        return all.Where(p => ReferenceEquals(p.CurrentMap, map)).ToList();
    }

    private static async Task EjectParticipantsAsync(GameMap map, IGameContext gameContext)
    {
        try
        {
            foreach (var player in await GetArenaPlayersAsync(map, gameContext).ConfigureAwait(false))
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
            if (EjectTimers.TryRemove(map, out var timer))
            {
                await timer.DisposeAsync().ConfigureAwait(false);
            }

            Ending.TryRemove(map, out _);
        }
    }
}
