// <copyright file="MobaCommanderOrders.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;

/// <summary>The outcome of an order to send a pack.</summary>
public enum MobaCommanderSendResult
{
    /// <summary>The pack was sent.</summary>
    Sent,

    /// <summary>The match is not running, or the team has no side.</summary>
    NotRunning,

    /// <summary>The lane does not exist or is closed in this format.</summary>
    LaneClosed,

    /// <summary>The team does not have the essence.</summary>
    NotEnoughEssence,

    /// <summary>The lane has too many creeps of the team already.</summary>
    LaneJammed,

    /// <summary>Nothing could be spawned; the essence was given back.</summary>
    Failed,
}

/// <summary>
/// What the commanders of an arena do to the map: send packs down lanes, and the one-second tick which feeds their economy
/// and plays the commander of every team which has no human one.
/// </summary>
public static class MobaCommanderOrders
{
    private static readonly ConcurrentDictionary<GameMap, Timer> Tickers = new();

    private static readonly ConcurrentDictionary<GameMap, int> Busy = new();

    /// <summary>
    /// Gets the lanes a commander can send creeps down in an arena.
    /// </summary>
    /// <param name="arena">The arena.</param>
    /// <returns>The lane indexes.</returns>
    public static IReadOnlyList<int> OpenLanes(MobaArena arena)
        => MobaFormats.MiddleLaneOnly(arena.Format) ? [MobaLayout.MidLane] : Enumerable.Range(0, MobaLayout.LaneCount).ToArray();

    /// <summary>
    /// Sends a pack down a lane for a team, paying with its essence.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="team">The team which sends it.</param>
    /// <param name="unit">The pack.</param>
    /// <param name="lane">The lane index.</param>
    /// <returns>What came of the order.</returns>
    public static async ValueTask<MobaCommanderSendResult> SendAsync(GameMap map, IGameContext gameContext, MobaTeam team, MobaCommanderUnit unit, int lane)
    {
        var arena = MobaArena.Of(map);
        var board = MobaCommanderBoard.Of(arena);
        if (team == MobaTeam.None || !board.Running)
        {
            return MobaCommanderSendResult.NotRunning;
        }

        if (!OpenLanes(arena).Contains(lane))
        {
            return MobaCommanderSendResult.LaneClosed;
        }

        if (board.Essence(team) < unit.Cost)
        {
            return MobaCommanderSendResult.NotEnoughEssence;
        }

        if (MobaWaveSpawner.IsLaneJammed(map, team, lane))
        {
            return MobaCommanderSendResult.LaneJammed;
        }

        if (!board.TrySpend(team, unit.Cost))
        {
            return MobaCommanderSendResult.NotEnoughEssence;
        }

        var spawned = await MobaWaveSpawner.SpawnUnitsAsync(map, gameContext, team, lane, unit.Build(team)).ConfigureAwait(false);
        if (spawned == 0)
        {
            board.Refund(team, unit.Cost);
            return MobaCommanderSendResult.Failed;
        }

        return MobaCommanderSendResult.Sent;
    }

    /// <summary>
    /// Starts the economy of an arena: both teams earn essence every second and the teams without a human commander play
    /// a bot one.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    public static void Start(GameMap map, IGameContext gameContext)
    {
        StopTicker(map);
        MobaCommanderBoard.Of(MobaArena.Of(map)).Begin();
        var ticks = 0;
        Tickers[map] = new Timer(
            _ => _ = TickAsync(map, gameContext, ++ticks),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Stops the economy of an arena and forgets its commanders.
    /// </summary>
    /// <param name="map">The arena map.</param>
    public static void Stop(GameMap map)
    {
        StopTicker(map);
        MobaCommanderBoard.Of(MobaArena.Of(map)).Reset();
    }

    private static void StopTicker(GameMap map)
    {
        if (Tickers.TryRemove(map, out var timer))
        {
            timer.Dispose();
        }
    }

    private static async Task TickAsync(GameMap map, IGameContext gameContext, int tick)
    {
        // A slow tick never overlaps the next one.
        if (!Busy.TryAdd(map, 0))
        {
            return;
        }

        try
        {
            var arena = MobaArena.Of(map);
            var board = MobaCommanderBoard.Of(arena);
            board.Tick(1);
            if (tick % MobaCommanderBot.ThinkSeconds != 0)
            {
                return;
            }

            foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
            {
                if (board.CommanderOf(team) is not null)
                {
                    continue;
                }

                var decision = MobaCommanderBot.Decide(board, team, arena.Elapsed, Random.Shared, OpenLanes(arena));
                if (decision.Action == MobaCommanderBotAction.Economy)
                {
                    board.TryBuyIncome(team);
                }
                else if (decision.Action == MobaCommanderBotAction.Send && decision.Unit is { } unit)
                {
                    await SendAsync(map, gameContext, team, unit, decision.Lane).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Keep the timer alive; a failed tick just skips a decision.
        }
        finally
        {
            Busy.TryRemove(map, out _);
        }
    }
}
