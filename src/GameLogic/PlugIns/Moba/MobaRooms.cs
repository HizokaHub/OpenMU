// <copyright file="MobaRooms.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Persistence;

/// <summary>
/// A private room: friends who play a match without the queue. The host creates it with a format and gets a code; the friends
/// join with the code, everybody picks a team and the host starts the match (the rest of the places are filled with bots, if the
/// host wants). No ready check: whoever is in the room when the host starts is in the match.
/// </summary>
public sealed class MobaRoom
{
    /// <summary>Gets the code the friends type to join.</summary>
    public required string Code { get; init; }

    /// <summary>Gets the format of the match.</summary>
    public required MobaFormat Format { get; init; }

    /// <summary>Gets or sets the account of the host.</summary>
    public Guid Host { get; set; }

    /// <summary>Gets or sets a value indicating whether the free places are filled with bots when the match starts.</summary>
    public bool FillWithBots { get; set; } = true;

    /// <summary>Gets the members: account to session and team.</summary>
    public Dictionary<Guid, (Player Session, MobaTeam Team)> Members { get; } = new();

    /// <summary>Gets the players of a team.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The sessions.</returns>
    public IReadOnlyList<Player> TeamPlayers(MobaTeam team) => this.Members.Values.Where(m => m.Team == team).Select(m => m.Session).ToList();
}

/// <summary>
/// The private rooms of the MOBA mode (RAM only). Driven by the <c>/sala</c> command and by the queue master's menu.
/// </summary>
public static class MobaRooms
{
    /// <summary>The identifier of the room menu of the queue master.</summary>
    public const byte MenuId = 5;

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int CodeLength = 5;

    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<string, MobaRoom> ByCode = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<Guid, string> CodeOfAccount = new();

    /// <summary>Gets the room a player is in.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The room, or <see langword="null"/>.</returns>
    public static MobaRoom? RoomOf(Player player)
        => player.Account is { } account && CodeOfAccount.TryGetValue(account.GetId(), out var code) && ByCode.TryGetValue(code, out var room) ? room : null;

    /// <summary>Creates a room with the player as host (blue team).</summary>
    /// <param name="player">The host.</param>
    /// <param name="format">The format.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> CreateAsync(Player player, MobaFormat format)
    {
        if (CheckPlayer(player) is { } problem)
        {
            return problem;
        }

        if (RoomOf(player) is not null)
        {
            return "Ya estás en una sala. Sal de ella antes de crear otra.";
        }

        MobaRoom room;
        lock (Sync)
        {
            string code;
            do
            {
                code = new string(Enumerable.Range(0, CodeLength).Select(_ => CodeAlphabet[Random.Shared.Next(CodeAlphabet.Length)]).ToArray());
            }
            while (ByCode.ContainsKey(code));

            room = new MobaRoom { Code = code, Format = format, Host = player.Account!.GetId() };
            room.Members[room.Host] = (player, MobaTeam.Blue);
            ByCode[code] = room;
            CodeOfAccount[room.Host] = code;
        }

        Track(player);
        await PublishAsync(room).ConfigureAwait(false);
        return $"Sala creada: código {room.Code} ({MobaFormats.Label(format)}). Tus amigos entran con /sala unir {room.Code}.";
    }

    /// <summary>Joins a room by its code (the team with fewer players).</summary>
    /// <param name="player">The player.</param>
    /// <param name="code">The code.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> JoinAsync(Player player, string? code)
    {
        if (CheckPlayer(player) is { } problem)
        {
            return problem;
        }

        if (RoomOf(player) is not null)
        {
            return "Ya estás en una sala.";
        }

        if (string.IsNullOrWhiteSpace(code) || !ByCode.TryGetValue(code.Trim(), out var room))
        {
            return "No existe una sala con ese código.";
        }

        var teamSize = MobaFormats.TeamSize(room.Format);
        MobaTeam team;
        lock (Sync)
        {
            var blue = room.TeamPlayers(MobaTeam.Blue).Count;
            var red = room.TeamPlayers(MobaTeam.Red).Count;
            if (blue >= teamSize && red >= teamSize)
            {
                return "La sala está llena.";
            }

            team = blue <= red && blue < teamSize ? MobaTeam.Blue : MobaTeam.Red;
            room.Members[player.Account!.GetId()] = (player, team);
            CodeOfAccount[player.Account!.GetId()] = room.Code;
        }

        Track(player);
        await TellRoomAsync(room, $"{player.SelectedCharacter?.Name} se unió a la sala (equipo {TeamName(team)}).", except: player).ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return $"Entraste a la sala {room.Code} ({MobaFormats.Label(room.Format)}), equipo {TeamName(team)}.";
    }

    /// <summary>Takes a player out of its room; the host leaving hands the room to another member, or closes it.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> LeaveAsync(Player player)
    {
        if (player.Account is not { } account || RoomOf(player) is not { } room)
        {
            return "No estás en ninguna sala.";
        }

        Player? newHost = null;
        lock (Sync)
        {
            room.Members.Remove(account.GetId());
            CodeOfAccount.TryRemove(account.GetId(), out _);
            if (room.Members.Count == 0)
            {
                ByCode.TryRemove(room.Code, out _);
            }
            else if (room.Host == account.GetId())
            {
                var next = room.Members.First();
                room.Host = next.Key;
                newHost = next.Value.Session;
            }
        }

        Untrack(player);
        await player.InvokeViewPlugInAsync<IMobaRoomPlugIn>(p => p.ShowRoomAsync(null)).ConfigureAwait(false);
        var name = player.SelectedCharacter?.Name;
        await TellRoomAsync(room, newHost is null ? $"{name} salió de la sala." : $"{name} salió de la sala; {newHost.SelectedCharacter?.Name} es el nuevo anfitrión.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return "Saliste de la sala.";
    }

    /// <summary>Moves the player to the other team of its room.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> SwitchTeamAsync(Player player)
    {
        if (player.Account is not { } account || RoomOf(player) is not { } room)
        {
            return "No estás en ninguna sala.";
        }

        string answer;
        lock (Sync)
        {
            var current = room.Members[account.GetId()].Team;
            var other = current == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;
            if (room.TeamPlayers(other).Count >= MobaFormats.TeamSize(room.Format))
            {
                return $"El equipo {TeamName(other)} está lleno.";
            }

            room.Members[account.GetId()] = (player, other);
            answer = $"Ahora estás en el equipo {TeamName(other)}.";
        }

        await TellRoomAsync(room, $"{player.SelectedCharacter?.Name} cambió de equipo.", except: player).ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return answer;
    }

    /// <summary>Lets the host choose whether the free places are filled with bots.</summary>
    /// <param name="player">The player.</param>
    /// <param name="fill">Whether to fill with bots.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> SetBotsAsync(Player player, bool fill)
    {
        if (player.Account is not { } account || RoomOf(player) is not { } room)
        {
            return "No estás en ninguna sala.";
        }

        if (room.Host != account.GetId())
        {
            return "Solo el anfitrión de la sala puede cambiar los bots.";
        }

        room.FillWithBots = fill;
        await TellRoomAsync(room, fill ? "Los lugares libres se rellenarán con bots." : "Sin bots: los dos equipos tienen que quedar igualados.", except: player).ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return fill ? "Rellenar con bots: sí." : "Rellenar con bots: no.";
    }

    /// <summary>Describes the room of a player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The text (a few lines).</returns>
    public static string Describe(Player player)
    {
        if (RoomOf(player) is not { } room)
        {
            return "No estás en ninguna sala.";
        }

        string Names(MobaTeam team) => string.Join(", ", room.Members.Where(m => m.Value.Team == team).Select(m => (m.Key == room.Host ? "★" : string.Empty) + m.Value.Session.SelectedCharacter?.Name));
        var size = MobaFormats.TeamSize(room.Format);
        return $"Sala {room.Code} - {MobaFormats.Label(room.Format)} - bots: {(room.FillWithBots ? "sí" : "no")}\n" +
               $"Azul ({room.TeamPlayers(MobaTeam.Blue).Count}/{size}): {Names(MobaTeam.Blue)}\n" +
               $"Rojo ({room.TeamPlayers(MobaTeam.Red).Count}/{size}): {Names(MobaTeam.Red)}";
    }

    /// <summary>Starts the match of the room (host only).</summary>
    /// <param name="player">The host.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> StartAsync(Player player)
    {
        if (player.Account is not { } account || RoomOf(player) is not { } room)
        {
            return "No estás en ninguna sala.";
        }

        if (room.Host != account.GetId())
        {
            return "Solo el anfitrión puede empezar la partida.";
        }

        var teamSize = MobaFormats.TeamSize(room.Format);
        var blue = room.TeamPlayers(MobaTeam.Blue);
        var red = room.TeamPlayers(MobaTeam.Red);
        if (!room.FillWithBots && (blue.Count != red.Count || blue.Count == 0))
        {
            return "Sin bots, los dos equipos tienen que tener los mismos jugadores (al menos uno). Activa los bots o cambia de equipo.";
        }

        foreach (var member in room.Members.Values.Select(m => m.Session))
        {
            if (CheckPlayer(member) is { } problem)
            {
                return $"{member.SelectedCharacter?.Name}: {problem}";
            }

            if (member is OfflinePlayer || member.Account is null)
            {
                return $"{member.SelectedCharacter?.Name} no está conectado.";
            }
        }

        // The room is closed before the match starts: its members belong to the match now.
        lock (Sync)
        {
            ByCode.TryRemove(room.Code, out _);
            foreach (var id in room.Members.Keys)
            {
                CodeOfAccount.TryRemove(id, out _);
            }
        }

        foreach (var member in room.Members.Values.Select(m => m.Session))
        {
            Untrack(member);
            await member.InvokeViewPlugInAsync<IMobaRoomPlugIn>(p => p.ShowRoomAsync(null)).ConfigureAwait(false);
        }

        var now = DateTime.UtcNow;
        var proposal = new MobaMatchProposal(
            EntriesOf(room, MobaTeam.Blue, teamSize, now),
            EntriesOf(room, MobaTeam.Red, teamSize, now),
            room.Format);
        var online = room.Members.ToDictionary(m => m.Key, m => m.Value.Session);
        player.Logger.LogInformation("[MOBA-ROOM] room {Code} starts a {Format} match: {Blue} blue / {Red} red humans.", room.Code, MobaFormats.Label(room.Format), blue.Count, red.Count);
        await MobaMatchLauncher.LaunchAsync(proposal, online).ConfigureAwait(false);
        return "¡Empieza la partida!";
    }

    /// <summary>Runs a button of the room panel of the client.</summary>
    /// <param name="player">The player who pressed it.</param>
    /// <param name="action">The button: 1 change team, 2 bots, 3 start, 4 leave.</param>
    /// <returns>A task.</returns>
    public static async ValueTask HandleActionAsync(Player player, byte action)
    {
        var room = RoomOf(player);
        var answer = action switch
        {
            1 => await SwitchTeamAsync(player).ConfigureAwait(false),
            2 => await SetBotsAsync(player, !(room?.FillWithBots ?? true)).ConfigureAwait(false),
            3 => await StartAsync(player).ConfigureAwait(false),
            4 => await LeaveAsync(player).ConfigureAwait(false),
            _ => null,
        };

        if (answer is not null)
        {
            await player.ShowBlueMessageAsync("[Sala] " + answer).ConfigureAwait(false);
        }
    }

    /// <summary>Sends the panel of the room to every member.</summary>
    private static async ValueTask PublishAsync(MobaRoom room)
    {
        foreach (var (id, (session, _)) in room.Members.ToList())
        {
            MobaRoomMember Of(KeyValuePair<Guid, (Player Session, MobaTeam Team)> m)
                => new(m.Value.Session.SelectedCharacter?.Name ?? "?", m.Key == room.Host, m.Key == id, MobaMinimapKind.BaseClassIndex(m.Value.Session.SelectedCharacter?.CharacterClass?.Number ?? 0));

            var blue = room.Members.Where(m => m.Value.Team == MobaTeam.Blue).Select(Of).ToList();
            var red = room.Members.Where(m => m.Value.Team == MobaTeam.Red).Select(Of).ToList();
            var info = new MobaRoomInfo(room.Code, MobaFormats.Label(room.Format), room.FillWithBots, id == room.Host, MobaFormats.TeamSize(room.Format), blue, red);
            await session.InvokeViewPlugInAsync<IMobaRoomPlugIn>(p => p.ShowRoomAsync(info)).ConfigureAwait(false);
        }
    }

    private static List<MobaQueueEntry> EntriesOf(MobaRoom room, MobaTeam team, int teamSize, DateTime now)
    {
        var entries = room.Members
            .Where(m => m.Value.Team == team)
            .Select(m => new MobaQueueEntry(MobaQueueMode.Open, [new MobaQueueMember(m.Key, m.Value.Session.SelectedCharacter?.Name ?? "?")], now, room.Format))
            .ToList();
        var missing = room.FillWithBots ? teamSize - entries.Count : 0;
        if (missing > 0)
        {
            var bots = Enumerable.Range(1, missing).Select(i => new MobaQueueMember(Guid.NewGuid(), $"Bot {i}", IsBot: true)).ToList();
            entries.Add(new MobaQueueEntry(MobaQueueMode.WithBots, bots, now, room.Format));
        }

        return entries;
    }

    private static string? CheckPlayer(Player player)
    {
        if (player.Account is not { } account || player.SelectedCharacter is not { } character)
        {
            return "no está en el juego.";
        }

        if (player.IsMobaClone || MobaMatchRegistry.IsInMatch(account.GetId()))
        {
            return "ya está en una partida.";
        }

        if (MobaQueueService.Queue.StatusOf(account.GetId()) != MobaQueueStatus.None)
        {
            return "está en la cola de partidas: sal de ella primero.";
        }

        // The game masters of the test accounts skip the level 400 / Master requirement, like '/mobaqueue ... force'.
        return character.CharacterStatus == CharacterStatus.GameMaster ? null : MobaEligibility.Check(player, character);
    }

    private static string TeamName(MobaTeam team) => team == MobaTeam.Blue ? "azul" : "rojo";

    private static async ValueTask TellRoomAsync(MobaRoom room, string text, Player? except = null)
    {
        foreach (var member in room.Members.Values.Select(m => m.Session).ToList())
        {
            if (!ReferenceEquals(member, except))
            {
                await member.ShowBlueMessageAsync($"[Sala {room.Code}] {text}").ConfigureAwait(false);
            }
        }
    }

    private static void Track(Player player)
    {
        player.PlayerDisconnected -= OnDisconnectedAsync;
        player.PlayerDisconnected += OnDisconnectedAsync;
    }

    private static void Untrack(Player player) => player.PlayerDisconnected -= OnDisconnectedAsync;

    private static async ValueTask OnDisconnectedAsync(Player player) => await LeaveAsync(player).ConfigureAwait(false);
}
