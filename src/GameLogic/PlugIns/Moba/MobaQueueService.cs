// <copyright file="MobaQueueService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using MUnique.OpenMU.DataModel.Entities;
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

    /// <summary>Search accepting bots (alone, or with the party if there is one): humans first, bots after the wait of <see cref="MobaMatchmaker.BotFillStages"/>.</summary>
    WithBots = 2,

    /// <summary>Play against five bots at once (alone, or with the party if there is one).</summary>
    Coop = 3,

    /// <summary>The 3v3 search (alone, or with the party of up to three): humans only for the first 10 minutes, then bots complete it if there are at least four humans.</summary>
    Trio = 4,

    /// <summary>The 1v1 search: humans only, alone.</summary>
    Duel = 5,

    /// <summary>The ARAM search (5v5 in the middle lane, the side lanes closed): like the search with bots, alone or with the party.</summary>
    Aram = 6,
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

    /// <summary>The menu id of the client's queue panel (option 0 leaves the queue).</summary>
    public const byte PanelMenuId = 4;

    private static readonly ConcurrentDictionary<Guid, Player> Sessions = new();
    private static readonly ConcurrentDictionary<Guid, byte> Fillers = new();
    private static readonly ConcurrentDictionary<Guid, MobaQueueFailureReason> FailureReasons = new();
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
        var format = choice switch
        {
            MobaQueueChoice.Trio => MobaFormat.Trio,
            MobaQueueChoice.Duel => MobaFormat.Duel,
            MobaQueueChoice.Aram => MobaFormat.Aram,
            _ => MobaFormat.Classic,
        };
        var teamSize = MobaFormats.TeamSize(format);
        var wantsParty = choice == MobaQueueChoice.Party
            || (choice is MobaQueueChoice.WithBots or MobaQueueChoice.Coop or MobaQueueChoice.Trio or MobaQueueChoice.Aram && player.Party is { PartyList.Count: >= 2 });
        if (choice == MobaQueueChoice.Duel && player.Party is { PartyList.Count: >= 2 })
        {
            return "El 1v1 es individual: sal del party para buscarlo.";
        }
        if (!wantsParty)
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

            if (team.Count > teamSize)
            {
                return $"El party tiene más de {teamSize} jugadores.";
            }
        }

        mode = choice switch
        {
            MobaQueueChoice.WithBots or MobaQueueChoice.Trio or MobaQueueChoice.Aram => MobaQueueMode.WithBots,
            MobaQueueChoice.Coop => MobaQueueMode.Coop,
            MobaQueueChoice.Duel => MobaQueueMode.Open,
            _ => team.Count == MobaMatchmaker.TeamSize ? MobaQueueMode.Premade : MobaQueueMode.Open,
        };

        foreach (var member in team)
        {
            if (CheckMember(member, force) is { } problem)
            {
                return team.Count == 1 ? problem : $"{member.SelectedCharacter?.Name}: {problem}";
            }
        }

        MobaQueuePenalties.Bind(player.GameContext);
        foreach (var member in team)
        {
            if (await MobaQueuePenalties.GetBlockRemainingAsync(member.Account!.GetId()).ConfigureAwait(false) is { } left)
            {
                var duration = MobaQueuePenalties.FormatDuration(left);
                return team.Count == 1
                    ? $"Estás bloqueado de la cola por no aceptar partidas. Faltan {duration}."
                    : $"{member.SelectedCharacter?.Name} está bloqueado de la cola por no aceptar partidas. Faltan {duration}.";
            }
        }

        var members = team.Select(p => new MobaQueueMember(p.Account!.GetId(), p.SelectedCharacter!.Name)).ToList();
        var result = Queue.Join(mode, members, null, format);
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

        var what = team.Count == 1 ? "solo" : mode == MobaQueueMode.Premade ? "equipo de 5" : $"party de {team.Count}";
        if (format == MobaFormat.Trio)
        {
            return $"Buscando partida 3v3 ({what}). Se espera completar con humanos; si a los 10:00 hay 4 o más, se completa con bots. Hay {Queue.WaitingPlayers(mode, format)} jugador(es) esperando en esta cola.";
        }

        if (format == MobaFormat.Duel)
        {
            return $"Buscando partida 1v1 (carril central). Hay {Queue.WaitingPlayers(mode, format)} jugador(es) esperando en esta cola.";
        }

        if (format == MobaFormat.Aram)
        {
            return $"Buscando ARAM 5v5 por el carril central ({what}). Primero se intenta completar con humanos; si no hay, empieza igualmente con bots: {MobaMatchmaker.DescribeBotStages(format)}. Hay {Queue.WaitingPlayers(mode, format)} jugador(es) esperando en esta cola.";
        }

        return mode switch
        {
            MobaQueueMode.Coop => $"Partida cooperativa contra bots ({what}): empieza en unos segundos.",
            MobaQueueMode.WithBots => $"Buscando partida con bots ({what}). Primero se intenta completar con humanos; si no hay, la partida empieza igualmente con bots: {MobaMatchmaker.DescribeBotStages()}. Hay {Queue.WaitingPlayers(mode)} jugador(es) esperando en esta cola.",
            _ => $"Buscando partida ({what}). Hay {Queue.WaitingPlayers(mode)} jugador(es) esperando en tu cola.",
        };
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

        var duringReadyCheck = Queue.StatusOf(id) == MobaQueueStatus.ReadyCheck;
        if (duringReadyCheck)
        {
            FailureReasons[id] = MobaQueueFailureReason.LeftQueue;
        }

        Untrack(player);
        await HandleEventsAsync(Queue.Leave(id)).ConfigureAwait(false);
        await HideQueuePanelAsync(player).ConfigureAwait(false);
        return duringReadyCheck
            ? "Saliste de la cola con una partida encontrada: recibes una advertencia."
            : "Saliste de la cola.";
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

        var check = Queue.ReadyCheckOf(account.GetId());
        var alreadyAnswered = check?.AnswerOf(account.GetId()) != MobaReadyAnswer.Pending;
        var events = Queue.Answer(account.GetId(), accepted);
        await HandleEventsAsync(events).ConfigureAwait(false);
        if (events.Count == 0 && check is not null)
        {
            // Still waiting for the others: refresh the "N/10 accepted" of every popup.
            await SendReadyCheckStateAsync(check).ConfigureAwait(false);
        }

        if (accepted && !alreadyAnswered && await MobaQueuePenalties.RegisterAcceptanceAsync(account.GetId()).ConfigureAwait(false))
        {
            return "Aceptaste. Tus advertencias de la cola se borraron. Esperando a los demás...";
        }

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
        var withBots = Queue.WaitingPlayers(MobaQueueMode.WithBots);
        var own = player.Account is { } account ? Queue.StatusOf(account.GetId()) : MobaQueueStatus.None;
        var text = own switch
        {
            MobaQueueStatus.Queued => "Estás en la cola.",
            MobaQueueStatus.ReadyCheck => "Se encontró una partida: acepta o rechaza.",
            _ => "No estás en la cola.",
        };
        return $"{text} Esperando: {open} jugador(es) (solos y parties), {premade} (equipos de 5), {withBots} (con bots).{DescribeOwnWait(player)}";
    }

    /// <summary>
    /// Explains the search modes (for the "how do the modes work" option of the queue master).
    /// </summary>
    /// <returns>The text.</returns>
    public static string ModesHelp() => string.Join(" | ", ModesHelpLines());

    /// <summary>
    /// Explains the search modes, one line per mode.
    /// </summary>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> ModesHelpLines() =>
    [
        "Solo / party: 5v5 solo con humanos, tres carriles y jungla.",
        "Con bots: se completa con humanos si los hay; si no, empieza igualmente (" + MobaMatchmaker.DescribeBotStages() + ").",
        "Cooperativo: tú o tu party (hasta 5) contra 5 bots, al instante.",
        "3v3: tres carriles con jungla; solo humanos y, pasados 10 minutos, se completa con bots si hay 4 o más.",
        "1v1: carril central, solo humanos. ARAM: 5v5 por el carril central, como «con bots» (" + MobaMatchmaker.DescribeBotStages(MobaFormat.Aram) + ").",
    ];

    /// <summary>
    /// Updates the queue panel of every player who waits for a match (once a second, with the queue's clock).
    /// </summary>
    private static async ValueTask SendQueuePanelsAsync()
    {
        foreach (var (account, player) in Sessions)
        {
            var status = BuildQueueStatus(account);
            await player.InvokeViewPlugInAsync<Views.Moba.IMobaQueueStatusPlugIn>(p => p.ShowQueueStatusAsync(status)).ConfigureAwait(false);
        }
    }

    private static ValueTask HideQueuePanelAsync(Player player)
        => player.InvokeViewPlugInAsync<Views.Moba.IMobaQueueStatusPlugIn>(p => p.ShowQueueStatusAsync(Views.Moba.MobaQueueStatusInfo.Hidden));

    /// <summary>
    /// Builds what the client's queue panel shows for an account: nothing unless it waits in a pool.
    /// </summary>
    private static Views.Moba.MobaQueueStatusInfo BuildQueueStatus(Guid account)
    {
        if (Queue.EntryOf(account) is not { } entry)
        {
            return Views.Moba.MobaQueueStatusInfo.Hidden;
        }

        var mode = (entry.Format, entry.Mode) switch
        {
            (MobaFormat.Trio, MobaQueueMode.WithBots) => Views.Moba.MobaQueueStatusMode.Trio,
            (MobaFormat.Duel, MobaQueueMode.Open) => Views.Moba.MobaQueueStatusMode.Duel,
            (MobaFormat.Aram, MobaQueueMode.WithBots) => Views.Moba.MobaQueueStatusMode.Aram,
            (_, MobaQueueMode.Open) => Views.Moba.MobaQueueStatusMode.Open,
            (_, MobaQueueMode.Premade) => Views.Moba.MobaQueueStatusMode.Premade,
            (_, MobaQueueMode.WithBots) => Views.Moba.MobaQueueStatusMode.WithBots,
            _ => Views.Moba.MobaQueueStatusMode.Hidden,
        };
        if (mode == Views.Moba.MobaQueueStatusMode.Hidden)
        {
            return Views.Moba.MobaQueueStatusInfo.Hidden;
        }

        var wait = DateTime.UtcNow - entry.JoinedUtc;
        var botStages = MobaFormats.BotStages(entry.Format);
        var stages = botStages.Select(s => ((int)s.Wait.TotalSeconds, s.Humans)).ToList();
        var next = botStages.Where(s => s.Wait > wait).Select(s => ((TimeSpan Wait, int Humans)?)s).FirstOrDefault();
        return new Views.Moba.MobaQueueStatusInfo(
            mode,
            (int)wait.TotalSeconds,
            Queue.WaitingPlayers(entry.Mode, entry.Format),
            next is { } step ? (int)Math.Ceiling((step.Wait - wait).TotalSeconds) : null,
            next?.Humans ?? 0,
            MobaMatchmaker.HumansNeeded(wait, entry.Format) ?? 0,
            entry.Mode == MobaQueueMode.WithBots ? stages : []);
    }

    private static string DescribeOwnWait(Player player)
    {
        if (player.Account is not { } account || Queue.EntryOf(account.GetId()) is not { Mode: MobaQueueMode.WithBots } entry)
        {
            return string.Empty;
        }

        var wait = DateTime.UtcNow - entry.JoinedUtc;
        var next = MobaFormats.BotStages(entry.Format).Where(stage => stage.Wait > wait).Select(stage => ((TimeSpan Wait, int Humans)?)stage).FirstOrDefault();
        var waited = MobaQueuePenalties.FormatDuration(wait);
        return next is { } step
            ? $" Llevas {waited}; con {MobaQueuePenalties.FormatDuration(step.Wait - wait)} más empieza con {(step.Humans == 1 ? "los humanos que haya" : $"{step.Humans} o más humanos")}."
            : $" Llevas {waited}: la partida con bots empieza en cuanto se arme.";
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
    /// Takes every filler out of the queue (GM tests).
    /// </summary>
    /// <returns>The number of fillers removed.</returns>
    public static async ValueTask<int> ClearFillersAsync()
    {
        var removed = 0;
        foreach (var id in Fillers.Keys.ToList())
        {
            Fillers.TryRemove(id, out _);
            if (Queue.StatusOf(id) != MobaQueueStatus.None)
            {
                await HandleEventsAsync(Queue.Leave(id)).ConfigureAwait(false);
                removed++;
            }
        }

        return removed;
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
            await SendQueuePanelsAsync().ConfigureAwait(false);
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
            if (Queue.StatusOf(account.GetId()) == MobaQueueStatus.ReadyCheck)
            {
                FailureReasons[account.GetId()] = MobaQueueFailureReason.Disconnected;
            }

            Untrack(player);
            await HandleEventsAsync(Queue.Leave(account.GetId())).ConfigureAwait(false);
        }
    }

    private static async ValueTask OnReadyCheckStartedAsync(MobaReadyCheck check)
    {
        // The fillers answer first so that the popups open already showing how many accepted.
        foreach (var member in check.Proposal.Players.Where(m => IsFiller(m.AccountId)))
        {
            await HandleEventsAsync(Queue.Answer(member.AccountId, true)).ConfigureAwait(false);
        }

        if (check.State != MobaReadyState.Pending)
        {
            return;
        }

        // A cooperative match of a single human has nobody to wait for: no question, it starts at once.
        if (check.Proposal.Entries.FirstOrDefault(e => e.Mode == MobaQueueMode.Coop) is { } coop
            && coop.Members.Count(m => !m.IsBot && !IsFiller(m.AccountId)) == 1)
        {
            await HandleEventsAsync(Queue.Answer(coop.Members.First(m => !m.IsBot).AccountId, true)).ConfigureAwait(false);
            return;
        }

        var seconds = (int)MobaReadyCheck.Window.TotalSeconds;
        foreach (var member in check.Proposal.Players.Where(m => !IsFiller(m.AccountId)))
        {
            if (PlayerOf(member.AccountId) is { } player)
            {
                await player.ShowBlueMessageAsync($"[Cola] ¡Partida encontrada! Acepta en {seconds} s (botón del aviso, Intro, o /mobaaccept; /mobadecline para rechazar).").ConfigureAwait(false);
            }
        }

        await SendReadyCheckStateAsync(check).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows every connected player of the check its popup: asking, or "accepted, waiting" for who already accepted.
    /// </summary>
    private static async ValueTask SendReadyCheckStateAsync(MobaReadyCheck check)
    {
        var remaining = (int)Math.Max(0, (check.DeadlineUtc - DateTime.UtcNow).TotalMilliseconds);
        var window = (int)(check.DeadlineUtc - check.StartedUtc).TotalMilliseconds;
        var accepted = check.AcceptedCount;
        var total = check.PlayerCount;
        foreach (var member in check.Proposal.Players)
        {
            if (PlayerOf(member.AccountId) is not { } player)
            {
                continue;
            }

            var status = check.AnswerOf(member.AccountId) == MobaReadyAnswer.Accepted
                ? Views.Moba.MobaReadyCheckStatus.Accepted
                : Views.Moba.MobaReadyCheckStatus.Asking;
            await player.InvokeViewPlugInAsync<Views.Moba.IMobaReadyCheckPlugIn>(p => p.ShowReadyCheckAsync(status, remaining, window, accepted, total)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Closes the popup of every connected player of the check (it ended: the match starts or failed).
    /// </summary>
    private static async ValueTask HideReadyCheckAsync(MobaReadyCheck check)
    {
        foreach (var member in check.Proposal.Players)
        {
            if (PlayerOf(member.AccountId) is { } player)
            {
                await player.InvokeViewPlugInAsync<Views.Moba.IMobaReadyCheckPlugIn>(p => p.ShowReadyCheckAsync(Views.Moba.MobaReadyCheckStatus.Hidden, 0, 0, 0, 0)).ConfigureAwait(false);
            }
        }
    }

    private static async ValueTask OnReadyCheckFailedAsync(MobaQueueEvent queueEvent)
    {
        await HideReadyCheckAsync(queueEvent.Check).ConfigureAwait(false);
        var penalties = await PenalizeOffendersAsync(queueEvent.Check).ConfigureAwait(false);
        foreach (var entry in queueEvent.Dropped)
        {
            foreach (var member in entry.Members)
            {
                if (PlayerOf(member.AccountId) is not { } player)
                {
                    continue;
                }

                Untrack(player);
                await HideQueuePanelAsync(player).ConfigureAwait(false);
                var guilty = queueEvent.Check.Offenders.Contains(member.AccountId);
                await player.ShowBlueMessageAsync(guilty
                    ? DescribePenalty(penalties.GetValueOrDefault(member.AccountId))
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

    /// <summary>
    /// Gives a warning (and maybe a block) to every player who failed the ready check, and records the incident.
    /// </summary>
    private static async ValueTask<Dictionary<Guid, MobaPenaltyOutcome?>> PenalizeOffendersAsync(MobaReadyCheck check)
    {
        var outcomes = new Dictionary<Guid, MobaPenaltyOutcome?>();
        foreach (var account in check.Offenders.Where(id => !IsFiller(id)))
        {
            var name = check.Proposal.Players.FirstOrDefault(m => m.AccountId == account)?.Name ?? string.Empty;
            if (!FailureReasons.TryRemove(account, out var reason))
            {
                reason = check.AnswerOf(account) == MobaReadyAnswer.Declined ? MobaQueueFailureReason.Declined : MobaQueueFailureReason.TimedOut;
            }

            outcomes[account] = await MobaQueuePenalties.RegisterFailureAsync(account, name, reason).ConfigureAwait(false);
        }

        return outcomes;
    }

    private static string DescribePenalty(MobaPenaltyOutcome? outcome)
    {
        const string Prefix = "[Cola] No aceptaste la partida: sales de la cola. ";
        if (outcome is not { } result)
        {
            return Prefix + "Recibes una advertencia.";
        }

        return result.BlockedFor is { } block
            ? Prefix + $"Advertencias {result.Warnings}/{MobaQueuePenaltyRules.WarningsForBlock}: no podrás buscar partida durante {MobaQueuePenalties.FormatDuration(block)}."
            : Prefix + $"Advertencia {result.Warnings}/{MobaQueuePenaltyRules.WarningsForBlock}.";
    }

    private static async ValueTask OnMatchReadyAsync(MobaReadyCheck check)
    {
        await HideReadyCheckAsync(check).ConfigureAwait(false);
        var online = new Dictionary<Guid, Player>();
        foreach (var member in check.Proposal.Players)
        {
            if (PlayerOf(member.AccountId) is { } player)
            {
                online[member.AccountId] = player;
                Untrack(player);
                await HideQueuePanelAsync(player).ConfigureAwait(false);
            }
        }

        await MobaMatchLauncher.LaunchAsync(check.Proposal, online).ConfigureAwait(false);
    }
}
