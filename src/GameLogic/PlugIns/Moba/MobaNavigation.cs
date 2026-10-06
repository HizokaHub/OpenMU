// <copyright file="MobaNavigation.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Whole-map A* over the walk grid for MOBA bots. The shared <see cref="PathFinder"/> gives up after 500 nodes and has no
/// heuristic, which is not enough for a 256x256 arena with walls between the lanes; bots that walked in a straight
/// line got stuck on wall corners whenever their target was far or off their lane.
/// </summary>
public static class MobaNavigation
{
    private const int MaxExpansions = 40000;

    private static readonly (int Dx, int Dy)[] Neighbours =
    {
        (0, -1), (1, 0), (0, 1), (-1, 0), (1, -1), (1, 1), (-1, 1), (-1, -1),
    };

    /// <summary>Finds a path over walkable cells (grid value other than 0), 8-connected without cutting wall corners.</summary>
    /// <param name="grid">The AI grid of the map.</param>
    /// <param name="from">The start cell.</param>
    /// <param name="to">The target cell (moved to the nearest walkable cell if it is a wall).</param>
    /// <returns>The cells from <paramref name="from"/> (included) to the target, or null if unreachable.</returns>
    public static List<Point>? FindPath(byte[,] grid, Point from, Point to)
    {
        if (grid[to.X, to.Y] == 0)
        {
            to = MobaLayout.NearestWalkable(to.X, to.Y);
            if (grid[to.X, to.Y] == 0)
            {
                return null;
            }
        }

        const int size = 256;
        var cost = new float[size * size];
        Array.Fill(cost, float.MaxValue);
        var parent = new int[size * size];
        var closed = new bool[size * size];
        var open = new PriorityQueue<int, float>();
        var start = (from.Y * size) + from.X;
        var goal = (to.Y * size) + to.X;
        cost[start] = 0;
        parent[start] = start;
        open.Enqueue(start, Heuristic(from.X, from.Y, to.X, to.Y));
        var expansions = 0;
        while (open.TryDequeue(out var current, out _))
        {
            if (closed[current])
            {
                continue;
            }

            closed[current] = true;
            if (current == goal)
            {
                var path = new List<Point>();
                for (var node = goal; ; node = parent[node])
                {
                    path.Add(new Point((byte)(node % size), (byte)(node / size)));
                    if (node == start)
                    {
                        break;
                    }
                }

                path.Reverse();
                return path;
            }

            if (++expansions > MaxExpansions)
            {
                return null;
            }

            int cx = current % size, cy = current / size;
            foreach (var (dx, dy) in Neighbours)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= size || ny >= size || grid[nx, ny] == 0)
                {
                    continue;
                }

                if (dx != 0 && dy != 0 && (grid[cx + dx, cy] == 0 || grid[cx, cy + dy] == 0))
                {
                    continue;
                }

                var next = (ny * size) + nx;
                var newCost = cost[current] + (dx != 0 && dy != 0 ? 1.414f : 1f);
                if (newCost < cost[next])
                {
                    cost[next] = newCost;
                    parent[next] = current;
                    open.Enqueue(next, newCost + Heuristic(nx, ny, to.X, to.Y));
                }
            }
        }

        return null;
    }

    private static float Heuristic(int x, int y, int tx, int ty)
    {
        float dx = Math.Abs(x - tx), dy = Math.Abs(y - ty);
        return (Math.Max(dx, dy) - Math.Min(dx, dy)) + (1.414f * Math.Min(dx, dy));
    }
}
