// <copyright file="MobaAnnouncer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The announcer of the MOBA: it watches the champion kills, the structures and the match, decides what is worth shouting (first blood,
/// multi-kills, aces, shutdowns, kill streaks, fallen turrets, a nexus under attack, the start and the end) and tells every human of the
/// arena through <see cref="IMobaAnnouncerPlugIn"/>. The clients play the voice and write the kill feed; this class owns the rules only.
/// </summary>
public static class MobaAnnouncer
{
    /// <summary>Seconds within which consecutive kills of the same champion count as one multi-kill.</summary>
    public const double MultiKillWindowSeconds = 10;

    /// <summary>The smallest team for which an ace (the whole team dead) is announced.</summary>
    public const int AceMinimumTeamSize = 3;

    /// <summary>Seconds between two "nexus under attack" warnings of the same nexus.</summary>
    public const double NexusWarningSeconds = 30;

    /// <summary>Kills in a row, without dying, that earn an announcement: the streak and what is said at it.</summary>
    private static readonly (int Streak, MobaAnnouncement Kind)[] StreakSteps =
    {
        (9, MobaAnnouncement.Legendary),
        (7, MobaAnnouncement.Unstoppable),
        (5, MobaAnnouncement.Dominating),
        (3, MobaAnnouncement.KillingSpree),
    };

    private static readonly Point ArenaCentre = new(128, 128);

    private static readonly ConditionalWeakTable<Player, ChampionRecord> Records = new();

    private static readonly ConditionalWeakTable<GameMap, MatchRecord> Matches = new();

    private static readonly ConditionalWeakTable<IAttackable, NexusRecord> Nexuses = new();

    /// <summary>
    /// Notes that a champion died (to anything): its streak ends, remembering how long it was for the shutdown. Call before the kill is noted.
    /// </summary>
    /// <param name="victim">The champion that died.</param>
    public static void NoteDeath(Player victim)
    {
        var record = Records.GetOrCreateValue(victim);
        record.StreakBeforeDeath = record.Streak;
        record.Streak = 0;
        record.MultiKills = 0;
    }

    /// <summary>Decides and tells what a champion kill deserves: the kill feed line, the loud announcement and the ace.</summary>
    /// <param name="map">The arena map.</param>
    /// <param name="killer">The champion that got the kill.</param>
    /// <param name="victim">The champion that died (after <see cref="NoteDeath"/>).</param>
    /// <returns>A task.</returns>
    public static async ValueTask NoteChampionKillAsync(GameMap map, Player killer, Player victim)
    {
        var match = MatchOf(map);
        var record = Records.GetOrCreateValue(killer);
        var now = DateTime.UtcNow;
        record.MultiKills = (now - record.LastKillUtc).TotalSeconds <= MultiKillWindowSeconds ? record.MultiKills + 1 : 1;
        record.LastKillUtc = now;
        record.Streak++;

        var kind = KindOfKill(match, record, Records.GetOrCreateValue(victim).StreakBeforeDeath);
        var killerTeam = (byte)MobaTeams.GetTeam(killer);
        await AnnounceAsync(map, kind, killerTeam, killer.Name, victim.Name).ConfigureAwait(false);

        if (IsAce(map, victim))
        {
            await AnnounceAsync(map, MobaAnnouncement.Ace, killerTeam, killer.Name, string.Empty).ConfigureAwait(false);
        }
    }

    /// <summary>Tells every human of the map an event.</summary>
    /// <param name="map">The arena map.</param>
    /// <param name="kind">What happened.</param>
    /// <param name="team">The team the event is about.</param>
    /// <param name="killer">The champion that did it, or empty.</param>
    /// <param name="victim">The champion that suffered it, or empty.</param>
    /// <returns>A task.</returns>
    public static async ValueTask AnnounceAsync(GameMap map, MobaAnnouncement kind, byte team, string killer = "", string victim = "")
    {
        foreach (var player in HumansOf(map))
        {
            await AnnounceToAsync(player, kind, team, killer, victim).ConfigureAwait(false);
        }
    }

    /// <summary>Tells one player an event.</summary>
    /// <param name="player">The player.</param>
    /// <param name="kind">What happened.</param>
    /// <param name="team">The team the event is about.</param>
    /// <param name="killer">The champion that did it, or empty.</param>
    /// <param name="victim">The champion that suffered it, or empty.</param>
    /// <returns>A task.</returns>
    public static ValueTask AnnounceToAsync(Player player, MobaAnnouncement kind, byte team, string killer = "", string victim = "")
        => player.InvokeViewPlugInAsync<IMobaAnnouncerPlugIn>(p => p.AnnounceAsync(kind, team, killer, victim));

    /// <summary>Warns the team whose nexus is losing life (once every <see cref="NexusWarningSeconds"/>). Call from the arena tick.</summary>
    /// <param name="map">The arena map.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(GameMap map)
    {
        foreach (var nexus in map.GetAttackablesInRange(ArenaCentre, 255).OfType<AttackableNpcBase>())
        {
            if (!nexus.IsAlive || MobaStructures.GetStructureType(nexus) != MobaStructureType.Nexus)
            {
                continue;
            }

            var record = Nexuses.GetOrCreateValue(nexus);
            var health = nexus.Health;
            var hurt = record.LastHealth > 0 && health < record.LastHealth;
            record.LastHealth = health;
            if (!hurt || (DateTime.UtcNow - record.LastWarningUtc).TotalSeconds < NexusWarningSeconds)
            {
                continue;
            }

            record.LastWarningUtc = DateTime.UtcNow;
            await AnnounceAsync(map, MobaAnnouncement.NexusUnderAttack, (byte)MobaTeams.GetTeam(nexus)).ConfigureAwait(false);
        }
    }

    private static MobaAnnouncement KindOfKill(MatchRecord match, ChampionRecord killer, int victimStreak)
    {
        if (!match.FirstBloodDone)
        {
            match.FirstBloodDone = true;
            return MobaAnnouncement.FirstBlood;
        }

        if (killer.MultiKills >= 2)
        {
            return killer.MultiKills switch
            {
                2 => MobaAnnouncement.DoubleKill,
                3 => MobaAnnouncement.TripleKill,
                4 => MobaAnnouncement.QuadraKill,
                _ => MobaAnnouncement.PentaKill,
            };
        }

        if (victimStreak >= MobaGold.ShutdownStreakThreshold)
        {
            return MobaAnnouncement.Shutdown;
        }

        foreach (var (streak, kind) in StreakSteps)
        {
            if (killer.Streak == streak)
            {
                return kind;
            }
        }

        return MobaAnnouncement.Kill;
    }

    private static bool IsAce(GameMap map, Player victim)
    {
        var team = MobaTeams.GetTeam(victim);
        if (team == MobaTeam.None)
        {
            return false;
        }

        var teammates = ChampionsOf(map).Where(p => MobaTeams.GetTeam(p) == team).ToList();
        return teammates.Count >= AceMinimumTeamSize && teammates.All(p => ReferenceEquals(p, victim) || !p.IsAlive);
    }

    /// <summary>The match record of the arena; a new match (a new start time) starts a clean one.</summary>
    private static MatchRecord MatchOf(GameMap map)
    {
        var record = Matches.GetOrCreateValue(map);
        var start = MobaArena.Of(map).MatchStartUtc;
        if (record.MatchStartUtc != start)
        {
            record.MatchStartUtc = start;
            record.FirstBloodDone = false;
        }

        return record;
    }

    private static IEnumerable<Player> ChampionsOf(GameMap map)
        => map.GetAttackablesInRange(ArenaCentre, 400).OfType<Player>().Where(p => p.IsMobaClone);

    private static IEnumerable<Player> HumansOf(GameMap map)
        => map.GetAttackablesInRange(ArenaCentre, 400).OfType<Player>().Where(p => p is not MobaBotPlayer).ToList();

    private sealed class ChampionRecord
    {
        public int Streak;

        public int StreakBeforeDeath;

        public int MultiKills;

        public DateTime LastKillUtc = DateTime.MinValue;
    }

    private sealed class MatchRecord
    {
        public DateTime MatchStartUtc = DateTime.MinValue;

        public bool FirstBloodDone;
    }

    private sealed class NexusRecord
    {
        public double LastHealth;

        public DateTime LastWarningUtc = DateTime.MinValue;
    }
}
