// <copyright file="MobaCommanderPanel.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Persistence;

/// <summary>What a button of the client's commander panel asks for (the <c>action</c> byte of <c>C1 07 D5 41</c>).</summary>
public enum MobaCommanderPanelAction : byte
{
    /// <summary>Send a pack (unit index, lane).</summary>
    Send = 0,

    /// <summary>Buy the next income level.</summary>
    Income = 1,

    /// <summary>Claim the command of the team.</summary>
    Claim = 2,

    /// <summary>Give the command up.</summary>
    Release = 3,

    /// <summary>Send now: release the waiting creeps of the team without waiting for the wave timer (unit and lane are ignored).</summary>
    SendNow = 4,
}

/// <summary>
/// The server side of the client's commander panel: builds what it shows and runs what its buttons ask for.
/// </summary>
public static class MobaCommanderPanel
{
    /// <summary>
    /// Builds what the panel of a player shows.
    /// </summary>
    /// <param name="player">The player; a champion of the arena.</param>
    /// <returns>The state; hidden if the player is not in a running match of the commanders mode.</returns>
    public static MobaCommanderPanelState StateOf(Player player)
    {
        if (!player.IsMobaClone || player is MobaBotPlayer || player.CurrentMap is not { } map || player.Account is not { } account)
        {
            return MobaCommanderPanelState.Hidden;
        }

        var arena = MobaArena.Of(map);
        var board = MobaCommanderBoard.Of(arena);
        var team = MobaTeams.GetTeam(player);
        if (arena.Mode.Id != MobaCommandersMode.ModeId || !board.Running || team == MobaTeam.None)
        {
            return MobaCommanderPanelState.Hidden;
        }

        var commander = board.CommanderOf(team);
        var queue = MobaCommanderQueue.Of(arena);
        var laneMask = MobaCommanderOrders.OpenLanes(arena).Aggregate(0, (mask, lane) => mask | (1 << lane));
        return new MobaCommanderPanelState(
            true,
            commander == account.GetId(),
            commander is null,
            (int)board.Essence(team),
            (int)Math.Round(board.IncomePerSecond(team)),
            board.IncomeLevel(team),
            MobaCommanderBoard.MaxIncomeLevel,
            board.NextIncomeCost(team) ?? 0,
            laneMask,
            MobaCommanderUnits.All.Select(u => u.Cost).ToList(),
            MobaWaveSpawner.CountCreepsPerLane(map, team),
            MobaWaveSpawner.CountCreepsPerLane(map, team == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue),
            queue.WaveSeconds,
            MobaCommanderQueue.WaveIntervalSeconds,
            queue.SendNowCooldown(team),
            queue.Queued(team, MobaCommanderUnits.All.Count));
    }

    /// <summary>
    /// Sends a player the panel state; every player of the commanders arena gets it once a second while the match runs.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SendStateAsync(Player player)
    {
        var state = StateOf(player);
        await player.InvokeViewPlugInAsync<IMobaCommanderPlugIn>(p => p.ShowCommanderAsync(state)).ConfigureAwait(false);
    }

    /// <summary>
    /// Hides the panel of every human champion of an arena (the match ended).
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask HideAllAsync(GameMap map, IGameContext gameContext)
    {
        foreach (var player in await ChampionsOfAsync(map, gameContext).ConfigureAwait(false))
        {
            await player.InvokeViewPlugInAsync<IMobaCommanderPlugIn>(p => p.ShowCommanderAsync(MobaCommanderPanelState.Hidden)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Gets the human champions of an arena.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The players.</returns>
    public static async ValueTask<IReadOnlyList<Player>> ChampionsOfAsync(GameMap map, IGameContext gameContext)
        => (await gameContext.GetPlayersAsync().ConfigureAwait(false))
            .Where(p => p.IsMobaClone && p is not MobaBotPlayer && ReferenceEquals(p.CurrentMap, map))
            .ToList();

    /// <summary>
    /// Runs what a button of the panel asked for, as the chat command does, and answers with a blue message.
    /// </summary>
    /// <param name="player">The player who pressed the button.</param>
    /// <param name="action">What the button does.</param>
    /// <param name="unitIndex">The pack (for <see cref="MobaCommanderPanelAction.Send"/>).</param>
    /// <param name="lane">The lane (for <see cref="MobaCommanderPanelAction.Send"/>).</param>
    /// <returns>A task.</returns>
    public static async ValueTask HandleOrderAsync(Player player, MobaCommanderPanelAction action, int unitIndex, int lane)
    {
        if (!player.IsMobaClone || player.CurrentMap is not { } map || player.Account is not { } account)
        {
            return;
        }

        var arena = MobaArena.Of(map);
        if (arena.Mode.Id != MobaCommandersMode.ModeId)
        {
            return;
        }

        // An action this server does not know (a newer client) is ignored.
        if (!Enum.IsDefined(action))
        {
            return;
        }

        var board = MobaCommanderBoard.Of(arena);
        var team = MobaTeams.GetTeam(player);
        var accountId = account.GetId();
        switch (action)
        {
            case MobaCommanderPanelAction.Claim:
                await TellAsync(player, board.TryClaim(team, accountId) ? "Ahora eres el comandante de tu equipo." : "Tu equipo ya tiene comandante.").ConfigureAwait(false);
                await MobaCommanderBody.SyncAsync(player).ConfigureAwait(false);
                break;
            case MobaCommanderPanelAction.Release:
                await TellAsync(player, board.Release(team, accountId) ? "Dejaste el mando; un bot comanda tu equipo." : "No eres el comandante de tu equipo.").ConfigureAwait(false);
                await MobaCommanderBody.SyncAsync(player).ConfigureAwait(false);
                break;
            case MobaCommanderPanelAction.SendNow when board.CommanderOf(team) == accountId:
                var sendNow = MobaCommanderOrders.SendNow(map, team);
                if (sendNow != MobaCommanderSendNowResult.Released)
                {
                    await TellAsync(player, DescribeSendNow(sendNow, MobaCommanderQueue.Of(arena).SendNowCooldown(team))).ConfigureAwait(false);
                }

                break;
            case MobaCommanderPanelAction.Income when board.CommanderOf(team) == accountId:
                await TellAsync(player, board.TryBuyIncome(team) ? $"Ingreso mejorado al nivel {board.IncomeLevel(team)}." : "No tienes esencia para mejorar el ingreso, o ya está al máximo.").ConfigureAwait(false);
                break;
            case MobaCommanderPanelAction.Send when board.CommanderOf(team) == accountId && unitIndex >= 0 && unitIndex < MobaCommanderUnits.All.Count:
                var unit = MobaCommanderUnits.All[unitIndex];
                var result = await MobaCommanderOrders.SendAsync(map, player.GameContext, team, unit, lane).ConfigureAwait(false);
                if (result != MobaCommanderSendResult.Sent)
                {
                    await TellAsync(player, Describe(result, unit)).ConfigureAwait(false);
                }

                break;
            default:
                await TellAsync(player, "Solo el comandante de tu equipo puede dar esa orden.").ConfigureAwait(false);
                break;
        }

        await SendStateAsync(player).ConfigureAwait(false);
    }

    private static string Describe(MobaCommanderSendResult result, MobaCommanderUnit unit) => result switch
    {
        MobaCommanderSendResult.NotEnoughEssence => $"No tienes esencia: {unit.Name} cuesta {unit.Cost}.",
        MobaCommanderSendResult.LaneClosed => "Ese carril está cerrado en este formato.",
        MobaCommanderSendResult.LaneJammed => "Ese carril ya está lleno de tus criaturas.",
        MobaCommanderSendResult.QueueFull => "La zona de espera de ese carril está llena: espera la oleada o usa Enviar ya.",
        MobaCommanderSendResult.NotRunning => "La partida no está en marcha.",
        _ => "No se pudo enviar.",
    };

    private static string DescribeSendNow(MobaCommanderSendNowResult result, int cooldown) => result switch
    {
        MobaCommanderSendNowResult.NothingWaiting => "No hay criaturas esperando en tu base.",
        MobaCommanderSendNowResult.Cooldown => $"Enviar ya estará listo en {cooldown} s.",
        _ => "La partida no está en marcha.",
    };

    private static ValueTask TellAsync(Player player, string text) => player.ShowBlueMessageAsync($"[CMD] {text}");
}
