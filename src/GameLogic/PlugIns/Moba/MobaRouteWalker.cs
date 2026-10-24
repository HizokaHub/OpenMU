// <copyright file="MobaRouteWalker.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Walks a monster towards a point anywhere on the map. Close points use the short search of the monster; far ones a heuristic search of the whole
/// route (like the travel of the bots), walked in stretches of <see cref="StepsPerHop"/> steps: the short search of the monster refuses far
/// destinations and the walker takes 16 steps at most, which left the creatures standing half way. One instance per brain (it keeps the route).
/// </summary>
public sealed class MobaRouteWalker
{
    /// <summary>Tiles within which the short search of the monster is enough.</summary>
    private const int DirectWalkRange = 6;

    /// <summary>Steps of a stretch of the route.</summary>
    private const int StepsPerHop = 10;

    /// <summary>Nodes a route search may close (the map is 256x256).</summary>
    private const int RouteSearchLimit = 20000;

    private static readonly ConcurrentBag<PathFinder> RouteFinders = new();

    private IList<PathResultNode>? _route;
    private int _routeIndex;
    private Point _routeGoal;

    /// <summary>Walks towards a point; does nothing while the monster is already walking.</summary>
    /// <param name="monster">The monster.</param>
    /// <param name="goal">The point.</param>
    /// <returns><see langword="true"/> if a walk was started or is on its way; <see langword="false"/> when there is no way there.</returns>
    public async ValueTask<bool> TravelAsync(Monster monster, Point goal)
    {
        if (monster.CurrentMap is not { } map)
        {
            return false;
        }

        var position = monster.Position;
        if (monster.IsWalking)
        {
            return true;
        }

        if (Distance(position, goal) <= DirectWalkRange && await monster.WalkToAsync(goal).ConfigureAwait(false))
        {
            this._route = null;
            return true;
        }

        if (this._route is not { } route
            || this._routeIndex >= route.Count
            || Distance(goal, this._routeGoal) > 3
            || Distance(position, route[this._routeIndex].PreviousPoint) > 1)
        {
            route = FindRoute(map, position, goal);
            if (route is null || route.Count == 0)
            {
                this._route = null;
                return false;
            }

            this._route = route;
            this._routeIndex = 0;
            this._routeGoal = goal;
        }

        var count = Math.Min(route.Count - this._routeIndex, StepsPerHop);
        var steps = new WalkingStep[count];
        for (var i = 0; i < count; i++)
        {
            var node = route[this._routeIndex + i];
            var from = i == 0 ? position : steps[i - 1].To;
            steps[i] = new WalkingStep(from, node.Point, from.GetDirectionTo(node.Point));
        }

        this._routeIndex += count;
        await monster.WalkToAsync(steps[count - 1].To, steps).ConfigureAwait(false);
        return true;
    }

    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static PathFinder CreateRouteFinder()
        => new(new FullGridNetwork(true))
        {
            Heuristic = new ManhattanHeuristic(),
            HeuristicEstimate = 12,
            SearchLimit = RouteSearchLimit,
        };

    private static IList<PathResultNode>? FindRoute(GameMap map, Point from, Point to)
    {
        if (!RouteFinders.TryTake(out var finder))
        {
            finder = CreateRouteFinder();
        }

        try
        {
            finder.ResetPathFinder();
            return finder.FindPath(from, to, map.Terrain.AIgrid, true);
        }
        finally
        {
            RouteFinders.Add(finder);
        }
    }

    private sealed class ManhattanHeuristic : IHeuristic
    {
        public int HeuristicEstimateMultiplier { get; set; }

        public int CalculateHeuristicDistance(Point location, Point target)
            => this.HeuristicEstimateMultiplier * (Math.Abs(location.X - target.X) + Math.Abs(location.Y - target.Y));
    }
}
