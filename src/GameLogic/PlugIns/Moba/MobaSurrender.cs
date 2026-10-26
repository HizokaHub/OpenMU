// <copyright file="MobaSurrender.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// The surrender vote of a team (<c>/rendirse</c>): after <see cref="MinimumMinutes"/> of match any human of a team can propose to give up, the others
/// vote with the same command and, when <see cref="RequiredVotes"/> agree within <see cref="VoteSeconds"/>, the team's nexus falls and the match ends.
/// A vote that does not pass cannot be repeated for <see cref="RetrySeconds"/>. Bots do not vote (a team without humans cannot surrender).
/// </summary>
public static class MobaSurrender
{
    /// <summary>Minutes of match before a team may surrender.</summary>
    public const int MinimumMinutes = 15;

    /// <summary>Seconds a vote stays open.</summary>
    public const int VoteSeconds = 60;

    /// <summary>Seconds after a vote began before the same team may begin another one.</summary>
    public const int RetrySeconds = 120;

    /// <summary>Share of the humans of the team whose votes make it surrender (rounded up).</summary>
    public const double RequiredShare = 0.75;

    private static readonly ConditionalWeakTable<GameMap, Dictionary<MobaTeam, Vote>> Votes = new();

    /// <summary>How many votes a team of <paramref name="humans"/> humans needs to surrender.</summary>
    /// <param name="humans">The humans of the team.</param>
    /// <returns>The votes needed (at least 1).</returns>
    public static int RequiredVotes(int humans) => Math.Max(1, (int)Math.Ceiling(humans * RequiredShare));

    /// <summary>Registers the vote of a champion (it opens the vote when none is open) and ends the match when enough agree.</summary>
    /// <param name="player">The champion who voted.</param>
    /// <returns>A task.</returns>
    public static async ValueTask VoteAsync(Player player)
    {
        if (!player.IsMobaClone || player is MobaBotPlayer || player.CurrentMap is not { } map)
        {
            await player.ShowBlueMessageAsync("[MOBA] Solo puedes rendirte dentro de una partida.").ConfigureAwait(false);
            return;
        }

        var arena = MobaArena.Of(map);
        var team = MobaTeams.GetTeam(player);
        if (team == MobaTeam.None || arena.IsPreMatch || arena.Elapsed.TotalMinutes < MinimumMinutes)
        {
            await player.ShowBlueMessageAsync($"[MOBA] Solo puedes rendirte con la partida en marcha y pasados {MinimumMinutes} minutos.").ConfigureAwait(false);
            return;
        }

        var humans = await HumansOfAsync(player, map, team).ConfigureAwait(false);
        var vote = OpenOrJoin(map, team, player, out var opened, out var coolingDown);
        if (coolingDown)
        {
            await player.ShowBlueMessageAsync($"[MOBA] Tu equipo acaba de votar; espera un poco para proponer otra rendición.").ConfigureAwait(false);
            return;
        }

        var needed = RequiredVotes(humans.Count);
        var text = opened
            ? $"[MOBA] {player.Name} propone rendirse ({vote.Voters.Count}/{needed}). Escribe /rendirse para votar a favor ({VoteSeconds} s)."
            : $"[MOBA] {player.Name} vota rendirse ({vote.Voters.Count}/{needed}).";
        foreach (var human in humans)
        {
            await human.ShowBlueMessageAsync(text).ConfigureAwait(false);
        }

        if (vote.Voters.Count >= needed)
        {
            Close(map, team);
            await MobaMatchEnder.EndMatchAsync(map, player.GameContext, team).ConfigureAwait(false);
        }
    }

    private static Vote OpenOrJoin(GameMap map, MobaTeam team, Player player, out bool opened, out bool coolingDown)
    {
        var byTeam = Votes.GetOrCreateValue(map);
        lock (byTeam)
        {
            var now = DateTime.UtcNow;
            opened = false;
            coolingDown = false;
            if (byTeam.TryGetValue(team, out var vote) && now < vote.StartedUtc.AddSeconds(VoteSeconds))
            {
                vote.Voters.Add(player);
                return vote;
            }

            if (vote is not null && now < vote.StartedUtc.AddSeconds(RetrySeconds))
            {
                coolingDown = true;
                return vote;
            }

            vote = new Vote { StartedUtc = now };
            vote.Voters.Add(player);
            byTeam[team] = vote;
            opened = true;
            return vote;
        }
    }

    private static void Close(GameMap map, MobaTeam team)
    {
        var byTeam = Votes.GetOrCreateValue(map);
        lock (byTeam)
        {
            byTeam.Remove(team);
        }
    }

    private static async ValueTask<List<Player>> HumansOfAsync(Player player, GameMap map, MobaTeam team)
    {
        var all = await player.GameContext.GetPlayersAsync().ConfigureAwait(false);
        return all.Where(p => p.IsMobaClone && p is not MobaBotPlayer && ReferenceEquals(p.CurrentMap, map) && MobaTeams.GetTeam(p) == team).ToList();
    }

    private sealed class Vote
    {
        public DateTime StartedUtc;

        public HashSet<Player> Voters { get; } = new();
    }
}
