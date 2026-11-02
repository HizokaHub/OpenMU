// <copyright file="MobaCommanderOrders.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;

/// <summary>The outcome of an order to send a pack.</summary>
public enum MobaCommanderSendResult
{
    /// <summary>The pack was bought; it waits in the base until the next wave.</summary>
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

    /// <summary>The waiting zone of the lane already holds the most packs of the team (<see cref="MobaCommanderQueue.MaxPacksPerLane"/>).</summary>
    QueueFull,
}

/// <summary>The outcome of the order "send now" (release the waiting packs of the team without waiting for the wave timer).</summary>
public enum MobaCommanderSendNowResult
{
    /// <summary>The waiting creeps of the team were released.</summary>
    Released,

    /// <summary>The match is not running, or the team has no side.</summary>
    NotRunning,

    /// <summary>Nothing waits for the team; the cooldown was not spent.</summary>
    NothingWaiting,

    /// <summary>The cooldown of "send now" is still running.</summary>
    Cooldown,
}

/// <summary>
/// What the commanders of an arena do to the map: buy packs (which wait, held, in the base until the wave timer of the arena
/// releases them or the commander uses "send now"), and the one-second tick which feeds their economy, runs the wave timer and
/// plays the commander of every team which has no human one.
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
    /// Gets the index of a pack in <see cref="MobaCommanderUnits.All"/>.
    /// </summary>
    /// <param name="unit">The pack.</param>
    /// <returns>The index, or -1 for a pack which is not one of them.</returns>
    public static int IndexOf(MobaCommanderUnit unit)
    {
        for (var i = 0; i < MobaCommanderUnits.All.Count; i++)
        {
            if (ReferenceEquals(MobaCommanderUnits.All[i], unit) || MobaCommanderUnits.All[i].Id == unit.Id)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Buys a pack for a team, paying with its essence. The creeps do not march: they appear held, lined up in the waiting
    /// zone of the lane in the base, and are released together with the next wave (see <see cref="MobaCommanderQueue"/>).
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

        var queue = MobaCommanderQueue.Of(arena);
        if (queue.PacksWaiting(team, lane) >= MobaCommanderQueue.MaxPacksPerLane)
        {
            return MobaCommanderSendResult.QueueFull;
        }

        if (!board.TrySpend(team, unit.Cost))
        {
            return MobaCommanderSendResult.NotEnoughEssence;
        }

        var pack = queue.TryReservePack(team, lane, IndexOf(unit));
        if (pack is null)
        {
            board.Refund(team, unit.Cost);
            return MobaCommanderSendResult.QueueFull;
        }

        var spawned = 0;
        try
        {
            spawned = await MobaWaveSpawner.SpawnUnitsAsync(map, gameContext, team, lane, unit.Build(team), pack.Slot, creep => queue.Register(pack, creep)).ConfigureAwait(false);
        }
        finally
        {
            queue.Complete(pack);
        }

        if (spawned == 0)
        {
            board.Refund(team, unit.Cost);
            return MobaCommanderSendResult.Failed;
        }

        return MobaCommanderSendResult.Sent;
    }

    /// <summary>
    /// "Send now": the human commander of a team lets his own waiting creeps go at once. The wave timer is not touched; the order
    /// has a cooldown of <see cref="MobaCommanderQueue.SendNowCooldownSeconds"/> seconds, which is not spent when nothing waits.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="team">The team whose creeps are released.</param>
    /// <returns>What came of the order.</returns>
    public static MobaCommanderSendNowResult SendNow(GameMap map, MobaTeam team)
    {
        var arena = MobaArena.Of(map);
        if (team == MobaTeam.None || !MobaCommanderBoard.Of(arena).Running)
        {
            return MobaCommanderSendNowResult.NotRunning;
        }

        var queue = MobaCommanderQueue.Of(arena);
        if (queue.PacksWaiting(team) == 0)
        {
            return MobaCommanderSendNowResult.NothingWaiting;
        }

        if (queue.SendNowCooldown(team) > 0)
        {
            return MobaCommanderSendNowResult.Cooldown;
        }

        if (!queue.TryStartSendNowCooldown(team))
        {
            return MobaCommanderSendNowResult.Cooldown;
        }

        queue.Release(team);
        return MobaCommanderSendNowResult.Released;
    }

    /// <summary>
    /// Starts the economy of an arena: both teams earn essence every second, the wave timer runs and the teams without a human
    /// commander play a bot one.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    public static void Start(GameMap map, IGameContext gameContext)
    {
        StopTicker(map);
        var arena = MobaArena.Of(map);
        MobaCommanderBoard.Of(arena).Begin();
        MobaCommanderQueue.Of(arena).Begin();
        var ticks = 0;
        Tickers[map] = new Timer(
            _ => _ = TickAsync(map, gameContext, ++ticks),
            null,
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Stops the economy of an arena and forgets its commanders; whatever waits is let go.
    /// </summary>
    /// <param name="map">The arena map.</param>
    public static void Stop(GameMap map)
    {
        StopTicker(map);
        var arena = MobaArena.Of(map);
        MobaCommanderBoard.Of(arena).Reset();
        MobaCommanderQueue.Of(arena).Reset();
    }

    /// <summary>
    /// One second of the arena: the essence comes in, the wave timer runs (and releases the waiting creeps of both teams when it
    /// runs out), the waiting creeps stay held, the commanders' bodies are kept parked, the bot commanders decide and every human
    /// champion gets the state of the panel.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="tick">The number of the second since the economy started (the bots think every <see cref="MobaCommanderBot.ThinkSeconds"/>).</param>
    /// <returns>A task.</returns>
    public static async ValueTask StepAsync(GameMap map, IGameContext gameContext, int tick)
    {
        var arena = MobaArena.Of(map);
        var board = MobaCommanderBoard.Of(arena);
        var queue = MobaCommanderQueue.Of(arena);
        board.Tick(1);
        if (queue.Advance(1))
        {
            queue.Release();
        }
        else
        {
            queue.Renew();
        }

        await MobaCommanderBody.SyncAllAsync(map, gameContext).ConfigureAwait(false);

        if (tick % MobaCommanderBot.ThinkSeconds == 0)
        {
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

        foreach (var champion in await MobaCommanderPanel.ChampionsOfAsync(map, gameContext).ConfigureAwait(false))
        {
            await MobaCommanderPanel.SendStateAsync(champion).ConfigureAwait(false);
        }
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
            await StepAsync(map, gameContext, tick).ConfigureAwait(false);
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
