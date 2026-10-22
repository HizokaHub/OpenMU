// <copyright file="MobaRooms.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Persistence;

/// <summary>
/// The private rooms of the MOBA mode (RAM only): the list of open rooms, creating, joining, leaving, the host's controls, the
/// chat of the room and the closing of the rooms nobody uses. Driven by the room browser and the room panel of the client, and
/// by the <c>/sala</c> command.
/// </summary>
public static class MobaRooms
{
    /// <summary>The most rooms open at the same time.</summary>
    public const int MaxRooms = 60;

    /// <summary>The longest room name.</summary>
    public const int MaxNameLength = 24;

    /// <summary>The longest chat line.</summary>
    public const int MaxChatLength = 120;

    /// <summary>After this long without news a room closes.</summary>
    public static readonly TimeSpan IdleLimit = TimeSpan.FromMinutes(15);

    /// <summary>After this long without news the members are told the room is about to close.</summary>
    public static readonly TimeSpan IdleWarning = TimeSpan.FromMinutes(12);

    /// <summary>The longest room password.</summary>
    public const int MaxPasswordLength = 16;

    private const int MaxChatHistory = 40;
    private static readonly TimeSpan RematchLife = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan InviteSeconds = TimeSpan.FromSeconds(30);
    private const int HistoryOnJoin = 20;
    private static readonly TimeSpan MinChatInterval = TimeSpan.FromMilliseconds(600);

    private static readonly object Sync = new();
    private static readonly ConcurrentDictionary<int, MobaRoom> ById = new();
    private static readonly ConcurrentDictionary<Guid, int> RoomIdOfAccount = new();
    private static readonly ConditionalWeakTable<Player, StrongBox<DateTime>> LastChat = new();
    private static readonly ConcurrentDictionary<Guid, Rematch> Rematches = new();
    private static int nextId = 100;

    private sealed record Rematch(Guid RoomKey, string Name, int TeamSize, bool MidOnly, bool Bots, string Password, Guid Host, MobaTeam Team, DateTime Expires);

    /// <summary>Gets the room a player is in.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The room, or <see langword="null"/>.</returns>
    public static MobaRoom? RoomOf(Player player)
        => player.Account is { } account && RoomIdOfAccount.TryGetValue(account.GetId(), out var id) && ById.TryGetValue(id, out var room) ? room : null;

    /// <summary>Gets the rooms which are open now (the newest first).</summary>
    /// <returns>The rooms.</returns>
    public static IReadOnlyList<MobaRoom> OpenRooms() => ById.Values.OrderByDescending(r => r.CreatedUtc).ToList();

    /// <summary>Creates a room with the player as host (blue team).</summary>
    /// <param name="player">The host.</param>
    /// <param name="name">The name of the room.</param>
    /// <param name="teamSize">The players per team (1 to 5).</param>
    /// <param name="midOnly">Whether only the middle lane is played.</param>
    /// <param name="fillWithBots">Whether the free places are filled with bots.</param>
    /// <param name="password">The password of the room; empty or <see langword="null"/> for an open room.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> CreateAsync(Player player, string? name, int teamSize, bool midOnly, bool fillWithBots, string? password = null)
    {
        if (CheckPlayer(player) is { } problem)
        {
            return problem;
        }

        if (RoomOf(player) is not null)
        {
            return "Ya estás en una sala. Sal de ella antes de crear otra.";
        }

        if (teamSize is < 1 or > 5)
        {
            return "Cada equipo puede tener de 1 a 5 jugadores.";
        }

        if (ById.Count >= MaxRooms)
        {
            return "Hay demasiadas salas abiertas. Prueba en un rato.";
        }

        var hostName = player.SelectedCharacter?.Name ?? "?";
        var clean = Clean(name, MaxNameLength);
        MobaRoom room;
        lock (Sync)
        {
            room = new MobaRoom
            {
                Id = ++nextId,
                Name = clean.Length >= 3 ? clean : $"Sala de {hostName}",
                TeamSize = teamSize,
                MidOnly = midOnly,
                FillWithBots = fillWithBots,
                Password = Clean(password, MaxPasswordLength),
                Host = player.Account!.GetId(),
            };
            room.Members[room.Host] = (player, MobaTeam.Blue);
            ById[room.Id] = room;
            RoomIdOfAccount[room.Host] = room.Id;
        }

        Track(player);
        player.Logger.LogInformation("[MOBA-ROOM] '{Host}' created room {Id} '{Name}' ({Label}, bots {Bots}).", hostName, room.Id, room.Name, room.Label, fillWithBots);
        await AddNoticeAsync(room, $"{hostName} creó la sala.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return $"Sala «{room.Name}» creada ({room.Label}){(room.IsLocked ? " con clave" : string.Empty)}.";
    }

    /// <summary>Joins a room (the team with fewer players).</summary>
    /// <param name="player">The player.</param>
    /// <param name="id">The identifier of the room.</param>
    /// <param name="password">The password typed by the player (not needed when invited).</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> JoinAsync(Player player, int id, string? password = null)
    {
        if (CheckPlayer(player) is { } problem)
        {
            return problem;
        }

        if (RoomOf(player) is not null)
        {
            return "Ya estás en una sala.";
        }

        if (!ById.TryGetValue(id, out var room))
        {
            return "Esa sala ya no existe.";
        }

        var accountId = player.Account!.GetId();
        if (room.Kicked.Contains(accountId))
        {
            return "El anfitrión te expulsó de esa sala.";
        }

        if (room.IsLocked && !room.Invited.Contains(accountId) && !string.Equals(Clean(password, MaxPasswordLength), room.Password, StringComparison.Ordinal))
        {
            return "La clave de la sala no es correcta.";
        }

        MobaTeam team;
        lock (Sync)
        {
            var blue = room.TeamPlayers(MobaTeam.Blue).Count;
            var red = room.TeamPlayers(MobaTeam.Red).Count;
            if (blue >= room.TeamSize && red >= room.TeamSize)
            {
                return "La sala está llena.";
            }

            team = blue <= red && blue < room.TeamSize ? MobaTeam.Blue : MobaTeam.Red;
            room.Members[accountId] = (player, team);
            RoomIdOfAccount[accountId] = room.Id;
        }

        Track(player);
        Touch(room);
        await AddNoticeAsync(room, $"{player.SelectedCharacter?.Name} entró a la sala (equipo {TeamName(team)}).").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        await SendHistoryAsync(room, player).ConfigureAwait(false);
        return $"Entraste a la sala «{room.Name}», equipo {TeamName(team)}.";
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

        await RemoveMemberAsync(room, player, "salió de la sala").ConfigureAwait(false);
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

        return await MoveToOtherTeamAsync(room, account.GetId(), $"{player.SelectedCharacter?.Name} cambió de equipo.").ConfigureAwait(false);
    }

    /// <summary>Lets the host choose whether the free places are filled with bots.</summary>
    /// <param name="player">The player.</param>
    /// <param name="fill">Whether to fill with bots.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> SetBotsAsync(Player player, bool fill)
    {
        if (HostRoomOf(player, out var room) is { } problem)
        {
            return problem;
        }

        room!.FillWithBots = fill;
        Touch(room);
        await AddNoticeAsync(room, fill ? "Los lugares libres se rellenarán con bots." : "Sin bots: los dos equipos tienen que quedar igualados.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return fill ? "Rellenar con bots: sí." : "Rellenar con bots: no.";
    }

    /// <summary>The host moves a player of the room to the other team.</summary>
    /// <param name="host">The host.</param>
    /// <param name="targetName">The character name of the player.</param>
    /// <returns>The answer for the host.</returns>
    public static async ValueTask<string> MovePlayerAsync(Player host, string targetName)
    {
        if (HostRoomOf(host, out var room) is { } problem)
        {
            return problem;
        }

        if (FindMember(room!, targetName) is not { } target)
        {
            return "Ese jugador ya no está en la sala.";
        }

        return await MoveToOtherTeamAsync(room!, target.Key, $"{host.SelectedCharacter?.Name} movió a {target.Value.Session.SelectedCharacter?.Name} de equipo.").ConfigureAwait(false);
    }

    /// <summary>The host kicks a player out of the room.</summary>
    /// <param name="host">The host.</param>
    /// <param name="targetName">The character name of the player.</param>
    /// <returns>The answer for the host.</returns>
    public static async ValueTask<string> KickAsync(Player host, string targetName)
    {
        if (HostRoomOf(host, out var room) is { } problem)
        {
            return problem;
        }

        if (FindMember(room!, targetName) is not { } target)
        {
            return "Ese jugador ya no está en la sala.";
        }

        if (target.Key == host.Account!.GetId())
        {
            return "No puedes expulsarte a ti mismo: sal de la sala.";
        }

        room!.Kicked.Add(target.Key);
        await target.Value.Session.ShowBlueMessageAsync($"[Sala] El anfitrión te expulsó de la sala «{room.Name}».").ConfigureAwait(false);
        await RemoveMemberAsync(room, target.Value.Session, "fue expulsado de la sala").ConfigureAwait(false);
        return $"{targetName} fue expulsado.";
    }

    /// <summary>The host hands the room to another player.</summary>
    /// <param name="host">The host.</param>
    /// <param name="targetName">The character name of the player.</param>
    /// <returns>The answer for the host.</returns>
    public static async ValueTask<string> MakeHostAsync(Player host, string targetName)
    {
        if (HostRoomOf(host, out var room) is { } problem)
        {
            return problem;
        }

        if (FindMember(room!, targetName) is not { } target || target.Key == room!.Host)
        {
            return "Elige a otro jugador de la sala.";
        }

        room.Host = target.Key;
        Touch(room);
        await AddNoticeAsync(room, $"{target.Value.Session.SelectedCharacter?.Name} es el nuevo anfitrión.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return "Entregaste la sala.";
    }

    /// <summary>The host closes the room.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The answer for the host.</returns>
    public static async ValueTask<string> CloseRoomAsync(Player host)
    {
        if (HostRoomOf(host, out var room) is { } problem)
        {
            return problem;
        }

        await CloseAsync(room!, "El anfitrión cerró la sala.").ConfigureAwait(false);
        return "Sala cerrada.";
    }

    /// <summary>Writes a line in the chat of the player's room.</summary>
    /// <param name="player">The player.</param>
    /// <param name="text">The text.</param>
    /// <returns>A task.</returns>
    public static async ValueTask ChatAsync(Player player, string text)
    {
        if (RoomOf(player) is not { } room)
        {
            return;
        }

        var clean = Clean(text, MaxChatLength);
        if (clean.StartsWith("/invitar ", StringComparison.OrdinalIgnoreCase))
        {
            await player.ShowBlueMessageAsync("[Sala] " + await InviteByNameAsync(player, clean[9..]).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        var last = LastChat.GetOrCreateValue(player);
        if (clean.Length == 0 || DateTime.UtcNow - last.Value < MinChatInterval)
        {
            return;
        }

        last.Value = DateTime.UtcNow;
        Touch(room);
        var name = player.SelectedCharacter?.Name ?? "?";
        Remember(room, name, clean, false);
        foreach (var member in room.Members.Values.Select(m => m.Session).ToList())
        {
            await member.InvokeViewPlugInAsync<IMobaRoomChatPlugIn>(p => p.ShowChatLineAsync(name, clean, false)).ConfigureAwait(false);
        }
    }

    /// <summary>Sends the list of the open rooms to a player, or its own room if it is in one.</summary>
    /// <param name="player">The player.</param>
    /// <param name="open">Whether the browser opens, or only refreshes.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SendListAsync(Player player, bool open)
    {
        var now = DateTime.UtcNow;
        var entries = OpenRooms()
            .Select(r => new MobaRoomListEntry(
                r.Id,
                r.Name,
                r.Members.TryGetValue(r.Host, out var host) ? host.Session.SelectedCharacter?.Name ?? "?" : "?",
                r.TeamSize,
                r.MidOnly,
                r.FillWithBots,
                r.IsLocked,
                r.Members.Count,
                (int)(now - r.CreatedUtc).TotalSeconds,
                (int)(now - r.LastActivityUtc).TotalSeconds))
            .ToList();
        await player.InvokeViewPlugInAsync<IMobaRoomListPlugIn>(p => p.ShowRoomListAsync(entries, open)).ConfigureAwait(false);
    }

    /// <summary>Opens what the player needs to see: the panel of its room, or the browser of the rooms.</summary>
    /// <param name="player">The player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OpenAsync(Player player)
    {
        if (RoomOf(player) is { } room)
        {
            await PublishAsync(room).ConfigureAwait(false);
            await SendHistoryAsync(room, player).ConfigureAwait(false);
            return;
        }

        await SendListAsync(player, true).ConfigureAwait(false);
    }

    /// <summary>Describes the room of a player.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The text (a few lines).</returns>
    public static string Describe(Player player)
    {
        if (RoomOf(player) is not { } room)
        {
            return $"No estás en ninguna sala. Hay {ById.Count} abierta(s): ábrelas con el Maestro de la Arena.";
        }

        string Names(MobaTeam team) => string.Join(", ", room.Members.Where(m => m.Value.Team == team).Select(m => (m.Key == room.Host ? "★" : string.Empty) + m.Value.Session.SelectedCharacter?.Name));
        return $"Sala «{room.Name}» (#{room.Id}) - {room.Label} - bots: {(room.FillWithBots ? "sí" : "no")}\n" +
               $"Azul ({room.TeamPlayers(MobaTeam.Blue).Count}/{room.TeamSize}): {Names(MobaTeam.Blue)}\n" +
               $"Rojo ({room.TeamPlayers(MobaTeam.Red).Count}/{room.TeamSize}): {Names(MobaTeam.Red)}";
    }

    /// <summary>Starts the match of the room (host only).</summary>
    /// <param name="player">The host.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> StartAsync(Player player)
    {
        if (HostRoomOf(player, out var room) is { } hostProblem)
        {
            return hostProblem;
        }

        var blue = room!.TeamPlayers(MobaTeam.Blue);
        var red = room.TeamPlayers(MobaTeam.Red);
        if (!room.FillWithBots && (blue.Count != red.Count || blue.Count == 0))
        {
            return "Sin bots, los dos equipos tienen que tener los mismos jugadores (al menos uno). Activa los bots o mueve a alguien de equipo.";
        }

        foreach (var member in room.Members.Values.Select(m => m.Session))
        {
            if (CheckPlayer(member, inRoom: true) is { } problem)
            {
                return $"{member.SelectedCharacter?.Name}: {problem}";
            }

            if (member is OfflinePlayer || member.Account is null)
            {
                return $"{member.SelectedCharacter?.Name} no está conectado.";
            }
        }

        var notReady = room.Members.Where(m => m.Key != room.Host && !room.Ready.Contains(m.Key)).Select(m => m.Value.Session.SelectedCharacter?.Name).ToList();
        if (notReady.Count > 0)
        {
            return $"Todavía no están listos: {string.Join(", ", notReady)}.";
        }

        // The room is closed before the match starts: its members belong to the match now.
        RememberForRematch(room);
        Unregister(room);
        foreach (var member in room.Members.Values.Select(m => m.Session))
        {
            Untrack(member);
            await member.InvokeViewPlugInAsync<IMobaRoomPlugIn>(p => p.ShowRoomAsync(null)).ConfigureAwait(false);
        }

        var now = DateTime.UtcNow;
        var proposal = new MobaMatchProposal(
            EntriesOf(room, MobaTeam.Blue, now),
            EntriesOf(room, MobaTeam.Red, now),
            room.Format);
        var online = room.Members.ToDictionary(m => m.Key, m => m.Value.Session);
        player.Logger.LogInformation("[MOBA-ROOM] room {Id} '{Name}' starts a {Label} match: {Blue} blue / {Red} red humans.", room.Id, room.Name, room.Label, blue.Count, red.Count);
        await MobaMatchLauncher.LaunchAsync(proposal, online).ConfigureAwait(false);
        return "¡Empieza la partida!";
    }

    /// <summary>Runs a button of the room panel of the client.</summary>
    /// <param name="player">The player who pressed it.</param>
    /// <param name="action">The button: 1 change team, 2 bots, 3 start, 4 leave, 5 close the room.</param>
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
            5 => await CloseRoomAsync(player).ConfigureAwait(false),
            6 => await ToggleReadyAsync(player).ConfigureAwait(false),
            7 => await OpenSkillsAsync(player).ConfigureAwait(false),
            8 => await InvitePartyAsync(player).ConfigureAwait(false),
            _ => null,
        };

        if (answer is not null)
        {
            await player.ShowBlueMessageAsync("[Sala] " + answer).ConfigureAwait(false);
        }
    }

    /// <summary>A player says it is ready, or not any more.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The answer for the player.</returns>
    public static async ValueTask<string> ToggleReadyAsync(Player player)
    {
        if (player.Account is not { } account || RoomOf(player) is not { } room)
        {
            return "No estás en ninguna sala.";
        }

        if (room.Host == account.GetId())
        {
            return "El anfitrión empieza la partida cuando todos los demás están listos.";
        }

        var ready = room.Ready.Add(account.GetId());
        if (!ready)
        {
            room.Ready.Remove(account.GetId());
        }

        Touch(room);
        await AddNoticeAsync(room, $"{player.SelectedCharacter?.Name} {(ready ? "está listo" : "ya no está listo")}.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return ready ? "Estás listo." : "Ya no estás listo.";
    }

    /// <summary>Opens the skill window of a player of a room.</summary>
    /// <param name="player">The player.</param>
    /// <returns>The answer for the player (empty: the window opens).</returns>
    public static async ValueTask<string?> OpenSkillsAsync(Player player)
    {
        if (RoomOf(player) is null)
        {
            return "No estás en ninguna sala.";
        }

        await MobaLoadoutService.OpenAsync(player).ConfigureAwait(false);
        return null;
    }

    /// <summary>The host invites every other member of its party to the room.</summary>
    /// <param name="host">The host.</param>
    /// <returns>The answer for the host.</returns>
    public static async ValueTask<string> InvitePartyAsync(Player host)
    {
        if (HostRoomOf(host, out var room) is { } problem)
        {
            return problem;
        }

        if (host.Party is not { } party || party.PartyList.Count < 2)
        {
            return "No tienes party. Para invitar a alguien escribe /invitar NOMBRE en el chat de la sala.";
        }

        var invited = 0;
        foreach (var member in party.PartyList.OfType<Player>().Where(p => !ReferenceEquals(p, host) && p is not OfflinePlayer))
        {
            if (await InviteAsync(room!, host, member).ConfigureAwait(false) is null)
            {
                invited++;
            }
        }

        return invited == 0 ? "Nadie de tu party puede recibir la invitación ahora." : $"Invitaste a {invited} jugador(es) de tu party.";
    }

    /// <summary>The host invites a connected player by character name.</summary>
    /// <param name="host">The host.</param>
    /// <param name="name">The character name.</param>
    /// <returns>The answer for the host.</returns>
    public static async ValueTask<string> InviteByNameAsync(Player host, string name)
    {
        if (HostRoomOf(host, out var room) is { } problem)
        {
            return problem;
        }

        var target = host.GameContext.GetPlayerByCharacterName(name.Trim());
        if (target is null || target is OfflinePlayer)
        {
            return $"No encuentro a «{name.Trim()}» conectado.";
        }

        return await InviteAsync(room!, host, target).ConfigureAwait(false) ?? $"Invitaste a {target.SelectedCharacter?.Name}.";
    }

    /// <summary>Sends the invitation of the room to a player.</summary>
    private static async ValueTask<string?> InviteAsync(MobaRoom room, Player host, Player target)
    {
        if (target.Account is not { } account || target.SelectedCharacter is null)
        {
            return "ese jugador no está en el juego.";
        }

        if (RoomOf(target) is not null || MobaMatchRegistry.IsInMatch(account.GetId()))
        {
            return $"{target.SelectedCharacter.Name} ya está en una sala o en una partida.";
        }

        room.Invited.Add(account.GetId());
        room.Kicked.Remove(account.GetId());
        await target.InvokeViewPlugInAsync<IMobaRoomInvitePlugIn>(p => p.ShowInviteAsync(room.Id, room.Name, host.SelectedCharacter?.Name ?? "?", (int)InviteSeconds.TotalSeconds)).ConfigureAwait(false);
        await target.ShowBlueMessageAsync($"[Sala] {host.SelectedCharacter?.Name} te invita a la sala «{room.Name}».").ConfigureAwait(false);
        return null;
    }

    /// <summary>
    /// Remembers who was in a room which starts a match: when they come back to town (the match ends) they find the same room
    /// again, with the same settings and the chat empty (the rematch).
    /// </summary>
    private static void RememberForRematch(MobaRoom room)
    {
        var expires = DateTime.UtcNow + RematchLife;
        foreach (var (accountId, (_, team)) in room.Members)
        {
            Rematches[accountId] = new Rematch(room.RematchId, room.Name, room.TeamSize, room.MidOnly, room.FillWithBots, room.Password, room.Host, team, expires);
        }
    }

    /// <summary>
    /// Puts the players of a finished room-match back into a room: the first one to be back in Lorencia recreates it (same name and
    /// settings) and the others join it, in their old team.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickRematchAsync(IGameContext gameContext)
    {
        if (Rematches.IsEmpty)
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var expired in Rematches.Where(r => r.Value.Expires < now).Select(r => r.Key).ToList())
        {
            Rematches.TryRemove(expired, out _);
        }

        foreach (var player in (await gameContext.GetPlayersAsync().ConfigureAwait(false)).Where(p => !p.IsMobaClone && p is not OfflinePlayer && p.Account is not null && p.SelectedCharacter is not null))
        {
            var accountId = player.Account!.GetId();
            if (!Rematches.TryGetValue(accountId, out var rematch)
                || RoomOf(player) is not null
                || MobaMatchRegistry.IsInMatch(accountId)
                || player.CurrentMap is not { MapId: MobaQueueNpc.MapNumber })
            {
                continue;
            }

            Rematches.TryRemove(accountId, out _);
            var room = ById.Values.FirstOrDefault(r => r.RematchId == rematch.RoomKey);
            if (room is null)
            {
                lock (Sync)
                {
                    room = ById.Values.FirstOrDefault(r => r.RematchId == rematch.RoomKey) ?? CreateFor(player, rematch);
                }
            }
            else
            {
                await JoinRematchAsync(room, player, rematch).ConfigureAwait(false);
            }
        }
    }

    private static MobaRoom CreateFor(Player host, Rematch rematch)
    {
        var room = new MobaRoom
        {
            Id = ++nextId,
            Name = rematch.Name,
            TeamSize = rematch.TeamSize,
            MidOnly = rematch.MidOnly,
            FillWithBots = rematch.Bots,
            Password = rematch.Password,
            RematchId = rematch.RoomKey,
            Host = host.Account!.GetId(),
        };
        room.Members[room.Host] = (host, rematch.Team);
        ById[room.Id] = room;
        RoomIdOfAccount[room.Host] = room.Id;
        _ = FinishRematchAsync(room, host);
        return room;
    }

    private static async Task FinishRematchAsync(MobaRoom room, Player host)
    {
        Track(host);
        await AddNoticeAsync(room, $"{host.SelectedCharacter?.Name} volvió a abrir la sala después de la partida.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
    }

    private static async ValueTask JoinRematchAsync(MobaRoom room, Player player, Rematch rematch)
    {
        var accountId = player.Account!.GetId();
        MobaTeam team;
        lock (Sync)
        {
            if (room.Members.Count >= 2 * room.TeamSize || RoomIdOfAccount.ContainsKey(accountId))
            {
                return;
            }

            team = room.TeamPlayers(rematch.Team).Count < room.TeamSize ? rematch.Team : (rematch.Team == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue);
            room.Members[accountId] = (player, team);
            RoomIdOfAccount[accountId] = room.Id;
        }

        Track(player);
        Touch(room);
        await AddNoticeAsync(room, $"{player.SelectedCharacter?.Name} volvió a la sala.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        await SendHistoryAsync(room, player).ConfigureAwait(false);
    }

    /// <summary>Runs a button of the host which acts on another player.</summary>
    /// <param name="host">The player who pressed it.</param>
    /// <param name="action">The button: 1 move to the other team, 2 kick, 3 make host.</param>
    /// <param name="targetName">The character name of the player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask HandleTargetActionAsync(Player host, byte action, string targetName)
    {
        var answer = action switch
        {
            1 => await MovePlayerAsync(host, targetName).ConfigureAwait(false),
            2 => await KickAsync(host, targetName).ConfigureAwait(false),
            3 => await MakeHostAsync(host, targetName).ConfigureAwait(false),
            _ => null,
        };

        if (answer is not null)
        {
            await host.ShowBlueMessageAsync("[Sala] " + answer).ConfigureAwait(false);
        }
    }

    /// <summary>Closes the rooms nobody has used for a while (every few seconds) and warns before it.</summary>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var room in ById.Values.ToList())
        {
            var idle = now - room.LastActivityUtc;
            if (room.Members.Count == 0)
            {
                Unregister(room);
            }
            else if (idle >= IdleLimit)
            {
                await CloseAsync(room, "La sala se cerró por inactividad.").ConfigureAwait(false);
            }
            else if (idle >= IdleWarning && !room.Warned)
            {
                room.Warned = true;
                await AddNoticeAsync(room, $"La sala se cerrará en {(int)(IdleLimit - idle).TotalMinutes + 1} min si nadie hace nada (escribe en el chat para mantenerla).", touch: false).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask<string> MoveToOtherTeamAsync(MobaRoom room, Guid accountId, string notice)
    {
        if (!room.Members.TryGetValue(accountId, out var member))
        {
            return "Ese jugador ya no está en la sala.";
        }

        var other = member.Team == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;
        lock (Sync)
        {
            if (room.TeamPlayers(other).Count >= room.TeamSize)
            {
                return $"El equipo {TeamName(other)} está lleno.";
            }

            room.Members[accountId] = (member.Session, other);
        }

        Touch(room);
        await AddNoticeAsync(room, notice).ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
        return $"Ahora {(member.Session.SelectedCharacter?.Name)} está en el equipo {TeamName(other)}.";
    }

    private static async ValueTask RemoveMemberAsync(MobaRoom room, Player player, string what)
    {
        var accountId = player.Account!.GetId();
        Player? newHost = null;
        lock (Sync)
        {
            room.Members.Remove(accountId);
            room.Ready.Remove(accountId);
            RoomIdOfAccount.TryRemove(accountId, out _);
            if (room.Members.Count == 0)
            {
                ById.TryRemove(room.Id, out _);
            }
            else if (room.Host == accountId)
            {
                var next = room.Members.First();
                room.Host = next.Key;
                newHost = next.Value.Session;
            }
        }

        Untrack(player);
        await player.InvokeViewPlugInAsync<IMobaRoomPlugIn>(p => p.ShowRoomAsync(null)).ConfigureAwait(false);
        var name = player.SelectedCharacter?.Name;
        if (room.Members.Count == 0)
        {
            return;
        }

        Touch(room);
        await AddNoticeAsync(room, newHost is null ? $"{name} {what}." : $"{name} {what}; {newHost.SelectedCharacter?.Name} es el nuevo anfitrión.").ConfigureAwait(false);
        await PublishAsync(room).ConfigureAwait(false);
    }

    private static async ValueTask CloseAsync(MobaRoom room, string reason)
    {
        var members = room.Members.Values.Select(m => m.Session).ToList();
        Unregister(room);
        foreach (var member in members)
        {
            Untrack(member);
            await member.InvokeViewPlugInAsync<IMobaRoomPlugIn>(p => p.ShowRoomAsync(null)).ConfigureAwait(false);
            await member.ShowBlueMessageAsync($"[Sala] {reason}").ConfigureAwait(false);
        }
    }

    private static void Unregister(MobaRoom room)
    {
        lock (Sync)
        {
            ById.TryRemove(room.Id, out _);
            foreach (var id in room.Members.Keys)
            {
                RoomIdOfAccount.TryRemove(id, out _);
            }
        }
    }

    private static void Touch(MobaRoom room)
    {
        room.LastActivityUtc = DateTime.UtcNow;
        room.Warned = false;
    }

    private static string? HostRoomOf(Player player, out MobaRoom? room)
    {
        room = RoomOf(player);
        if (room is null || player.Account is null)
        {
            return "No estás en ninguna sala.";
        }

        return room.Host == player.Account.GetId() ? null : "Solo el anfitrión de la sala puede hacer eso.";
    }

    private static KeyValuePair<Guid, (Player Session, MobaTeam Team)>? FindMember(MobaRoom room, string name)
    {
        foreach (var member in room.Members)
        {
            if (string.Equals(member.Value.Session.SelectedCharacter?.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>Removes control characters and cuts the text.</summary>
    private static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var clean = new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return clean.Length <= maxLength ? clean : clean[..maxLength];
    }

    private static void Remember(MobaRoom room, string name, string text, bool isNotice)
    {
        lock (room.Chat)
        {
            room.Chat.Add((name, text, isNotice));
            if (room.Chat.Count > MaxChatHistory)
            {
                room.Chat.RemoveAt(0);
            }
        }
    }

    private static async ValueTask AddNoticeAsync(MobaRoom room, string text, bool touch = true)
    {
        if (touch)
        {
            Touch(room);
        }

        Remember(room, string.Empty, text, true);
        foreach (var member in room.Members.Values.Select(m => m.Session).ToList())
        {
            await member.InvokeViewPlugInAsync<IMobaRoomChatPlugIn>(p => p.ShowChatLineAsync(string.Empty, text, true)).ConfigureAwait(false);
        }
    }

    private static async ValueTask SendHistoryAsync(MobaRoom room, Player player)
    {
        List<(string Name, string Text, bool IsNotice)> lines;
        lock (room.Chat)
        {
            lines = room.Chat.TakeLast(HistoryOnJoin).ToList();
        }

        foreach (var (name, text, isNotice) in lines)
        {
            await player.InvokeViewPlugInAsync<IMobaRoomChatPlugIn>(p => p.ShowChatLineAsync(name, text, isNotice)).ConfigureAwait(false);
        }
    }

    /// <summary>Sends the panel of the room to every member.</summary>
    private static async ValueTask PublishAsync(MobaRoom room)
    {
        foreach (var (id, (session, _)) in room.Members.ToList())
        {
            MobaRoomMember Of(KeyValuePair<Guid, (Player Session, MobaTeam Team)> m)
                => new(
                    m.Value.Session.SelectedCharacter?.Name ?? "?",
                    m.Key == room.Host,
                    m.Key == id,
                    MobaMinimapKind.BaseClassIndex(m.Value.Session.SelectedCharacter?.CharacterClass?.Number ?? 0),
                    m.Key == room.Host || room.Ready.Contains(m.Key),
                    MobaLoadoutStore.HasPick(m.Value.Session.SelectedCharacter?.Name ?? string.Empty));

            var blue = room.Members.Where(m => m.Value.Team == MobaTeam.Blue).Select(Of).ToList();
            var red = room.Members.Where(m => m.Value.Team == MobaTeam.Red).Select(Of).ToList();
            var info = new MobaRoomInfo(room.Id, room.Name, room.TeamSize, room.MidOnly, room.FillWithBots, id == room.Host, room.IsLocked, blue, red);
            await session.InvokeViewPlugInAsync<IMobaRoomPlugIn>(p => p.ShowRoomAsync(info)).ConfigureAwait(false);
        }
    }

    private static List<MobaQueueEntry> EntriesOf(MobaRoom room, MobaTeam team, DateTime now)
    {
        var entries = room.Members
            .Where(m => m.Value.Team == team)
            .Select(m => new MobaQueueEntry(MobaQueueMode.Open, [new MobaQueueMember(m.Key, m.Value.Session.SelectedCharacter?.Name ?? "?")], now, room.Format))
            .ToList();
        var missing = room.FillWithBots ? room.TeamSize - entries.Count : 0;
        if (missing > 0)
        {
            var bots = Enumerable.Range(1, missing).Select(i => new MobaQueueMember(Guid.NewGuid(), $"Bot {i}", IsBot: true)).ToList();
            entries.Add(new MobaQueueEntry(MobaQueueMode.WithBots, bots, now, room.Format));
        }

        return entries;
    }

    private static string? CheckPlayer(Player player, bool inRoom = false)
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

    private static void Track(Player player)
    {
        player.PlayerDisconnected -= OnDisconnectedAsync;
        player.PlayerDisconnected += OnDisconnectedAsync;
    }

    private static void Untrack(Player player) => player.PlayerDisconnected -= OnDisconnectedAsync;

    private static async ValueTask OnDisconnectedAsync(Player player) => await LeaveAsync(player).ConfigureAwait(false);
}
