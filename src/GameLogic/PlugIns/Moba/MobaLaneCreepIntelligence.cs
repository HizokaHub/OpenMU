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
/// AI for a MOBA lane creep: it marches along a fixed list of lane waypoints and
/// fights enemies of its team along the way.
/// </summary>
/// <remarks>
/// Fase 2 building block (see GAMEDESIGN.md). W1 = the lane march. W2 slice 1 (this
/// version) adds a <see cref="MobaTeam"/> and the passive part of the LoL targeting
/// priority: nearest enemy creep, then nearest enemy champion, then nearest enemy
/// structure. The reactive rules (#1-#6: react to who is attacking whom), the
/// champion-aggro interrupt, chase leash and target lock come with the combat-events
/// ledger in the next slice.
///
/// Movement: a dedicated fast timer feeds the walker straight-line step chunks and
/// re-feeds the next chunk a few steps before the current one runs out, so the walk
/// is continuous. The timer yields while the creep has a combat target (the base AI
/// tick drives the attack / approach then).
/// </remarks>
public sealed class MobaLaneCreepIntelligence : BasicMonsterIntelligence
{
    private const float WaypointReachedDistance = 1.5f;

    private const int MaxStepsPerChunk = 16;

    /// <summary>Re-feed the walker when this many steps of the current chunk are left.</summary>
    private const int RefeedWhenStepsLeft = 3;

    /// <summary>Tiles added to the creep's attack range to "notice" and walk toward an enemy.</summary>
    private const int AcquisitionRangeBonus = 6;

    private static readonly TimeSpan MarchInterval = TimeSpan.FromMilliseconds(60);

    private readonly IReadOnlyList<Point> _waypoints;

    private readonly MobaTeam _team;

    private Timer? _marchTimer;

    private int _currentWaypoint;

    private volatile bool _ticking;

    private DateTime _chunkStartedUtc;

    private int _chunkStepCount;

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
    /// <remarks>Registers the team and starts the dedicated march timer (Start/Pause are not virtual on the base).</remarks>
    protected override void OnStart()
    {
        base.OnStart();
        MobaTeams.Set(this.Monster, this._team);
        this._marchTimer ??= new Timer(_ => this.SafeMarchTick(), null, MarchInterval, MarchInterval);
    }

    /// <inheritdoc />
    protected override async ValueTask<IAttackable?> SearchNextTargetAsync()
    {
        var monster = this.Monster;
        var map = monster.CurrentMap;
        if (map is null)
        {
            return null;
        }

        var acquisitionRange = monster.Definition.AttackRange + AcquisitionRangeBonus;
        var enemies = map.GetAttackablesInRange(monster.Position, acquisitionRange)
            .Where(a => a.IsActive() && !ReferenceEquals(a, monster) && MobaTeams.AreEnemies(monster, a))
            .ToList();

        if (enemies.Count == 0)
        {
            return null;
        }

        // #7 nearest enemy creep.
        var creep = enemies.OfType<NPC.Monster>().Where(m => !IsStructure(m)).MinBy(monster.GetDistanceTo);
        if (creep is not null)
        {
            return creep;
        }

        // #8 nearest enemy champion.
        var champion = enemies.OfType<Player>().MinBy(monster.GetDistanceTo);
        if (champion is not null)
        {
            return champion;
        }

        // #9 nearest enemy structure.
        return enemies.OfType<NPC.Monster>().Where(IsStructure).MinBy(monster.GetDistanceTo);
    }

    // Structures (turrets / nexus) are a later W-topic; nothing is a structure yet.
    private static bool IsStructure(NPC.Monster monster) => false;

    /// <inheritdoc />
    /// <remarks>Marching is done on the dedicated timer; keep the base AI tick idle.</remarks>
    protected override ValueTask TickWithoutTargetAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    protected override void Dispose(bool managed)
    {
        this._marchTimer?.Dispose();
        this._marchTimer = null;
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
    private async void SafeMarchTick()
    {
        if (this._ticking)
        {
            return;
        }

        this._ticking = true;
        try
        {
            await this.MarchTickAsync().ConfigureAwait(false);
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

    private async ValueTask MarchTickAsync()
    {
        var monster = this.Monster;
        if (!monster.IsAlive || this._currentWaypoint >= this._waypoints.Count)
        {
            return;
        }

        // Fighting: let the base AI tick drive the attack / approach, don't march.
        if (this.CurrentTarget is { } target && target.IsActive())
        {
            return;
        }

        if (monster.Attributes[Stats.IsStunned] > 0 || monster.Attributes[Stats.IsAsleep] > 0 || monster.Attributes[Stats.IsFrozen] > 0)
        {
            return;
        }

        var pos = monster.Position;
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
            var consumed = (int)(elapsed.TotalMilliseconds / Math.Max(1, monster.StepDelay.TotalMilliseconds));
            if (consumed < this._chunkStepCount - RefeedWhenStepsLeft)
            {
                return;
            }
        }

        await this.FeedNextChunkAsync(pos, waypoint).ConfigureAwait(false);
    }

    private async ValueTask FeedNextChunkAsync(Point from, Point to)
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
