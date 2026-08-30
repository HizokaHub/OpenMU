// <copyright file="MobaLaneCreepIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// AI for a MOBA lane creep: it marches along a fixed list of lane waypoints.
/// </summary>
/// <remarks>
/// First building block of Fase 2 (see GAMEDESIGN.md). This version only walks the
/// lane - no faction, no target selection. Team-aware aggression, turrets and the
/// nexus come in later topics. When the last waypoint is reached the creep just
/// stops; wave lifetime / cleanup is a later topic too.
///
/// The monster pathfinder uses a <see cref="ScopedGridNetwork"/> which rejects any
/// path whose start/end differ by more than 16 tiles on an axis, so a far waypoint
/// is walked toward in short hops.
/// </remarks>
public sealed class MobaLaneCreepIntelligence : BasicMonsterIntelligence
{
    private const float WaypointReachedDistance = 2.5f;

    /// <summary>
    /// Maximum tiles per walk request on any axis. Kept below the scoped grid network's
    /// 16-tile segment limit.
    /// </summary>
    private const int MaxHopTiles = 12;

    private readonly IReadOnlyList<Point> _waypoints;

    private int _currentWaypoint;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaLaneCreepIntelligence"/> class.
    /// </summary>
    /// <param name="waypoints">The ordered lane waypoints the creep walks through.</param>
    public MobaLaneCreepIntelligence(IReadOnlyList<Point> waypoints)
    {
        this._waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
    }

    /// <inheritdoc />
    /// <remarks>W1: no targets, the creep only marches.</remarks>
    protected override ValueTask<IAttackable?> SearchNextTargetAsync() => ValueTask.FromResult<IAttackable?>(null);

    /// <inheritdoc />
    protected override async ValueTask TickWithoutTargetAsync()
    {
        if (this._currentWaypoint >= this._waypoints.Count)
        {
            return;
        }

        var pos = this.Monster.Position;
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

        await this.Monster.WalkToAsync(NextHopToward(pos, waypoint)).ConfigureAwait(false);
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
