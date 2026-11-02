// <copyright file="MobaCommanderQueue.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The waiting zone of the commanders mode (the Direct Strike way of playing): the packs the commanders buy are not sent
/// at once, they appear lined up in the base of their team, held in place, and the whole zone is released together when the
/// wave timer of the arena runs out (or when the human commander of a team uses "send now" for his own team).
/// </summary>
/// <remarks>
/// <para>
/// One queue per arena. It holds the wave timer, the cooldown of "send now" of each team and the packs which wait. What waits
/// is read from the living held creeps (a pack counts while at least one of its creeps is alive), never from separate counters.
/// </para>
/// <para>
/// The hold of <see cref="MobaCreepHold"/> is timed, so <see cref="Renew"/> has to run every second while creeps wait.
/// </para>
/// </remarks>
public sealed class MobaCommanderQueue
{
    /// <summary>Seconds between two waves: the timer is common to both teams and runs from the start of the economy.</summary>
    public const int WaveIntervalSeconds = 48;

    /// <summary>Seconds the human commander has to wait before he can use "send now" again.</summary>
    public const int SendNowCooldownSeconds = 8;

    /// <summary>The most packs a team can have waiting in one lane.</summary>
    public const int MaxPacksPerLane = 6;

    /// <summary>How long one renewal of the hold lasts (the tick renews it every second, this is the margin for a late tick).</summary>
    public static readonly TimeSpan HoldDuration = TimeSpan.FromSeconds(6);

    /// <summary>Packs side by side across the lane in the waiting zone.</summary>
    public const int WaitingColumns = 3;

    /// <summary>Tiles between two columns of packs (across the lane).</summary>
    public const int WaitingColumnGap = 4;

    /// <summary>Tiles between two rows of packs (along the lane).</summary>
    public const int WaitingRowGap = 5;

    /// <summary>
    /// Distance along the lane, from its first waypoint, of the first row of the zone. The zone sits on the road of the lane
    /// inside the base, clear of the nexus and the base turrets; the later rows are nearer the nexus.
    /// </summary>
    public const int WaitingFirstRowArc = 20;

    private const int RankGap = 2;

    private const int CreepGap = 2;

    private const int SearchRadius = 6;

    // How far a creep may be from its place in the zone and still count as waiting (a late tick that let the hold run out for a moment).
    private const int DriftTiles = 2;

    private static readonly ConditionalWeakTable<MobaArena, MobaCommanderQueue> Queues = new();

    private readonly object _sync = new();

    private readonly List<HeldPack> _packs = [];

    private readonly int[] _sendNowCooldown = new int[2];

    private bool _running;

    private int _waveSeconds;

    private int _nextPackId = 1;

    /// <summary>Gets the seconds left until the next wave (0 while the economy does not run).</summary>
    public int WaveSeconds
    {
        get
        {
            lock (this._sync)
            {
                return this._running ? this._waveSeconds : 0;
            }
        }
    }

    /// <summary>
    /// Gets the queue of an arena.
    /// </summary>
    /// <param name="arena">The arena.</param>
    /// <returns>The queue.</returns>
    public static MobaCommanderQueue Of(MobaArena arena) => Queues.GetValue(arena, _ => new MobaCommanderQueue());

    /// <summary>
    /// Starts the timer: the first wave comes <see cref="WaveIntervalSeconds"/> seconds from now; nothing waits.
    /// </summary>
    public void Begin()
    {
        lock (this._sync)
        {
            // Whatever waited from an earlier run (the mode was started again without stopping it) is let go, never left orphaned.
            this.ReleaseLocked(MobaTeam.None);
            this._packs.Clear();
            this._running = true;
            this._waveSeconds = WaveIntervalSeconds;
            Array.Clear(this._sendNowCooldown);
        }
    }

    /// <summary>
    /// Stops the timer and lets go of whatever waits (the match ended, or the mode was left).
    /// </summary>
    public void Reset()
    {
        lock (this._sync)
        {
            this.ReleaseLocked(MobaTeam.None);
            this._packs.Clear();
            this._running = false;
            this._waveSeconds = 0;
            Array.Clear(this._sendNowCooldown);
        }
    }

    /// <summary>
    /// Lets time pass: the wave timer and the cooldowns of "send now" go down.
    /// </summary>
    /// <param name="seconds">The seconds that passed.</param>
    /// <returns><see langword="true"/> if the timer ran out (the caller releases the waves); the timer starts again by itself.</returns>
    public bool Advance(int seconds)
    {
        lock (this._sync)
        {
            if (!this._running)
            {
                return false;
            }

            for (var i = 0; i < this._sendNowCooldown.Length; i++)
            {
                this._sendNowCooldown[i] = Math.Max(0, this._sendNowCooldown[i] - seconds);
            }

            this._waveSeconds -= seconds;
            if (this._waveSeconds > 0)
            {
                return false;
            }

            this._waveSeconds = WaveIntervalSeconds;
            return true;
        }
    }

    /// <summary>
    /// Gets the seconds a team has to wait before "send now" works again.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The seconds (0 when it is ready).</returns>
    public int SendNowCooldown(MobaTeam team)
    {
        lock (this._sync)
        {
            return this._sendNowCooldown[IndexOf(team)];
        }
    }

    /// <summary>
    /// Starts the cooldown of "send now" of a team, if it is ready.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns><see langword="false"/> if the cooldown was still running (nothing changed).</returns>
    public bool TryStartSendNowCooldown(MobaTeam team)
    {
        lock (this._sync)
        {
            ref var cooldown = ref this._sendNowCooldown[IndexOf(team)];
            if (cooldown > 0)
            {
                return false;
            }

            cooldown = SendNowCooldownSeconds;
            return true;
        }
    }

    /// <summary>
    /// Reserves the place of a new pack in the waiting zone of a lane.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="lane">The lane.</param>
    /// <param name="unitIndex">The index of the pack in <see cref="MobaCommanderUnits.All"/>.</param>
    /// <returns>The reserved pack, or <see langword="null"/> if the zone of that lane is full.</returns>
    public HeldPack? TryReservePack(MobaTeam team, int lane, int unitIndex)
    {
        lock (this._sync)
        {
            this.Prune();
            var used = this._packs.Where(p => p.Team == team && p.Lane == lane).Select(p => p.Slot).ToHashSet();
            var slot = Enumerable.Range(0, MaxPacksPerLane).FirstOrDefault(s => !used.Contains(s), -1);
            if (slot < 0)
            {
                return null;
            }

            var pack = new HeldPack(this._nextPackId++, team, lane, unitIndex, slot);
            this._packs.Add(pack);
            return pack;
        }
    }

    /// <summary>
    /// Registers a creep which was spawned held as part of a reserved pack.
    /// </summary>
    /// <param name="pack">The pack.</param>
    /// <param name="creep">The creep.</param>
    public void Register(HeldPack pack, Monster creep)
    {
        lock (this._sync)
        {
            pack.Creeps.Add(creep);
            pack.Spots[creep] = creep.Position;
            if (pack.Released)
            {
                // The wave (or "send now") came while the pack was still being spawned: the rest of its creeps go with it.
                MobaCreepHold.Release(creep);
            }
        }
    }

    /// <summary>
    /// Closes the reservation of a pack: its creeps are in. A pack without a living creep disappears.
    /// </summary>
    /// <param name="pack">The pack.</param>
    public void Complete(HeldPack pack)
    {
        lock (this._sync)
        {
            pack.Spawning = false;
            this.Prune();
        }
    }

    /// <summary>
    /// Gets the packs which wait for a team, one count per lane and pack (lane-major, <paramref name="unitCount"/> packs per lane).
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="unitCount">The number of different packs.</param>
    /// <returns>The counts: lane 0 pack 0, lane 0 pack 1, ..., lane 1 pack 0, ...</returns>
    public IReadOnlyList<int> Queued(MobaTeam team, int unitCount)
    {
        var counts = new int[MobaLayout.LaneCount * unitCount];
        lock (this._sync)
        {
            this.Prune();
            foreach (var pack in this._packs.Where(p => p.Team == team && !p.Spawning && !p.Released && p.UnitIndex >= 0 && p.UnitIndex < unitCount && p.Lane is >= 0 and < MobaLayout.LaneCount))
            {
                counts[(pack.Lane * unitCount) + pack.UnitIndex]++;
            }
        }

        return counts;
    }

    /// <summary>
    /// Gets how many packs wait for a team (in one lane, or in all of them).
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="lane">The lane, or -1 for all.</param>
    /// <returns>The number of packs.</returns>
    public int PacksWaiting(MobaTeam team, int lane = -1)
    {
        lock (this._sync)
        {
            this.Prune();
            return this._packs.Count(p => !p.Released && p.Team == team && (lane < 0 || p.Lane == lane));
        }
    }

    /// <summary>
    /// Holds the waiting creeps for another <see cref="HoldDuration"/> (call it every second). A creep which is no longer near its
    /// place in the zone (its hold ran out because a tick was very late, and it already walked off) is not caught again in mid-lane:
    /// it is dropped from its pack and goes on with its wave.
    /// </summary>
    public void Renew()
    {
        // Under the lock, so a release which runs at the same time can never be followed by a hold of the creeps it just let go.
        lock (this._sync)
        {
            this.Prune();
            foreach (var pack in this._packs.Where(p => !p.Released))
            {
                foreach (var creep in pack.Creeps.Where(IsAlive).ToList())
                {
                    if (pack.Spots.TryGetValue(creep, out var spot) && !MobaCreepHold.IsHeld(creep) && !IsNear(creep, spot))
                    {
                        pack.Creeps.Remove(creep);
                        continue;
                    }

                    MobaCreepHold.Hold(creep, HoldDuration);
                }
            }

            this.Prune();
        }
    }

    /// <summary>
    /// Lets the waiting creeps of a team go (they walk their lane like the creeps of any wave).
    /// </summary>
    /// <param name="team">The team, or <see cref="MobaTeam.None"/> for both.</param>
    /// <returns>The number of creeps released.</returns>
    public int Release(MobaTeam team = MobaTeam.None)
    {
        lock (this._sync)
        {
            this.Prune();
            return this.ReleaseLocked(team);
        }
    }

    /// <summary>
    /// Gets where the creeps of a pack stand in the waiting zone: on the road of the lane inside the team's base, in a grid of
    /// <see cref="WaitingColumns"/> columns, each pack in a block of its own; a second pack is behind or beside the first one.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="laneIndex">The lane.</param>
    /// <param name="slot">The place of the pack in the zone (0 to <see cref="MaxPacksPerLane"/> - 1).</param>
    /// <param name="composition">The ranks of the pack, front first.</param>
    /// <param name="isFree">Whether a tile can take a creep (walkable, not a structure, nobody there).</param>
    /// <returns>One tile for each creep of the pack, rank by rank; never the same tile twice.</returns>
    public static IReadOnlyList<Point> WaitingSpots(MobaTeam team, int laneIndex, int slot, IReadOnlyList<MobaWaveUnit> composition, Func<int, int, bool> isFree)
        => PlanWaitingSpots(team, laneIndex, slot, composition, isFree).OfType<Point>().ToList();

    /// <summary>
    /// Like <see cref="WaitingSpots"/>, but keeps one entry for each creep of the pack, in order: <see langword="null"/> for a creep
    /// which found no free tile near its place (the zone is full or blocked there), which the spawner leaves out instead of putting
    /// it on a tile where it could not walk.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="laneIndex">The lane.</param>
    /// <param name="slot">The place of the pack in the zone.</param>
    /// <param name="composition">The ranks of the pack, front first.</param>
    /// <param name="isFree">Whether a tile can take a creep (walkable, not a structure, nobody there).</param>
    /// <returns>One entry for each creep of the pack, rank by rank.</returns>
    public static IReadOnlyList<Point?> PlanWaitingSpots(MobaTeam team, int laneIndex, int slot, IReadOnlyList<MobaWaveUnit> composition, Func<int, int, bool> isFree)
    {
        var lane = MobaLayout.WaypointsFor(team, laneIndex);
        var safeSlot = Math.Clamp(slot, 0, WaitingColumns * 4);
        var row = safeSlot / WaitingColumns;
        var column = safeSlot % WaitingColumns;
        var centreArc = Math.Max(RankGap * 3, WaitingFirstRowArc - (row * WaitingRowGap));
        var (cx, cy, dx, dy) = MobaLayout.PointAtArc(lane, centreArc);
        var across = column switch
        {
            0 => 0,
            1 => -WaitingColumnGap,
            _ => WaitingColumnGap,
        };

        var spots = new List<Point?>();
        var used = new HashSet<(int, int)>();
        var rank = 0;
        foreach (var unit in composition)
        {
            var width = (unit.Count - 1) * CreepGap;
            for (var i = 0; i < unit.Count; i++)
            {
                var offset = across + (i * CreepGap) - (width / 2.0);
                var spot = NearestFree(
                    cx - (dx * rank * RankGap) - (dy * offset),
                    cy - (dy * rank * RankGap) + (dx * offset),
                    isFree,
                    used);
                if (spot is { } found)
                {
                    used.Add((found.X, found.Y));
                }

                spots.Add(spot);
            }

            rank++;
        }

        return spots;
    }

    /// <summary>
    /// Gets the index of the first waypoint of a lane which a creep standing at a spot still has to walk to (the ones behind it are skipped).
    /// </summary>
    /// <param name="lane">The waypoints of the lane in the marching order of the team.</param>
    /// <param name="spot">Where the creep stands.</param>
    /// <returns>The index into <paramref name="lane"/>.</returns>
    public static int FirstWaypointAhead(IReadOnlyList<Point> lane, Point spot)
    {
        var arc = MobaLayout.ArcOf(lane, spot.X, spot.Y);
        double walked = 0;
        for (var i = 1; i < lane.Count; i++)
        {
            walked += Math.Sqrt(Math.Pow(lane[i].X - lane[i - 1].X, 2) + Math.Pow(lane[i].Y - lane[i - 1].Y, 2));
            if (walked > arc + 1.5)
            {
                return i;
            }
        }

        return Math.Max(0, lane.Count - 1);
    }

    private static Point? NearestFree(double x, double y, Func<int, int, bool> isFree, HashSet<(int, int)> used)
    {
        int cx = (int)Math.Clamp(Math.Round(x), 0, 255), cy = (int)Math.Clamp(Math.Round(y), 0, 255);
        Point? best = null;
        var bestDistance = double.MaxValue;
        for (var radius = 0; radius <= SearchRadius && best is null; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx is < 0 or > 255 || ny is < 0 or > 255 || Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius
                        || used.Contains((nx, ny)) || !isFree(nx, ny))
                    {
                        continue;
                    }

                    var distance = Math.Pow(nx - x, 2) + Math.Pow(ny - y, 2);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new Point((byte)nx, (byte)ny);
                    }
                }
            }
        }

        return best;
    }

    private static bool IsNear(Monster creep, Point spot)
        => Math.Abs(creep.Position.X - spot.X) <= DriftTiles && Math.Abs(creep.Position.Y - spot.Y) <= DriftTiles;

    private static int IndexOf(MobaTeam team) => team == MobaTeam.Red ? 1 : 0;

    private static bool IsAlive(Monster creep) => creep.IsAlive && creep.CurrentMap is not null;

    // Must be called with the lock held.
    private void Prune() => this._packs.RemoveAll(p => !p.Spawning && (p.Released || !p.Creeps.Any(IsAlive)));

    // Must be called with the lock held. A pack which is still being spawned stays in the list until its spawn ends (it keeps its
    // place and its late creeps are released by Register); every other pack leaves the list.
    private int ReleaseLocked(MobaTeam team)
    {
        var count = 0;
        foreach (var pack in this._packs.Where(p => team == MobaTeam.None || p.Team == team).ToList())
        {
            foreach (var creep in pack.Creeps.Where(IsAlive))
            {
                MobaCreepHold.Release(creep);
                count++;
            }

            pack.Released = true;
            if (!pack.Spawning)
            {
                this._packs.Remove(pack);
            }
        }

        return count;
    }

    /// <summary>A pack which waits in the zone of a lane.</summary>
    public sealed class HeldPack
    {
        internal HeldPack(int id, MobaTeam team, int lane, int unitIndex, int slot)
        {
            this.Id = id;
            this.Team = team;
            this.Lane = lane;
            this.UnitIndex = unitIndex;
            this.Slot = slot;
        }

        /// <summary>Gets the number of the pack.</summary>
        public int Id { get; }

        /// <summary>Gets the team which bought it.</summary>
        public MobaTeam Team { get; }

        /// <summary>Gets the lane it waits in.</summary>
        public int Lane { get; }

        /// <summary>Gets the index of the pack in <see cref="MobaCommanderUnits.All"/>.</summary>
        public int UnitIndex { get; }

        /// <summary>Gets its place in the zone of the lane.</summary>
        public int Slot { get; }

        /// <summary>Gets the creeps of the pack.</summary>
        public List<Monster> Creeps { get; } = [];

        /// <summary>Gets or sets a value indicating whether the creeps are still being spawned (the place is reserved).</summary>
        internal bool Spawning { get; set; } = true;

        /// <summary>Gets or sets a value indicating whether the pack was let go (the creeps which are still being spawned join the wave).</summary>
        internal bool Released { get; set; }

        /// <summary>Gets where each creep of the pack was put in the zone.</summary>
        internal Dictionary<Monster, Point> Spots { get; } = [];
    }
}
