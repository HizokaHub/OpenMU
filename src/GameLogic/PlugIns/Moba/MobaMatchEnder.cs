// <copyright file="MobaMatchEnder.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Ends a MOBA match when a nexus falls: announces the winner, stops the wave rhythm,
/// clears the arena (creeps + structures), shows every player the results table and sends the
/// participants back to town (their real character) when they press the button of the table or
/// after <see cref="ReturnSeconds"/> seconds.
/// </summary>
public static class MobaMatchEnder
{
    /// <summary>Seconds after the end of the match in which the server sends the remaining players back on its own.</summary>
    public const int ReturnSeconds = 90;

    /// <summary>Milliseconds between the victory banner and the results table.</summary>
    private const int ResultsDelayMs = 4000;

    private static readonly ConcurrentDictionary<GameMap, byte> Ending = new();

    // Keeps the one-shot return timers alive until they fire (a discarded Timer can be
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

        // The results are taken before the arena is cleared: the bots are still in it and the clock still runs.
        var arenaPlayers = await GetArenaPlayersAsync(map, gameContext).ConfigureAwait(false);
        var summary = MobaMatchResults.Build(arenaPlayers, winner, MobaArena.Of(map).Elapsed);

        // The ranking of the web (when the host installed it): taken now, before the invasion and the arena are cleared.
        MobaMatchRecorder.RecordMatch(arenaPlayers, winner, MobaArena.Of(map).Elapsed, MobaArena.Of(map).Format.ToString(), MobaGolden.HasStarted(map));

        // Everything the match left on the map goes now (bots included: they have no owner to eject, or they would idle
        // in the empty arena); the map itself waits for the players to be sent away (see FinishAsync).
        MobaWavePeriodicSpawner.Stop(map);
        await MobaStructureSpawner.RemoveTurretsAsync(map).ConfigureAwait(false);
        await MobaStructureSpawner.RemoveNexusesAsync(map).ConfigureAwait(false);
        await MobaWaveSpawner.DespawnAllCreepsAsync(map).ConfigureAwait(false);
        await MobaJungle.StopAsync(map).ConfigureAwait(false);
        MobaBotObjectives.Reset(MobaArena.Of(map));
        MobaMatchPhase.Reset(MobaArena.Of(map));
        await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);

        foreach (var player in arenaPlayers)
        {
            var playerTeam = MobaTeams.GetTeam(player);
            var winnerEs = winner == MobaTeam.Blue ? "AZUL" : "ROJO";
            var banner = playerTeam == MobaTeam.None
                ? $"GANA EL EQUIPO {winnerEs}"
                : playerTeam == winner ? "VICTORIA" : "DERROTA";

            await player.InvokeViewPlugInAsync<IShowMessagePlugIn>(p => p.ShowMessageAsync(banner, MessageType.GoldenCenter)).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[MOBA] Nexo {(losingTeam == MobaTeam.Blue ? "azul" : "rojo")} destruido - gana el equipo {winnerEs}. Volverás a Lorencia al pulsar el botón de la tabla de resultados o en {ReturnSeconds} s.").ConfigureAwait(false);
        }

        // Send the participants away if they do not press the button themselves.
        var timer = new Timer(
            _ => _ = FinishAsync(map, gameContext),
            null,
            TimeSpan.FromSeconds(ReturnSeconds),
            Timeout.InfiniteTimeSpan);
        EjectTimers[map] = timer;

        _ = ShowResultsLaterAsync(arenaPlayers, summary);
    }

    /// <summary>
    /// Sends a player who looks at the results table back to town: the clone is discarded and the session is closed, so the
    /// client reconnects by itself and enters the real character. Does nothing while the match is not over.
    /// </summary>
    /// <param name="player">The player who pressed the button.</param>
    /// <returns>A task.</returns>
    public static async ValueTask ReturnToTownAsync(Player player)
    {
        if (player.Account is not { } account
            || player.CurrentMap is not { } map
            || !IsEnding(map)
            || !MobaMatchRegistry.IsInMatch(account.GetId()))
        {
            return;
        }

        await SendAwayAsync(player).ConfigureAwait(false);

        // The last one to leave takes the arena down with it.
        if (map.IsInstance && !MobaMatchRegistry.UsesArena(map))
        {
            await FinishAsync(map, player.GameContext).ConfigureAwait(false);
        }
    }

    private static async Task ShowResultsLaterAsync(IReadOnlyList<Player> players, MobaMatchSummary summary)
    {
        try
        {
            await Task.Delay(ResultsDelayMs).ConfigureAwait(false);
            foreach (var player in players)
            {
                var name = player.SelectedCharacter?.Name ?? string.Empty;
                await player.InvokeViewPlugInAsync<IMobaResultsPlugIn>(p => p.ShowResultsAsync(summary, name, ReturnSeconds)).ConfigureAwait(false);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static async Task<IReadOnlyList<Player>> GetArenaPlayersAsync(GameMap map, IGameContext gameContext)
    {
        var all = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        return all.Where(p => ReferenceEquals(p.CurrentMap, map)).ToList();
    }

    private static async Task SendAwayAsync(Player player)
    {
        if (player.Account is not { } account)
        {
            return;
        }

        var clone = MobaMatchRegistry.Leave(account.GetId());
        if (clone is not null && !ReferenceEquals(clone, player.SelectedCharacter))
        {
            MobaCloneFactory.DetachClone(player, clone);
        }

        await player.DisconnectAsync().ConfigureAwait(false);
    }

    /// <summary>Sends everybody who is left back to town and takes the arena down. Runs once per match.</summary>
    private static async Task FinishAsync(GameMap map, IGameContext gameContext)
    {
        // Whoever takes the timer out runs the finish; a second call (button of the last player + timer) does nothing.
        if (!EjectTimers.TryRemove(map, out var timer))
        {
            return;
        }

        try
        {
            await timer.DisposeAsync().ConfigureAwait(false);
            foreach (var player in await GetArenaPlayersAsync(map, gameContext).ConfigureAwait(false))
            {
                if (player.Account is not { } account || !MobaMatchRegistry.IsInMatch(account.GetId()))
                {
                    continue;
                }

                await SendAwayAsync(player).ConfigureAwait(false);
            }
        }
        catch
        {
            // best effort
        }
        finally
        {
            if (map.IsInstance)
            {
                // The copy of the arena was made for this match only.
                await MobaArenaBoot.TearDownAsync(map, gameContext).ConfigureAwait(false);
            }

            Ending.TryRemove(map, out _);
        }
    }
}
