// <copyright file="MobaLaneCreepIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Buffers;
using System.Threading;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Self-contained AI for a MOBA lane creep: it marches its lane and fights enemies of
/// its <see cref="MobaTeam"/> along the way.
/// </summary>
/// <remarks>
/// The base <see cref="BasicMonsterIntelligence"/> is kept idle
/// (<see cref="SearchNextTargetAsync"/> returns null) - everything runs on one
/// dedicated timer, started by the spawner so creeps march and fight even when no
/// player is watching (the base AI only starts on the first observer).
///
/// W2 slice (this version): teams + the passive part of the LoL priority - nearest
/// enemy creep, then nearest enemy champion, then nearest enemy structure - plus a
/// simple chase leash. Reactive rules (#1-#6), the champion-aggro interrupt and the
/// hard structure lock come with the combat-events ledger next.
/// </remarks>
public sealed class MobaLaneCreepIntelligence : BasicMonsterIntelligence
{
    private const float WaypointReachedDistance = 1.5f;

    /// <summary>
    /// Tiles walked per march chunk. Deliberately short: a creep that has just committed
    /// to a long walk cannot react, so with a big chunk two waves marching at each other
    /// walk clean through the meeting point before the fight starts and then have to walk
    /// back. Keep this below <see cref="AcquisitionRangeBonus"/> so a creep notices the
    /// enemy wave and stops before it can overshoot.
    /// </summary>
    private const int MaxStepsPerChunk = 4;

    private const int RefeedWhenStepsLeft = 1;

    /// <summary>Tiles added to the creep's attack range to "notice" and walk toward an enemy.</summary>
    private const int AcquisitionRangeBonus = 6;

    /// <summary>How far off its lane the creep will stray chasing a target before giving up.</summary>
    private const double ChaseLeashTiles = 10;

    /// <summary>
    /// Radius (tiles) in which the champion-aggro rule (#1) looks for the enemy champion.
    /// Wider than the normal acquisition range: once an allied champion near the creep is
    /// in champion-vs-champion combat, the creep should switch onto the enemy champion even
    /// if that champion is attacking from range (a caster / archer well outside the tiny
    /// creep acquisition range). The allied champion itself still has to be in normal range
    /// for the creep to "care".
    /// </summary>
    private const int ChampAggroRevealTiles = 16;

    /// <summary>The creep is considered "back on its lane" once this close to it.</summary>
    private const double BackOnLaneTiles = 3;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(150);

    private readonly IReadOnlyList<Point> _waypoints;

    private readonly MobaTeam _team;

    private Timer? _aiTimer;

    private volatile bool _ticking;

    private int _currentWaypoint;

    private DateTime _chunkStartedUtc;

    private int _chunkStepCount;

    private IAttackable? _combatTarget;

    /// <summary>
    /// True while <see cref="_combatTarget"/> is an enemy champion picked by the #1
    /// champ-aggro rule (rather than normal acquisition). Such a target is force-dropped
    /// as soon as the aggro expires, so the creep goes straight back to the enemy wave.
    /// </summary>
    private bool _combatTargetFromChampAggro;

    /// <summary>The target the creep last fed a walk chunk toward, so a fresh acquisition interrupts an in-progress march but an ongoing chase does not re-path every tick.</summary>
    private IAttackable? _lastChaseTarget;

    /// <summary>Earliest UTC time the creep may attack again (attack pacing on the fast AI timer).</summary>
    private DateTime _nextAttackUtc;

    private bool _returningToLane;

    private Point _engageAnchor;

    private bool _hasEngageAnchor;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaLaneCreepIntelligence"/> class.
    /// </summary>
    /// <param name="waypoints">The ordered lane waypoints (already offset for this creep's parallel track).</param>
    /// <param name="team">The creep's team.</param>
    public MobaLaneCreepIntelligence(IReadOnlyList<Point> waypoints, MobaTeam team)
    {
        this._waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
        this._team = team;
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        base.OnStart();
        MobaTeams.Set(this.Monster, this._team);
        this._aiTimer ??= new Timer(_ => this.SafeTick(), null, TickInterval, TickInterval);
    }

    /// <inheritdoc />
    /// <remarks>The base AI stays idle; this creep runs its own timer.</remarks>
    protected override ValueTask<IAttackable?> SearchNextTargetAsync() => ValueTask.FromResult<IAttackable?>(null);

    /// <inheritdoc />
    protected override ValueTask TickWithoutTargetAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    protected override void Dispose(bool managed)
    {
        this._aiTimer?.Dispose();
        this._aiTimer = null;
        base.Dispose(managed);
    }

    private static Direction GetDirection(Point from, Point to)
    {
        var dx = Math.Sign(to.X - from.X);
        var dy = Math.Sign(to.Y - from.Y);
        return (dx, dy) switch
        {
            (0, -1) => Direction.North,
            (1, -1) => Direction.NorthEast,
            (1, 0) => Direction.East,
            (1, 1) => Direction.SouthEast,
            (0, 1) => Direction.South,
            (-1, 1) => Direction.SouthWest,
            (-1, 0) => Direction.West,
            (-1, -1) => Direction.NorthWest,
            _ => Direction.Undefined,
        };
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Timer callback; exceptions swallowed to keep the timer alive.")]
    private async void SafeTick()
    {
        if (this._ticking)
        {
            return;
        }

        this._ticking = true;
        try
        {
            await this.TickAsync().ConfigureAwait(false);
        }
        catch
        {
            // keep the timer alive
        }
        finally
        {
            this._ticking = false;
        }
    }

    private async ValueTask TickAsync()
    {
        var monster = this.Monster;
        if (!monster.IsAlive)
        {
            return;
        }

        if (monster.Attributes[Stats.IsStunned] > 0 || monster.Attributes[Stats.IsAsleep] > 0 || monster.Attributes[Stats.IsFrozen] > 0)
        {
            return;
        }

        var pos = monster.Position;
        var attackRange = monster.Definition.AttackRange;
        var acquisitionRange = attackRange + AcquisitionRangeBonus;

        // Returning to the spot where the last fight started: ignore enemies, walk back.
        if (this._returningToLane)
        {
            if (pos.EuclideanDistanceTo(this._engageAnchor) <= BackOnLaneTiles)
            {
                this._returningToLane = false;
                this._hasEngageAnchor = false;
            }
            else
            {
                if (!monster.IsWalking)
                {
                    await this.FeedChunkTowardAsync(pos, this._engageAnchor).ConfigureAwait(false);
                }

                return;
            }
        }

        // Strayed too far from where this engagement started (or off the lane axis):
        // drop the target and head back.
        if (this._hasEngageAnchor
            && (pos.EuclideanDistanceTo(this._engageAnchor) > ChaseLeashTiles
                || this.DistanceToLane(pos) > ChaseLeashTiles))
        {
            this._combatTarget = null;
            this._combatTargetFromChampAggro = false;
            this._returningToLane = true;
            return;
        }

        // Drop an invalid / out-of-range target. A champion target (set by the #1
        // champ-aggro rule) is kept out to the wider reveal radius so the creep can
        // actually close on a ranged poker; the leash check above still yanks it back
        // once it has strayed too far from where the fight started.
        var dropRange = this._combatTarget is Player ? ChampAggroRevealTiles : acquisitionRange;
        if (this._combatTarget is { } current
            && (!current.IsActive() || current.GetDistanceTo(pos) > dropRange))
        {
            this._combatTarget = null;
            this._combatTargetFromChampAggro = false;
        }

        var lockedOnStructure = this._combatTarget is Monster m && IsStructure(m);

        // #1 champion-aggro: an enemy champion that just damaged an allied champion. While
        // it keeps hitting, the creep stays on it; the moment it stops (no hit within
        // ChampAggroWindow) the creep drops it here and re-acquires a wave target below.
        if (!lockedOnStructure)
        {
            var aggroChamp = this.FindChampionAggro(monster, pos, acquisitionRange);
            if (aggroChamp is not null)
            {
                this._combatTarget = aggroChamp;
                this._combatTargetFromChampAggro = true;
            }
            else if (this._combatTargetFromChampAggro)
            {
                this._combatTarget = null;
                this._combatTargetFromChampAggro = false;
            }
        }

        if (this._combatTarget is null && this.AcquireTarget(monster, pos, acquisitionRange) is { } acquired)
        {
            this._combatTarget = acquired;
            this._combatTargetFromChampAggro = false;
        }

        if (this._combatTarget is not null && !this._hasEngageAnchor)
        {
            this._engageAnchor = pos;
            this._hasEngageAnchor = true;
        }

        // Anti-stack: if we've come to rest on a tile another unit also holds, step off it
        // (staying in attack range of the current target when possible).
        if (!monster.IsWalking && this.TileSharedWithOther(pos))
        {
            await this.StepOffSharedTileAsync(pos, this._combatTarget, attackRange).ConfigureAwait(false);
            return;
        }

        if (this._combatTarget is { } target)
        {
            if (target.GetDistanceTo(pos) <= attackRange)
            {
                if (monster.IsWalking)
                {
                    await monster.StopWalkingAsync().ConfigureAwait(false);
                }

                this._lastChaseTarget = null;

                // Pace attacks: the fast AI timer would otherwise call AttackAsync every
                // tick (Monster.AttackAsync has no cooldown of its own).
                if (DateTime.UtcNow >= this._nextAttackUtc)
                {
                    var delay = monster.Definition.AttackDelay;
                    this._nextAttackUtc = DateTime.UtcNow + (delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1500));
                    await monster.AttackAsync(target).ConfigureAwait(false);
                }
            }
            else if (!monster.IsWalking || !ReferenceEquals(target, this._lastChaseTarget))
            {
                // Fresh target while mid-march -> cut the march chunk short and head at
                // it now, so two waves meeting engage on contact instead of walking past.
                await this.FeedChunkTowardAsync(pos, target.Position).ConfigureAwait(false);
                this._lastChaseTarget = target;
            }

            return;
        }

        // No target: march the lane (also the path back to it when returning).
        this._lastChaseTarget = null;
        await this.MarchAsync(pos).ConfigureAwait(false);
    }

    private static readonly TimeSpan ReactWindow = TimeSpan.FromSeconds(2);

    /// <summary>
    /// How recently an enemy champion must have damaged an allied champion for the #1
    /// rule to keep this creep on that champion. Once the enemy champion stops (no hit
    /// within this window) the creep drops it and returns to the enemy wave.
    /// </summary>
    private static readonly TimeSpan ChampAggroWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// #1 of the LoL priority: an enemy champion that has damaged one of this creep's
    /// allied champions in the last <see cref="ChampAggroWindow"/>. Only that direction -
    /// the wave defends its champion; it does NOT pile onto an enemy champion just because
    /// an allied champion is attacking it (your own minions don't help you engage). Damage
    /// to creeps never triggers this: a player may last-hit freely. Force-switch interrupt
    /// unless the creep is locked on a structure.
    /// </summary>
    private Player? FindChampionAggro(Monster self, Point pos, int range)
    {
        var map = self.CurrentMap;
        if (map is null)
        {
            return null;
        }

        // The allied champion must be near the creep (normal acquisition range) for the
        // creep to react on its behalf...
        var alliedChampions = map.GetAttackablesInRange(pos, range)
            .Where(a => a.IsActive() && !ReferenceEquals(a, self) && MobaTeams.AreAllies(self, a))
            .OfType<Player>()
            .Cast<object>()
            .ToList();

        if (alliedChampions.Count == 0)
        {
            return null;
        }

        // ...but the enemy champion that triggered the aggro is looked for in a wider
        // radius, so a caster / archer poking an ally from outside the creep's tiny
        // acquisition range still gets focused.
        return map.GetAttackablesInRange(pos, ChampAggroRevealTiles)
            .Where(a => a.IsActive())
            .OfType<Player>()
            .Where(c => MobaTeams.AreEnemies(self, c)
                && MobaCombatLog.HitAnyOf(c, alliedChampions, ChampAggroWindow))
            .MinBy(self.GetDistanceTo);
    }

    private IAttackable? AcquireTarget(Monster self, Point pos, int range)
    {
        var map = self.CurrentMap;
        if (map is null)
        {
            return null;
        }

        var inRange = map.GetAttackablesInRange(pos, range).Where(a => a.IsActive() && !ReferenceEquals(a, self)).ToList();
        var enemies = inRange.Where(a => MobaTeams.AreEnemies(self, a)).ToList();
        if (enemies.Count == 0)
        {
            return null;
        }

        var enemyCreeps = enemies.OfType<Monster>().Where(m => !IsStructure(m)).ToList();
        var enemyChampions = enemies.OfType<Player>().ToList();

        var allyChampions = inRange.Where(a => MobaTeams.AreAllies(self, a)).OfType<Player>().Cast<object>().ToList();
        var allyCreeps = inRange.Where(a => MobaTeams.AreAllies(self, a)).OfType<Monster>().Where(m => !IsStructure(m)).Cast<object>().ToList();
        var me = new object[] { self };

        // Reactive rules #2-#4: react to an enemy CREEP attacking an ally / me. Enemy
        // CHAMPIONS attacking creeps never pull aggro here - only the #1 champ-vs-champ
        // interrupt or #8 (nothing else in range) makes a creep target a champion, so a
        // player can last-hit the wave freely.

        // #2 enemy creep attacking an allied champion.
        var t = enemyCreeps.Where(c => MobaCombatLog.HitAnyOf(c, allyChampions, ReactWindow)).MinBy(self.GetDistanceTo);
        if (t is not null)
        {
            return t;
        }

        // #3 enemy creep attacking an allied creep (focus fire).
        t = enemyCreeps.Where(c => MobaCombatLog.HitAnyOf(c, allyCreeps, ReactWindow)).MinBy(self.GetDistanceTo);
        if (t is not null)
        {
            return t;
        }

        // #4 enemy creep attacking me.
        t = enemyCreeps.Where(c => MobaCombatLog.HitAnyOf(c, me, ReactWindow)).MinBy(self.GetDistanceTo);
        if (t is not null)
        {
            return t;
        }

        // #7 nearest enemy creep.
        var creep = enemyCreeps.MinBy(self.GetDistanceTo);
        if (creep is not null)
        {
            return creep;
        }

        // #8 nearest enemy champion (only reached when no enemy creeps in range).
        var champion = enemyChampions.MinBy(self.GetDistanceTo);
        if (champion is not null)
        {
            return champion;
        }

        // #9 nearest enemy structure.
        return enemies.OfType<Monster>().Where(IsStructure).MinBy(self.GetDistanceTo);
    }

    // Structures (turrets / nexus) are a later W-topic; nothing is a structure yet.
    private static bool IsStructure(Monster monster) => MobaStructures.IsStructure(monster);

    /// <summary>Shortest distance (tiles) from a point to this creep's lane polyline.</summary>
    private double DistanceToLane(Point p)
    {
        if (this._waypoints.Count == 1)
        {
            return this._waypoints[0].EuclideanDistanceTo(p);
        }

        var best = double.MaxValue;
        for (var i = 0; i < this._waypoints.Count - 1; i++)
        {
            best = Math.Min(best, DistanceToSegment(p, this._waypoints[i], this._waypoints[i + 1]));
        }

        return best;
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        double ax = a.X, ay = a.Y, bx = b.X, by = b.Y, px = p.X, py = p.Y;
        var dx = bx - ax;
        var dy = by - ay;
        var lenSq = (dx * dx) + (dy * dy);
        var t = lenSq <= 0 ? 0 : (((px - ax) * dx) + ((py - ay) * dy)) / lenSq;
        t = Math.Clamp(t, 0, 1);
        var cx = ax + (t * dx);
        var cy = ay + (t * dy);
        return Math.Sqrt(((px - cx) * (px - cx)) + ((py - cy) * (py - cy)));
    }

    private async ValueTask MarchAsync(Point pos)
    {
        // Marching normally on the lane -> the next engagement gets a fresh anchor.
        if (this._hasEngageAnchor && this.DistanceToLane(pos) <= BackOnLaneTiles)
        {
            this._hasEngageAnchor = false;
        }

        if (this._currentWaypoint >= this._waypoints.Count)
        {
            return;
        }

        var waypoint = this._waypoints[this._currentWaypoint];
        if (waypoint.EuclideanDistanceTo(pos) <= WaypointReachedDistance)
        {
            this._currentWaypoint++;
            this._chunkStepCount = 0;
            if (this._currentWaypoint >= this._waypoints.Count)
            {
                return;
            }

            waypoint = this._waypoints[this._currentWaypoint];
        }

        // Only re-feed once the current chunk is (almost) consumed, so the walk stays fluid.
        if (this._chunkStepCount > 0)
        {
            var elapsed = DateTime.UtcNow - this._chunkStartedUtc;
            var consumed = (int)(elapsed.TotalMilliseconds / Math.Max(1, this.Monster.StepDelay.TotalMilliseconds));
            if (consumed < this._chunkStepCount - RefeedWhenStepsLeft)
            {
                return;
            }
        }

        await this.FeedChunkTowardAsync(pos, waypoint).ConfigureAwait(false);
    }

    /// <summary>
    /// Tiles occupied by other living creeps / champions near <paramref name="around"/>,
    /// so a march / chase step never lands on another unit or on a player. Snapshot taken
    /// once per chunk; a rare same-tick race just resolves itself on the next tick.
    /// </summary>
    private HashSet<Point> OccupiedTilesNear(Point around)
    {
        var occupied = new HashSet<Point>();
        foreach (var other in this.Monster.CurrentMap.GetAttackablesInRange(around, MaxStepsPerChunk + 3))
        {
            if (ReferenceEquals(other, this.Monster) || !other.IsActive())
            {
                continue;
            }

            if (other is Monster m)
            {
                occupied.Add(m.Position);

                // Reserve where the other creep is walking to, so we don't both aim
                // for the same tile and arrive stacked.
                if (m.IsWalking)
                {
                    occupied.Add(m.WalkTarget);
                }
            }
            else if (other is MUnique.OpenMU.GameLogic.Player)
            {
                occupied.Add(other.Position);
            }
        }

        return occupied;
    }

    /// <summary>Whether another living creep / champion is standing on <paramref name="tile"/>.</summary>
    private bool TileSharedWithOther(Point tile)
    {
        foreach (var other in this.Monster.CurrentMap.GetAttackablesInRange(tile, 1))
        {
            if (!ReferenceEquals(other, this.Monster) && other.IsActive()
                && (other is Monster || other is MUnique.OpenMU.GameLogic.Player)
                && other.Position == tile)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// One-tile sidestep off a tile shared with another unit. Prefers a free neighbour
    /// that keeps <paramref name="keepInRangeOf"/> within <paramref name="attackRange"/>
    /// so the creep can go straight back to attacking; otherwise any free neighbour.
    /// </summary>
    private async ValueTask StepOffSharedTileAsync(Point pos, IAttackable? keepInRangeOf, int attackRange)
    {
        var terrain = this.Monster.CurrentMap.Terrain.AIgrid;
        var occupied = this.OccupiedTilesNear(pos);
        Point? fallback = null;

        // Start the search from a per-creep direction so two creeps sharing a tile tend
        // to pick different neighbours instead of chasing each other around.
        var start = this.Monster.Id % 8;
        for (var i = 0; i < 8; i++)
        {
            var dir = (start + i) % 8;
            var (dx, dy) = dir switch
            {
                0 => (0, -1), 1 => (1, -1), 2 => (1, 0), 3 => (1, 1),
                4 => (0, 1), 5 => (-1, 1), 6 => (-1, 0), _ => (-1, -1),
            };

            var n = new Point((byte)(pos.X + dx), (byte)(pos.Y + dy));
            if (n == pos || terrain[n.X, n.Y] == 0 || occupied.Contains(n))
            {
                continue;
            }

            fallback ??= n;
            if (keepInRangeOf is null || n.EuclideanDistanceTo(keepInRangeOf.Position) <= attackRange)
            {
                fallback = n;
                break;
            }
        }

        if (fallback is { } dest)
        {
            var steps = new WalkingStep[] { new(pos, dest, GetDirection(pos, dest)) };
            await this.Monster.WalkToAsync(dest, steps.AsMemory()).ConfigureAwait(false);
            this._chunkStartedUtc = DateTime.UtcNow;
            this._chunkStepCount = 1;
        }
    }

    private async ValueTask FeedChunkTowardAsync(Point from, Point to)
    {
        var terrain = this.Monster.CurrentMap.Terrain.AIgrid;
        var occupied = this.OccupiedTilesNear(from);
        var buffer = ArrayPool<WalkingStep>.Shared.Rent(MaxStepsPerChunk);

        bool Passable(Point p) => terrain[p.X, p.Y] != 0 && !occupied.Contains(p);

        try
        {
            var count = 0;
            var cursor = from;
            while (count < MaxStepsPerChunk && cursor != to)
            {
                var dx = Math.Sign(to.X - cursor.X);
                var dy = Math.Sign(to.Y - cursor.Y);

                // Prefer the straight step; if it is blocked or taken, try the two steps
                // either side of it that still make progress, then give up (wait a tick).
                Point? Try(int sx, int sy)
                {
                    if (sx == 0 && sy == 0)
                    {
                        return null;
                    }

                    var p = new Point((byte)(cursor.X + sx), (byte)(cursor.Y + sy));
                    return Passable(p) ? p : null;
                }

                var next = Try(dx, dy)
                    ?? (dx != 0 && dy != 0 ? Try(dx, 0) ?? Try(0, dy) : null)
                    ?? (dx == 0 ? Try(1, dy) ?? Try(-1, dy) : null)
                    ?? (dy == 0 ? Try(dx, 1) ?? Try(dx, -1) : null);

                if (next is not { } step)
                {
                    break;
                }

                buffer[count++] = new WalkingStep(cursor, step, GetDirection(cursor, step));
                occupied.Remove(cursor);
                occupied.Add(step);
                cursor = step;
            }

            if (count == 0)
            {
                return;
            }

            await this.Monster.WalkToAsync(cursor, buffer.AsMemory(0, count)).ConfigureAwait(false);
            this._chunkStartedUtc = DateTime.UtcNow;
            this._chunkStepCount = count;
        }
        finally
        {
            ArrayPool<WalkingStep>.Shared.Return(buffer);
        }
    }
}
