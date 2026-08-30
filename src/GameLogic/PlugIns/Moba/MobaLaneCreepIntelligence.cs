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
/// </remarks>
public sealed class MobaLaneCreepIntelligence : BasicMonsterIntelligence
{
    private const float WaypointReachedDistance = 2.5f;

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
        if (this._waypoints.Count == 0 || this._currentWaypoint >= this._waypoints.Count)
        {
            return;
        }

        var target = this._waypoints[this._currentWaypoint];
        if (target.EuclideanDistanceTo(this.Monster.Position) <= WaypointReachedDistance)
        {
            this._currentWaypoint++;
            if (this._currentWaypoint >= this._waypoints.Count)
            {
                return;
            }

            target = this._waypoints[this._currentWaypoint];
        }

        await this.Monster.WalkToAsync(target).ConfigureAwait(false);
    }
}
