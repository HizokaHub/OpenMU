// <copyright file="MobaLaneCreepIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// AI for a MOBA lane creep: it marches along a fixed list of lane waypoints.
/// </summary>
/// <remarks>
/// First building block of Fase 2 (see GAMEDESIGN.md). This version only walks the
/// lane - no faction, no target selection. Team-aware aggression, turrets and the
/// nexus come in later topics.
///
/// The monster pathfinder uses a <see cref="ScopedGridNetwork"/> which rejects any
/// path whose start/end differ by more than 16 tiles on an axis, so a far waypoint
/// is walked toward in short hops. Marching runs on its own fast timer (not the
/// base AI tick, which fires only every <c>AttackDelay</c>) so the walk looks
/// continuous instead of stop-and-go at each hop.
/// </remarks>
public sealed class MobaLaneCreepIntelligence : BasicMonsterIntelligence
{
    private const float WaypointReachedDistance = 2.5f;

    /// <summary>
    /// Maximum tiles per walk request on any axis. Kept below the scoped grid network's
    /// 16-tile segment limit.
    /// </summary>
    private const int MaxHopTiles = 10;

    private static readonly TimeSpan MarchInterval = TimeSpan.FromMilliseconds(50);

    private readonly IReadOnlyList<Point> _waypoints;

    private Timer? _marchTimer;

    private int _currentWaypoint;

    private volatile bool _marching;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaLaneCreepIntelligence"/> class.
    /// </summary>
    /// <param name="waypoints">The ordered lane waypoints the creep walks through.</param>
    public MobaLaneCreepIntelligence(IReadOnlyList<Point> waypoints)
    {
        this._waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
    }

    /// <inheritdoc />
    /// <remarks>Starts the dedicated fast march timer (Start/Pause are not virtual on the base).</remarks>
    protected override void OnStart()
    {
        base.OnStart();
        this._marchTimer ??= new Timer(_ => this.SafeMarchTick(), null, MarchInterval, MarchInterval);
    }

    /// <inheritdoc />
    /// <remarks>W1: no targets, the creep only marches (handled by the march timer).</remarks>
    protected override ValueTask<IAttackable?> SearchNextTargetAsync() => ValueTask.FromResult<IAttackable?>(null);

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Timer callback; all exceptions are swallowed.")]
    private async void SafeMarchTick()
    {
        if (this._marching)
        {
            return;
        }

        this._marching = true;
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
            this._marching = false;
        }
    }

    private async ValueTask MarchTickAsync()
    {
        var monster = this.Monster;
        if (!monster.IsAlive || monster.IsWalking || this._currentWaypoint >= this._waypoints.Count)
        {
            return;
        }

        if (monster.Attributes[Stats.IsStunned] > 0 || monster.Attributes[Stats.IsAsleep] > 0)
        {
            return;
        }

        var pos = monster.Position;
        var waypoint = this._waypoints[this._currentWaypoint];

        if (waypoint.EuclideanDistanceTo(pos) <= WaypointReachedDistance)
        {
            this._currentWaypoint++;
            if (this._currentWaypoint >= this._waypoints.Count)
            {
                return;
            }

            waypoint = this._waypoints[this._currentWaypoint];
        }

        await monster.WalkToAsync(NextHopToward(pos, waypoint)).ConfigureAwait(false);
    }

    private static Point NextHopToward(Point from, Point to)
    {
        int dx = to.X - from.X;
        int dy = to.Y - from.Y;
        int steps = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps <= MaxHopTiles)
        {
            return to;
        }

        var hopX = from.X + (dx * MaxHopTiles / steps);
        var hopY = from.Y + (dy * MaxHopTiles / steps);
        return new Point((byte)Math.Clamp(hopX, 0, 255), (byte)Math.Clamp(hopY, 0, 255));
    }
}
