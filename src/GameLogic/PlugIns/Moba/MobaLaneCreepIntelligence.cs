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

    private const int MaxStepsPerChunk = 16;

    private const int RefeedWhenStepsLeft = 3;

    /// <summary>Tiles added to the creep's attack range to "notice" and walk toward an enemy.</summary>
    private const int AcquisitionRangeBonus = 6;

    /// <summary>How far off its lane the creep will stray chasing a target before giving up.</summary>
    private const double ChaseLeashTiles = 10;

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
            this._returningToLane = true;
            return;
        }

        // Drop an invalid / out-of-range target.
        if (this._combatTarget is { } current
            && (!current.IsActive() || current.GetDistanceTo(pos) > acquisitionRange))
        {
            this._combatTarget = null;
        }

        var lockedOnStructure = this._combatTarget is Monster m && IsStructure(m);

        // #1 champion-aggro interrupt: force-switch onto an enemy champion that just
        // damaged an ally, for ChampAggroWindow. Does not override a structure lock.
        if (!lockedOnStructure && this.FindChampionAggro(monster, pos, acquisitionRange) is { } aggroChamp)
        {
            this._combatTarget = aggroChamp;
        }
        else if (this._combatTarget is null && this.AcquireTarget(monster, pos, acquisitionRange) is { } acquired)
        {
            this._combatTarget = acquired;
        }

        if (this._combatTarget is not null && !this._hasEngageAnchor)
        {
            this._engageAnchor = pos;
            this._hasEngageAnchor = true;
        }

        if (this._combatTarget is { } target)
        {
            if (target.GetDistanceTo(pos) <= attackRange)
            {
                await monster.AttackAsync(target).ConfigureAwait(false);
            }
            else if (!monster.IsWalking)
            {
                await this.FeedChunkTowardAsync(pos, target.Position).ConfigureAwait(false);
            }

            return;
        }

        // No target: march the lane (also the path back to it when returning).
        await this.MarchAsync(pos).ConfigureAwait(false);
    }

    private static readonly TimeSpan ReactWindow = TimeSpan.FromSeconds(2);

    private static readonly TimeSpan ChampAggroWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// #1 of the LoL priority: an enemy champion involved in champion-vs-champion combat
    /// with one of this creep's allied champions in the last <see cref="ChampAggroWindow"/>
    /// (either direction - the enemy hit our champ, or our champ hit the enemy). Damage to
    /// creeps never triggers this: a player may last-hit freely. This is a force-switch
    /// interrupt (unless the creep is locked on a structure).
    /// </summary>
    private Player? FindChampionAggro(Monster self, Point pos, int range)
    {
        var map = self.CurrentMap;
        if (map is null)
        {
            return null;
        }

        var inRange = map.GetAttackablesInRange(pos, range).Where(a => a.IsActive()).ToList();
        var alliedChampions = inRange
            .Where(a => !ReferenceEquals(a, self) && MobaTeams.AreAllies(self, a))
            .OfType<Player>()
            .Cast<object>()
            .ToList();

        if (alliedChampions.Count == 0)
        {
            return null;
        }

        return inRange
            .OfType<Player>()
            .Where(c => MobaTeams.AreEnemies(self, c)
                && (MobaCombatLog.HitAnyOf(c, alliedChampions, ChampAggroWindow)
                    || alliedChampions.Any(ac => MobaCombatLog.HitAnyOf(ac, new object[] { c }, ChampAggroWindow))))
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
    private static bool IsStructure(Monster monster) => false;

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

    private async ValueTask FeedChunkTowardAsync(Point from, Point to)
    {
        var terrain = this.Monster.CurrentMap.Terrain.AIgrid;
        var buffer = ArrayPool<WalkingStep>.Shared.Rent(MaxStepsPerChunk);
        try
        {
            var count = 0;
            var cursor = from;
            while (count < MaxStepsPerChunk && cursor != to)
            {
                var next = new Point(
                    (byte)(cursor.X + Math.Sign(to.X - cursor.X)),
                    (byte)(cursor.Y + Math.Sign(to.Y - cursor.Y)));

                if (terrain[next.X, next.Y] == 0)
                {
                    break;
                }

                buffer[count++] = new WalkingStep(cursor, next, GetDirection(cursor, next));
                cursor = next;
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
