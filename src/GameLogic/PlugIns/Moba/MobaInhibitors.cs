// <copyright file="MobaInhibitors.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The inhibitors of a match: one per lane and team, behind the base turret of the lane. The chain of a lane is outer turret, middle turret, base turret,
/// inhibitor. A fallen inhibitor (a) opens the nexus of its team to attack for as long as it stays down, (b) makes the enemy team send super-minions down that
/// lane with every wave (<see cref="MobaSuperMinions"/>) and (c) comes back after <see cref="RespawnSeconds"/>. This class holds the state and the rules;
/// <see cref="MobaStructureSpawner"/> creates the monsters.
/// </summary>
public static class MobaInhibitors
{
    /// <summary>Seconds an inhibitor stays down.</summary>
    public const int RespawnSeconds = 300;

    /// <summary>Seconds before the respawn at which the owner team is warned.</summary>
    public const int WarningSeconds = 30;

    /// <summary>Gold each champion of the attacking team gets when an inhibitor falls.</summary>
    public const int KillGold = 100;

    /// <summary>EXP the attacking team gets when an inhibitor falls (the same as a turret).</summary>
    public const int KillExp = MobaLevels.TurretKillExp;

    /// <summary>Lanes a team has in the full arena: with all of its inhibitors down the enemy sends two super-minions per wave.</summary>
    public const int FullArenaLanes = 3;

    /// <summary>Super-minions per wave in a lane whose inhibitor is down.</summary>
    public const int SuperMinionsPerWave = 1;

    /// <summary>Super-minions per wave in every lane when all the inhibitors of a full arena are down.</summary>
    public const int SuperMinionsWhenAllDown = 2;

    private static readonly ConcurrentDictionary<GameMap, List<Entry>> ByMap = new();

    /// <summary>The state of one inhibitor.</summary>
    public sealed class Entry
    {
        /// <summary>Initializes a new instance of the <see cref="Entry"/> class.</summary>
        /// <param name="team">The team that owns it.</param>
        /// <param name="lane">The lane index.</param>
        /// <param name="position">Where it stands.</param>
        public Entry(MobaTeam team, int lane, Point position)
        {
            this.Team = team;
            this.Lane = lane;
            this.Position = position;
        }

        /// <summary>Gets the team that owns the inhibitor.</summary>
        public MobaTeam Team { get; }

        /// <summary>Gets the lane index.</summary>
        public int Lane { get; }

        /// <summary>Gets where the inhibitor stands.</summary>
        public Point Position { get; }

        /// <summary>Gets or sets the monster that is the inhibitor now (the last one spawned).</summary>
        public Monster? Monster { get; set; }

        /// <summary>Gets or sets when it comes back; <c>null</c> while it stands.</summary>
        public DateTime? DownUntilUtc { get; set; }

        /// <summary>Gets or sets the token that cancels the pending respawn.</summary>
        public CancellationTokenSource? Respawn { get; set; }

        /// <summary>Gets a value indicating whether the inhibitor is down.</summary>
        public bool IsDown => this.DownUntilUtc is not null;

        /// <summary>Gets or sets a value indicating whether the super-minions of this down period were announced.</summary>
        public bool SuperMinionsAnnounced { get; set; }
    }

    /// <summary>The number of super-minions a team sends down a lane with one wave.</summary>
    /// <param name="inhibitorsOfEnemy">How many inhibitors the enemy team has in the arena (3 in the full arena, 1 in the single-lane formats).</param>
    /// <param name="enemyInhibitorsDown">How many of them are down.</param>
    /// <param name="laneInhibitorDown">Whether the enemy inhibitor of this lane is down.</param>
    /// <returns>0, 1 or 2.</returns>
    public static int SuperMinionCount(int inhibitorsOfEnemy, int enemyInhibitorsDown, bool laneInhibitorDown)
    {
        if (!laneInhibitorDown)
        {
            return 0;
        }

        return inhibitorsOfEnemy >= FullArenaLanes && enemyInhibitorsDown >= inhibitorsOfEnemy ? SuperMinionsWhenAllDown : SuperMinionsPerWave;
    }

    /// <summary>Whether the nexus of a team is shielded (it cannot be damaged).</summary>
    /// <param name="teamHasInhibitors">Whether the team has inhibitors in this match.</param>
    /// <param name="anyInhibitorDown">Whether one of them is down.</param>
    /// <param name="legacyLaneFallen">The old rule: a whole lane of its turrets has fallen (used when there are no inhibitors, e.g. the test commands).</param>
    /// <returns><c>true</c> while the nexus is protected.</returns>
    public static bool NexusShielded(bool teamHasInhibitors, bool anyInhibitorDown, bool legacyLaneFallen)
        => teamHasInhibitors ? !anyInhibitorDown : !legacyLaneFallen;

    /// <summary>Registers an inhibitor of the map (or returns the one already registered for that team and lane).</summary>
    /// <param name="map">The map.</param>
    /// <param name="team">The team.</param>
    /// <param name="lane">The lane index.</param>
    /// <param name="position">Where it stands.</param>
    /// <returns>The entry.</returns>
    public static Entry Register(GameMap map, MobaTeam team, int lane, Point position)
    {
        var list = ByMap.GetOrAdd(map, _ => new List<Entry>());
        lock (list)
        {
            var existing = list.Find(e => e.Team == team && e.Lane == lane);
            if (existing is not null)
            {
                return existing;
            }

            var entry = new Entry(team, lane, position);
            list.Add(entry);
            return entry;
        }
    }

    /// <summary>Removes the inhibitors of a map from the registry and cancels their respawns.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The monsters of the inhibitors (the caller takes them off the map).</returns>
    public static IReadOnlyList<Monster> Clear(GameMap map)
    {
        if (!ByMap.TryRemove(map, out var list))
        {
            return Array.Empty<Monster>();
        }

        lock (list)
        {
            foreach (var entry in list)
            {
                entry.Respawn?.Cancel();
            }

            return list.Select(e => e.Monster).OfType<Monster>().ToList();
        }
    }

    /// <summary>Gets a copy of the entries of a map.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The entries.</returns>
    public static IReadOnlyList<Entry> EntriesOf(GameMap map)
    {
        if (!ByMap.TryGetValue(map, out var list))
        {
            return Array.Empty<Entry>();
        }

        lock (list)
        {
            return list.ToArray();
        }
    }

    /// <summary>Whether the map has inhibitors.</summary>
    /// <param name="map">The map.</param>
    /// <returns><c>true</c> if some are registered.</returns>
    public static bool HasAny(GameMap map) => EntriesOf(map).Count > 0;

    /// <summary>Whether a team has inhibitors on the map.</summary>
    /// <param name="map">The map.</param>
    /// <param name="team">The team.</param>
    /// <returns><c>true</c> if it has.</returns>
    public static bool Has(GameMap map, MobaTeam team) => EntriesOf(map).Any(e => e.Team == team);

    /// <summary>Whether at least one inhibitor of a team is down.</summary>
    /// <param name="map">The map.</param>
    /// <param name="team">The team.</param>
    /// <returns><c>true</c> if one is down.</returns>
    public static bool AnyDown(GameMap map, MobaTeam team) => EntriesOf(map).Any(e => e.Team == team && e.IsDown);

    /// <summary>Whether the inhibitor of a team in a lane is down.</summary>
    /// <param name="map">The map.</param>
    /// <param name="team">The team.</param>
    /// <param name="lane">The lane index.</param>
    /// <returns><c>true</c> if it is down.</returns>
    public static bool IsDown(GameMap map, MobaTeam team, int lane) => EntriesOf(map).Any(e => e.Team == team && e.Lane == lane && e.IsDown);

    /// <summary>The super-minions that <paramref name="attackingTeam"/> sends down a lane with its next wave (the inhibitors that count are the enemy's).</summary>
    /// <param name="map">The map.</param>
    /// <param name="attackingTeam">The team whose wave is spawning.</param>
    /// <param name="lane">The lane index of the wave.</param>
    /// <returns>0, 1 or 2.</returns>
    public static int SuperMinionsFor(GameMap map, MobaTeam attackingTeam, int lane)
    {
        var enemy = attackingTeam == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;
        var enemyEntries = EntriesOf(map).Where(e => e.Team == enemy).ToList();
        return SuperMinionCount(enemyEntries.Count, enemyEntries.Count(e => e.IsDown), enemyEntries.Any(e => e.Lane == lane && e.IsDown));
    }

    /// <summary>The champions of a team on the map (humans and bots).</summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="map">The map.</param>
    /// <param name="team">The team.</param>
    /// <returns>The champions.</returns>
    public static async ValueTask<IReadOnlyList<Player>> ChampionsOfAsync(IGameContext gameContext, GameMap map, MobaTeam team)
        => (await gameContext.GetPlayersAsync().ConfigureAwait(false))
            .Concat(MobaBotPlayer.All)
            .Distinct()
            .Where(p => p.IsMobaClone && ReferenceEquals(p.CurrentMap, map) && MobaTeams.GetTeam(p) == team)
            .ToList();

    /// <summary>Announces the super-minions of a lane the first time they are sent while its enemy inhibitor stays down.</summary>
    /// <param name="map">The map.</param>
    /// <param name="attackingTeam">The team that sends them.</param>
    /// <param name="lane">The lane index.</param>
    /// <returns>A task.</returns>
    public static async ValueTask AnnounceSuperMinionsOnceAsync(GameMap map, MobaTeam attackingTeam, int lane)
    {
        var enemy = attackingTeam == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;
        var entry = EntriesOf(map).FirstOrDefault(e => e.Team == enemy && e.Lane == lane && e.IsDown);
        if (entry is null || entry.SuperMinionsAnnounced)
        {
            return;
        }

        entry.SuperMinionsAnnounced = true;
        await MobaAnnouncer.AnnounceAsync(map, MobaAnnouncement.SuperMinions, (byte)attackingTeam).ConfigureAwait(false);
    }

    /// <summary>The fallen inhibitors, for the minimap: team, position and the seconds left before they come back.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The fallen ones.</returns>
    public static IReadOnlyList<(MobaTeam Team, Point Position, int SecondsLeft)> DownList(GameMap map)
        => EntriesOf(map)
            .Where(e => e.DownUntilUtc is not null)
            .Select(e => (e.Team, e.Position, Math.Max(0, (int)Math.Ceiling((e.DownUntilUtc!.Value - DateTime.UtcNow).TotalSeconds))))
            .ToList();
}
