// <copyright file="MobaQueueService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.Persistence;

/// <summary>
/// What a player asks the queue for.
/// </summary>
public enum MobaQueueChoice : byte
{
    /// <summary>Search alone.</summary>
    Solo = 0,

    /// <summary>Search with the whole party (2 to 4 players: the open pool; exactly 5: the premade pool).</summary>
    Party = 1,
}

/// <summary>
/// The host of the MOBA queue: validates who may queue, keeps track of the sessions of the queued players,
/// drives the queue's clock and tells the players what happens (match found, accepted, declined, ...).
/// The queue itself (<see cref="MobaQueue"/>) does no I/O; this is where it meets the game.
/// </summary>
public static class MobaQueueService
{
    /// <summary>The menu id of the queue master's menu.</summary>
    public const byte MenuId = 2;

    /// <summary>The menu id of the "match found" question.</summary>
    public const byte ReadyMenuId = 3;

    private static readonly ConcurrentDictionary<Guid, Player> Sessions = new();
    private static readonly ConcurrentDictionary<Guid, byte> Fillers = new();
    private static readonly SemaphoreSlim TickLock = new(1, 1);
    private static DateTime _lastTickUtc = DateTime.MinValue;

    /// <summary>Gets the queue (one per process: every game server feeds the same pools).</summary>
    public static MobaQueue Queue { get; } = new();

    /// <summary>
    /// Puts a player, or the player's party, into the queue.
    /// </summary>
    /// <param name="player">The player who asks (the party master when it is a party).</param>
    /// <param name="choice">What the player asks for.</param>
    /// <param name="force">Skips the level / Master requirement (GM tests).</param>
    /// <returns>What to tell the player.</returns>
    public static async ValueTask<string> JoinAsync(Player player, MobaQueueChoice choice, bool force = false)
    {
        if (player.Account is null || player.SelectedCharacter is null)
        {
            return "Primero entra al juego con un personaje.";
        }

        var team = new List<Player>();
        var mode = MobaQueueMode.Open;
        if (choice == MobaQueueChoice.Solo)
        {
            team.Add(player);
        }
        else
        {
            if (player.Party is not { } party || party.PartyList.Count < 2)
            {
                return "No tienes party: crea uno o busca partida solo.";
            }

            if (!ReferenceEquals(party.PartyMaster, player))
            {
                return "Solo el líder del party puede buscar partida.";
            }

            foreach (var member in party.PartyList)
            {
                if (member is not Player human || human is OfflinePlayer)
                {
                    return "Todos los miembros del party tienen que estar conectados.";
                }

                team.Add(human);
            }

            mode = team.Count == MobaMatchmaker.TeamSize ? MobaQueueMode.Premade : MobaQueueMode.Open;
            if (team.Count > MobaMatchmaker.TeamSize)
            {
                return "El party tiene más de 5 jugadores.";
            }
        }

        foreach (var member in team)
        {
            if (CheckMember(member, force) is { } problem)
            {
                return team.Count == 1 ? problem : $"{member.SelectedCharacter?.Name}: {problem}";
            }
        }

        var members = team.Select(p => new MobaQueueMember(p.Account!.GetId(), p.SelectedCharacter!.Name)).ToList();
        var result = Queue.Join(mode, members);
        if (result == MobaQueueJoinResult.AlreadyQueued)
        {
            return "Ya estás en la cola o en una partida encontrada. Sal de la cola primero.";
        }

        if (result != MobaQueueJoinResult.Joined)
        {
            return "No se pudo entrar a la cola: tamaño de grupo no válido.";
        }

        foreach (var member in team)
        {
            Track(member);
        }

        var what = mode == MobaQueueMode.Premade ? "equipo de 5" : team.Count > 1 ? $"party de {team.Count}" : "solo";
        return $"Buscando partida ({what}). Hay {Queue.WaitingPlayers(mode)} jugador(es) esperando en tu cola.";
    }

    /// <summary>
    /// Takes the player out of the queue (the whole party leaves). During a ready check this counts as declining.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>What to tell the player.</returns>
    public static async ValueTask<string> LeaveAsync(Player player)
    {
        if (player.Account is not { } account)
        {
            return "No estás en la cola.";
        }

        var id = account.GetId();
        if (Queue.StatusOf(id) == MobaQueueStatus.None)
        {
            return "No estás en la cola.";
        }

        Untrack(player);
        await HandleEventsAsync(Queue.Leave(id)).ConfigureAwait(false);
        return "Saliste de la cola.";
    }

    /// <summary>
    /// Records the answer of a player to the "match found" question.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="accepted">Whether the player accepts.</param>
    /// <returns>What to tell the player.</returns>
    public static async ValueTask<string> AnswerAsync(Player player, bool accepted)
    {
        if (player.Account is not { } account || Queue.StatusOf(account.GetId()) != MobaQueueStatus.ReadyCheck)
        {
            return "No hay ninguna partida esperando tu respuesta.";
        }

        await HandleEventsAsync(Queue.Answer(account.GetId(), accepted)).ConfigureAwait(false);
        return accepted ? "Aceptaste. Esperando a los demás..." : "Rechazaste la partida.";
    }

    /// <summary>
    /// Describes where the player stands.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The text.</returns>
    public static string StatusOf(Player player)
    {
        var open = Queue.WaitingPlayers(MobaQueueMode.Open);
        var premade = Queue.WaitingPlayers(MobaQueueMode.Premade);
        var own = player.Account is { } account ? Queue.StatusOf(account.GetId()) : MobaQueueStatus.None;
        var text = own switch
        {
            MobaQueueStatus.Queued => "Estás en la cola.",
            MobaQueueStatus.ReadyCheck => "Se encontró una partida: acepta o rechaza.",
            _ => "No estás en la cola.",
        };
        return $"{text} Esperando: {open} jugador(es) (solos y parties), {premade} (equipos de 5).";
    }

    /// <summary>
    /// Adds fake solo players to the queue; they accept every match on their own (GM tests).
    /// </summary>
    /// <param name="count">How many.</param>
    /// <returns>The number added.</returns>
    public static int AddFillers(int count)
    {
        var added = 0;
        for (var i = 0; i < count; i++)
        {
            var id = Guid.NewGuid();
            if (Queue.Join(MobaQueueMode.Open, [new MobaQueueMember(id, "Relleno" + (i + 1))]) == MobaQueueJoinResult.Joined)
            {
                Fillers[id] = 0;
                added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Gets whether an account is a filler (a fake player added with <see cref="AddFillers"/>).
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns><see langword="true"/> for a filler.</returns>
    public static bool IsFiller(Guid account) => Fillers.ContainsKey(account);

    /// <summary>
    /// Gets the session of a queued player.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>The player, or <see langword="null"/> if it is offline or a filler.</returns>
    public static Player? PlayerOf(Guid account) => Sessions.TryGetValue(account, out var player) ? player : null;

    /// <summary>
    /// Runs the queue's clock (about once a second, whichever game server calls it): forms matches and ends ready checks.
    /// </summary>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync()
    {
        if (DateTime.UtcNow - _lastTickUtc < TimeSpan.FromMilliseconds(900) || !await TickLock.WaitAsync(0).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            _lastTickUtc = DateTime.UtcNow;
            await HandleEventsAsync(Queue.Tick()).ConfigureAwait(false);
        }
        finally
        {
            TickLock.Release();
        }
    }

    /// <summary>
    /// Acts on what happened in the queue: asks the players to accept, tells the offenders and the others, starts matches.
    /// </summary>
    /// <param name="events">The events.</param>
    /// <returns>A task.</returns>
    public static async ValueTask HandleEventsAsync(IReadOnlyList<MobaQueueEvent> events)
    {
        foreach (var queueEvent in events)
        {
            switch (queueEvent.Kind)
            {
                case MobaQueueEventKind.ReadyCheckStarted:
                    await OnReadyCheckStartedAsync(queueEvent.Check).ConfigureAwait(false);
                    break;
                case MobaQueueEventKind.ReadyCheckFailed:
                    await OnReadyCheckFailedAsync(queueEvent).ConfigureAwait(false);
                    break;
                case MobaQueueEventKind.MatchReady:
                    await OnMatchReadyAsync(queueEvent.Check).ConfigureAwait(false);
                    break;
            }
        }
    }

    private static string? CheckMember(Player member, bool force)
    {
        if (member.Account is not { } account || member.SelectedCharacter is not { } character)
        {
            return "no está en el juego.";
        }

        if (member.IsMobaClone || MobaMatchRegistry.IsInMatch(account.GetId()))
        {
            return "ya está en una partida.";
        }

        return force ? null : MobaEligibility.Check(member, character) is { } reason ? reason : null;
    }

    private static void Track(Player player)
    {
        Sessions[player.Account!.GetId()] = player;
        player.PlayerDisconnected -= OnDisconnectedAsync;
        player.PlayerDisconnected += OnDisconnectedAsync;
    }

    private static void Untrack(Player player)
    {
        if (player.Account is { } account)
        {
            Sessions.TryRemove(account.GetId(), out _);
        }

        player.PlayerDisconnected -= OnDisconnectedAsync;
    }

    private static async ValueTask OnDisconnectedAsync(Player player)
    {
        if (player.Account is { } account && Queue.StatusOf(account.GetId()) != MobaQueueStatus.None)
        {
            Untrack(player);
            await HandleEventsAsync(Queue.Leave(account.GetId())).ConfigureAwait(false);
        }
    }

    private static async ValueTask OnReadyCheckStartedAsync(MobaReadyCheck check)
    {
        var seconds = (int)MobaReadyCheck.Window.TotalSeconds;
        foreach (var member in check.Proposal.Players)
        {
            if (IsFiller(member.AccountId))
            {
                await HandleEventsAsync(Queue.Answer(member.AccountId, true)).ConfigureAwait(false);
                continue;
            }

            if (PlayerOf(member.AccountId) is not { } player)
            {
                continue;
            }

            await player.ShowBlueMessageAsync($"[Cola] ¡Partida encontrada! Acepta en {seconds} s (menú o /mobaaccept; /mobadecline para rechazar).").ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<Views.Moba.IMobaShopPlugIn>(p => p.ShowMenuAsync(ReadyMenuId, "Partida encontrada", $"Tu partida está lista. Acepta en {seconds} segundos o saldrás de la cola.", ["Aceptar", "Rechazar"])).ConfigureAwait(false);
        }
    }

    private static async ValueTask OnReadyCheckFailedAsync(MobaQueueEvent queueEvent)
    {
        foreach (var entry in queueEvent.Dropped)
        {
            foreach (var member in entry.Members)
            {
                if (PlayerOf(member.AccountId) is not { } player)
                {
                    continue;
                }

                Untrack(player);
                var guilty = queueEvent.Check.Offenders.Contains(member.AccountId);
                await player.ShowBlueMessageAsync(guilty
                    ? "[Cola] No aceptaste la partida: sales de la cola y recibes una advertencia."
                    : "[Cola] Alguien de tu party no aceptó la partida: sales de la cola.").ConfigureAwait(false);
            }
        }

        foreach (var entry in queueEvent.Requeued)
        {
            foreach (var member in entry.Members)
            {
                if (PlayerOf(member.AccountId) is { } player)
                {
                    await player.ShowBlueMessageAsync("[Cola] Un jugador no aceptó la partida; sigues en la cola y buscamos reemplazo.").ConfigureAwait(false);
                }
            }
        }
    }

    private static async ValueTask OnMatchReadyAsync(MobaReadyCheck check)
    {
        var online = new Dictionary<Guid, Player>();
        foreach (var member in check.Proposal.Players)
        {
            if (PlayerOf(member.AccountId) is { } player)
            {
                online[member.AccountId] = player;
                Untrack(player);
            }
        }

        await MobaMatchLauncher.LaunchAsync(check.Proposal, online).ConfigureAwait(false);
    }
}
