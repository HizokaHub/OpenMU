// <copyright file="MobaMatchLauncher.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Starts a match the queue formed: gives it its own arena (a copy of the arena map), makes the clone of every
/// player and registers it, fills the empty places with bots, and sends the players into the match. A player who has
/// not come back in time is replaced by a ghost, so the team is never a player short.
/// </summary>
public static class MobaMatchLauncher
{
    /// <summary>How long a player has to enter the match before its ghost takes its place.</summary>
    public static readonly TimeSpan JoinGrace = TimeSpan.FromSeconds(90);

    /// <summary>The pause between the "match ready" message and the disconnection which sends the player to the match.</summary>
    private static readonly TimeSpan SendOffDelay = TimeSpan.FromSeconds(3);

    /// <summary>The lanes the bot fillers of a team take, in order (a team of three has one player per lane).</summary>
    private static readonly int[] FillerLanes = { MobaLayout.MidLane, 0, 2, 1, 0 };

    /// <summary>
    /// Starts the match.
    /// </summary>
    /// <param name="proposal">The teams.</param>
    /// <param name="online">The sessions of the human players (fillers are not in it).</param>
    /// <returns>A task.</returns>
    public static async ValueTask LaunchAsync(MobaMatchProposal proposal, IReadOnlyDictionary<Guid, Player> online)
    {
        var host = online.Values.FirstOrDefault();
        if (host is null)
        {
            return;
        }

        var context = host.GameContext;
        var logger = context.LoggerFactory.CreateLogger(typeof(MobaMatchLauncher));
        try
        {
            var arena = await context.CreateInstanceMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false);
            if (arena is null)
            {
                await TellAllAsync(online.Values, "[Cola] No se pudo crear la arena. Inténtalo de nuevo.").ConfigureAwait(false);
                return;
            }

            // The format first (the arena boot and the countdown read it), then the countdown: while it runs the arena is held
            // (barrier around the bases, no bots, jungle or waves).
            MobaArena.Of(arena).Format = proposal.Format;
            MobaPreMatch.Begin(arena, context);
            await MobaArenaBoot.EnsureAsync(arena, context).ConfigureAwait(false);

            var sendOff = new List<Player>();
            foreach (var (team, entries) in new[] { (MobaTeam.Blue, proposal.Blue), (MobaTeam.Red, proposal.Red) })
            {
                var fillers = 0;
                foreach (var member in entries.SelectMany(e => e.Members))
                {
                    if (online.TryGetValue(member.AccountId, out var player))
                    {
                        await ArmAsync(player, team, arena).ConfigureAwait(false);
                        sendOff.Add(player);
                    }
                    else
                    {
                        fillers++;
                    }
                }

                if (fillers > 0)
                {
                    await SpawnFillersAsync(host, team, arena, fillers).ConfigureAwait(false);
                }
            }

            logger.LogInformation(
                "[MOBA-QUEUE] {Format} match started on arena {Arena}: {Blue} blue / {Red} red, {Humans} human(s).",
                MobaFormats.Label(proposal.Format),
                arena.Id.ToString()[..8],
                proposal.Blue.Sum(e => e.Size),
                proposal.Red.Sum(e => e.Size),
                sendOff.Count);

            _ = SendOffAsync(sendOff, arena, context);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MOBA-QUEUE] the match could not be started.");
            await TellAllAsync(online.Values, "[Cola] Error al iniciar la partida. Inténtalo de nuevo.").ConfigureAwait(false);
        }
    }

    /// <summary>Makes the clone of a player and registers it for the match, on the team and arena given.</summary>
    private static async ValueTask ArmAsync(Player player, MobaTeam team, GameMap arena)
    {
        var real = player.SelectedCharacter!;
        var loadout = MobaLoadoutStore.SelectedFor(real.Name, real.CharacterClass?.Number ?? 0);
        var clone = await MobaCloneFactory.BuildCloneAsync(player, real, loadout).ConfigureAwait(false);

        // The clone is created through this session's persistence context: detach it and suppress this session's save.
        MobaCloneFactory.DetachClone(player, clone);
        player.SuppressPersistence = true;
        var spot = MobaLayout.NearestWalkable(MobaLayout.ShopOf(team).X, MobaLayout.ShopOf(team).Y);
        clone.PositionX = (byte)spot.X;
        clone.PositionY = (byte)spot.Y;

        var accountId = player.Account!.GetId();
        MobaMatchRegistry.Enter(accountId, clone);
        if (MobaMatchRegistry.TryGetState(accountId, out var state) && state is not null)
        {
            state.Team = team;
            state.Arena = arena;
        }
    }

    private static async ValueTask SpawnFillersAsync(Player host, MobaTeam team, GameMap arena, int count)
    {
        var families = MobaBotChatCommandPlugIn.AllFamilies.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
        var lanes = MobaFormats.MiddleLaneOnly(MobaArena.Of(arena).Format)
            ? Enumerable.Repeat(MobaLayout.MidLane, count).ToList()
            : FillerLanes.Take(count).ToList();
        await MobaBotChatCommandPlugIn.SpawnAsync(host, team, families, lanes, arena).ConfigureAwait(false);
    }

    private static async Task SendOffAsync(List<Player> players, GameMap arena, IGameContext context)
    {
        try
        {
            await TellAllAsync(players, "[Cola] ¡Partida lista! Vuelve a entrar con el mismo personaje (el cliente se desconecta en unos segundos).").ConfigureAwait(false);
            await Task.Delay(SendOffDelay).ConfigureAwait(false);
            foreach (var player in players)
            {
                await player.DisconnectAsync().ConfigureAwait(false);
            }

            await Task.Delay(JoinGrace).ConfigureAwait(false);
            await PutGhostsForTheAbsentAsync(players, arena, context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.LoggerFactory.CreateLogger(typeof(MobaMatchLauncher)).LogError(ex, "[MOBA-QUEUE] error while sending the players to the match.");
        }
    }

    /// <summary>After the grace time every player who did not come back is stood in by its ghost.</summary>
    private static async Task PutGhostsForTheAbsentAsync(IEnumerable<Player> players, GameMap arena, IGameContext context)
    {
        foreach (var player in players)
        {
            if (player.Account is not { } account
                || !MobaMatchRegistry.TryGetState(account.GetId(), out var state)
                || state is null
                || !ReferenceEquals(state.Arena, arena)
                || state.Placed
                || state.Ghost is not null
                || !MobaMatchRegistry.TryGetClone(account.GetId(), out var clone)
                || clone is null)
            {
                continue;
            }

            var handover = new MobaGhostPlayer.Handover(account, clone, null, state, null, true);
            await MobaGhostPlayer.SpawnAsync(context, handover).ConfigureAwait(false);
        }
    }

    private static async ValueTask TellAllAsync(IEnumerable<Player> players, string message)
    {
        foreach (var player in players)
        {
            await player.ShowBlueMessageAsync(message).ConfigureAwait(false);
        }
    }
}
