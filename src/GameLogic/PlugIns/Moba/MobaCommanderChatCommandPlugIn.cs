// <copyright file="MobaCommanderChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// The chat command of the commanders (see <see cref="MobaCommandersMode"/>). Players: <c>/mobacmd tomar</c> claims the
/// command of your team, <c>/mobacmd enviar soldado mid</c> sends a pack down a lane, <c>/mobacmd eco</c> buys income,
/// <c>/mobacmd estado</c> shows the economy. Game masters: <c>/mobacmd iniciar</c> starts a test match in the arena you are in
/// (you command blue, a bot commands red) and <c>/mobacmd parar</c> stops it.
/// </summary>
[Guid("5B7D2E94-0C1A-4F36-9D58-A3E6B1C72F04")]
[PlugIn]
[Display(Name = "MOBA: commander orders", Description = "Command '/mobacmd' - the orders of the commanders of the commanders mode.")]
[ChatCommandHelp(Command, "Commanders mode: /mobacmd tomar | soltar | enviar <soldado|bruto|elite> <top|mid|bot> | eco | estado; GM: /mobacmd iniciar | parar.", null, CharacterStatus.Normal)]
public class MobaCommanderChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/mobacmd";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <summary>
    /// Reads a lane from what a commander types.
    /// </summary>
    /// <param name="text">The text: top / arriba / 0, mid / medio / 1, bot / abajo / 2.</param>
    /// <returns>The lane index, or <see langword="null"/> if the text is not a lane.</returns>
    public static int? ParseLane(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "top" or "arriba" or "0" => 0,
        "mid" or "medio" or "centro" or "1" => MobaLayout.MidLane,
        "bot" or "abajo" or "2" => 2,
        _ => null,
    };

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
        var sub = words.FirstOrDefault()?.ToLowerInvariant() ?? "ayuda";
        var isGameMaster = player.SelectedCharacter?.CharacterStatus >= CharacterStatus.GameMaster;

        if (sub is "iniciar" or "start" or "parar" or "stop")
        {
            if (!isGameMaster)
            {
                await Tell(player, "Solo un GM puede iniciar o parar una partida de prueba.").ConfigureAwait(false);
                return;
            }

            await (sub is "iniciar" or "start" ? StartTestAsync(player) : StopTestAsync(player)).ConfigureAwait(false);
            return;
        }

        if (!player.IsMobaClone || player.CurrentMap is not { } map || player.Account is not { } account)
        {
            await Tell(player, "Primero tienes que estar dentro de una partida de la arena.").ConfigureAwait(false);
            return;
        }

        var arena = MobaArena.Of(map);
        if (arena.Mode.Id != MobaCommandersMode.ModeId)
        {
            await Tell(player, "Esta partida no es del modo Comandantes.").ConfigureAwait(false);
            return;
        }

        var board = MobaCommanderBoard.Of(arena);
        var team = MobaTeams.GetTeam(player);
        var accountId = account.GetId();
        switch (sub)
        {
            case "tomar" or "take":
                await Tell(player, board.TryClaim(team, accountId)
                    ? "Ahora eres el comandante de tu equipo."
                    : "Tu equipo ya tiene comandante.").ConfigureAwait(false);
                break;
            case "soltar" or "release":
                await Tell(player, board.Release(team, accountId)
                    ? "Dejaste el mando; un bot comanda tu equipo."
                    : "No eres el comandante de tu equipo.").ConfigureAwait(false);
                break;
            case "estado" or "status":
                await ShowStatusAsync(player, board, team, accountId).ConfigureAwait(false);
                break;
            case "eco" or "economia":
                await OrderAsync(player, board, team, accountId, () =>
                    board.TryBuyIncome(team)
                        ? Tell(player, $"Ingreso mejorado al nivel {board.IncomeLevel(team)}: {board.IncomePerSecond(team):0} de esencia por segundo.")
                        : Tell(player, board.NextIncomeCost(team) is { } cost ? $"Mejorar el ingreso cuesta {cost} de esencia." : "El ingreso ya está al máximo.")).ConfigureAwait(false);
                break;
            case "enviar" or "send":
                await OrderAsync(player, board, team, accountId, () => SendAsync(player, map, team, words)).ConfigureAwait(false);
                break;
            default:
                await ShowHelpAsync(player).ConfigureAwait(false);
                break;
        }
    }

    private static ValueTask Tell(Player player, string text) => player.ShowBlueMessageAsync($"[CMD] {text}");

    private static async ValueTask OrderAsync(Player player, MobaCommanderBoard board, MobaTeam team, Guid accountId, Func<ValueTask> order)
    {
        if (board.CommanderOf(team) != accountId)
        {
            await Tell(player, "Solo el comandante de tu equipo puede dar esa orden. Usa /mobacmd tomar si el mando está libre.").ConfigureAwait(false);
            return;
        }

        await order().ConfigureAwait(false);
    }

    private static async ValueTask SendAsync(Player player, GameMap map, MobaTeam team, IReadOnlyList<string> words)
    {
        var unit = MobaCommanderUnits.Find(words.Count > 1 ? words[1] : null);
        var lane = ParseLane(words.Count > 2 ? words[2] : null);
        if (unit is null || lane is null)
        {
            await Tell(player, "Uso: /mobacmd enviar <soldado|bruto|elite> <top|mid|bot>").ConfigureAwait(false);
            return;
        }

        var result = await MobaCommanderOrders.SendAsync(map, player.GameContext, team, unit, lane.Value).ConfigureAwait(false);
        var laneName = lane.Value == MobaLayout.MidLane ? "mid" : lane.Value == 0 ? "top" : "bot";
        await Tell(player, result switch
        {
            MobaCommanderSendResult.Sent => $"{unit.Name} enviados por {laneName}.",
            MobaCommanderSendResult.NotEnoughEssence => $"No tienes esencia: {unit.Name} cuesta {unit.Cost}.",
            MobaCommanderSendResult.LaneClosed => "Ese carril está cerrado en este formato.",
            MobaCommanderSendResult.LaneJammed => "Ese carril ya está lleno de tus criaturas.",
            MobaCommanderSendResult.NotRunning => "La partida no está en marcha.",
            _ => "No se pudo enviar.",
        }).ConfigureAwait(false);
    }

    private static async ValueTask ShowStatusAsync(Player player, MobaCommanderBoard board, MobaTeam team, Guid accountId)
    {
        var role = board.CommanderOf(team) == accountId ? "eres el comandante" : board.CommanderOf(team) is null ? "comanda un bot" : "comanda otro jugador";
        var upgrade = board.NextIncomeCost(team) is { } cost ? $"siguiente mejora {cost}" : "ingreso al máximo";
        await Tell(player, string.Create(
            CultureInfo.InvariantCulture,
            $"Esencia {board.Essence(team):0} (+{board.IncomePerSecond(team):0}/s, nivel {board.IncomeLevel(team)}; {upgrade}). En tu equipo {role}.")).ConfigureAwait(false);
        await Tell(player, "Paquetes: " + string.Join(" | ", MobaCommanderUnits.All.Select(u => $"{u.Id} {u.Cost} ({u.Description})"))).ConfigureAwait(false);
    }

    private static async ValueTask ShowHelpAsync(Player player)
    {
        await Tell(player, "Comandos: tomar | soltar | estado | eco | enviar <soldado|bruto|elite> <top|mid|bot>").ConfigureAwait(false);
        await Tell(player, "Ganas esencia cada segundo y la gastas en criaturas o en mejorar tu ingreso. Gana quien destruya el nexo enemigo.").ConfigureAwait(false);
    }

    private static async ValueTask StartTestAsync(Player player)
    {
        if (!player.IsMobaClone || player.CurrentMap is not { } map || player.Account is not { } account)
        {
            await Tell(player, "Primero entra a la arena con /moba.").ConfigureAwait(false);
            return;
        }

        var context = player.GameContext;
        var arena = MobaArena.Of(map);
        await StopMatchAsync(map, context).ConfigureAwait(false);
        arena.Mode = MobaGameModes.Commanders;
        MobaBotObjectives.Reset(arena);
        MobaMatchPhase.Reset(arena);
        await MobaArenaBoot.EnsureAsync(map, context).ConfigureAwait(false);

        MobaTeams.Set(player, MobaTeam.Blue);
        if (arena.MatchStartUtc == DateTime.MinValue)
        {
            arena.MatchStartUtc = DateTime.UtcNow;
        }

        await arena.Mode.StartMatchAsync(map, context).ConfigureAwait(false);
        MobaCommanderBoard.Of(arena).TryClaim(MobaTeam.Blue, account.GetId());
        await Tell(player, "Partida de prueba de Comandantes: tú comandas al equipo azul y un bot al rojo. Empiezas con 200 de esencia. /mobacmd estado para verla; /mobacmd parar para terminar.").ConfigureAwait(false);
    }

    private static async ValueTask StopTestAsync(Player player)
    {
        if (player.CurrentMap is not { } map)
        {
            return;
        }

        await StopMatchAsync(map, player.GameContext).ConfigureAwait(false);
        await Tell(player, "Partida de prueba detenida: criaturas y bots eliminados (las estructuras se quedan).").ConfigureAwait(false);
    }

    private static async ValueTask StopMatchAsync(GameMap map, IGameContext context)
    {
        await MobaArena.Of(map).Mode.StopAsync(map, context).ConfigureAwait(false);
        await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);
        await MobaWaveSpawner.DespawnAllCreepsAsync(map).ConfigureAwait(false);
    }
}
