// <copyright file="MobaLayout.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.IO;
using System.Text.Json;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// A lane of the MOBA arena: its waypoints from the blue base to the red base.
/// </summary>
/// <param name="Name">The lane name (top, mid or bot).</param>
/// <param name="Waypoints">The waypoints, blue base first.</param>
public sealed record MobaLane(string Name, IReadOnlyList<Point> Waypoints);

/// <summary>
/// A turret of the MOBA arena.
/// </summary>
/// <param name="Team">The team the turret defends.</param>
/// <param name="Lane">The lane index (0 top, 1 mid, 2 bot).</param>
/// <param name="Tier">The tier: 0 outer (first to fall), 1 middle, 2 base turret (last).</param>
/// <param name="Position">The position.</param>
public sealed record MobaTowerSpec(MobaTeam Team, int Lane, int Tier, Point Position);

/// <summary>
/// An inhibitor of the MOBA arena (one per lane and team, behind the base turret of the lane).
/// </summary>
/// <param name="Team">The team the inhibitor defends.</param>
/// <param name="Lane">The lane index (0 top, 1 mid, 2 bot).</param>
/// <param name="Position">The position.</param>
public sealed record MobaInhibitorSpec(MobaTeam Team, int Lane, Point Position);

/// <summary>
/// The geometry of the MOBA arena (lanes, turrets, nexuses, shops), read from the embedded
/// <c>MobaLayout.json</c> exported by <c>tools/moba-map-editor.html</c>. Turrets are generated along
/// every lane (two pairs per lane and team) so the layout only needs the lanes, nexuses and shops.
/// </summary>
public static class MobaLayout
{
    /// <summary>The number of lanes.</summary>
    public const int LaneCount = 3;

    /// <summary>The lane index of the middle lane.</summary>
    public const int MidLane = 1;

    /// <summary>
    /// Scale position of the middle turret of a lane. The scale runs from the base turret (1, placed by hand in the editor) to
    /// the middle of the lane (100), measured along the lane.
    /// </summary>
    public const int MiddleTowerScale = 35;

    /// <summary>Scale position of the outer turret of a lane (see <see cref="MiddleTowerScale"/>).</summary>
    public const int OuterTowerScale = 75;

    /// <summary>Half the distance between the two turrets of a pair, in tiles.</summary>
    public const float PairHalfSpacing = 3f;

    /// <summary>How far behind the base turret of a lane its inhibitor stands, measured along the lane toward the own nexus, in tiles.</summary>
    public const double InhibitorSetback = 9;

    private static readonly string[] LaneNames = { "top", "mid", "bot" };

    private static readonly Lazy<Data> Loaded = new(Load);

    private static readonly Lazy<IReadOnlyList<MobaInhibitorSpec>> InhibitorList = new(BuildInhibitors);

    /// <summary>Gets the inhibitors of both teams: on the road of each lane, <see cref="InhibitorSetback"/> tiles behind its base turret toward its nexus.</summary>
    public static IReadOnlyList<MobaInhibitorSpec> Inhibitors => InhibitorList.Value;

    /// <summary>Gets the lanes (top, mid, bot), each from the blue base to the red base.</summary>
    public static IReadOnlyList<MobaLane> Lanes => Loaded.Value.Lanes;

    /// <summary>Gets the generated turrets of both teams.</summary>
    public static IReadOnlyList<MobaTowerSpec> Towers => Loaded.Value.Towers;

    /// <summary>Gets the nexus position of a team.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The position.</returns>
    public static Point NexusOf(MobaTeam team) => team == MobaTeam.Red ? Loaded.Value.RedNexus : Loaded.Value.BlueNexus;

    /// <summary>Gets the walkable cell of the arena nearest to a position (within a few tiles).</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The nearest walkable cell, or the rounded position if none is near.</returns>
    public static Point NearestWalkable(double x, double y)
    {
        var terrain = Loaded.Value.Terrain;
        int cx = (int)Math.Clamp(Math.Round(x), 0, 255), cy = (int)Math.Clamp(Math.Round(y), 0, 255);
        for (var radius = 0; radius <= 6; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx is >= 0 and <= 255 && ny is >= 0 and <= 255 && Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius && terrain.WalkMap[nx, ny])
                    {
                        return new Point((byte)nx, (byte)ny);
                    }
                }
            }
        }

        return new Point((byte)cx, (byte)cy);
    }

    /// <summary>Gets whether an arena cell can be walked on (walls, solid decoration and the outside are not).</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns><see langword="true"/> if the cell is walkable.</returns>
    public static bool IsWalkable(int x, int y) => x is >= 0 and <= 255 && y is >= 0 and <= 255 && Loaded.Value.Terrain.WalkMap[x, y];

    /// <summary>Gets the shop (vendor and recall) position of a team.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The position.</returns>
    public static Point ShopOf(MobaTeam team) => team == MobaTeam.Red ? Loaded.Value.RedShop : Loaded.Value.BlueShop;

    /// <summary>Gets the waypoints of a lane in the marching order of a team (from its own base to the enemy base).</summary>
    /// <param name="team">The team.</param>
    /// <param name="lane">The lane index.</param>
    /// <returns>The waypoints.</returns>
    public static IReadOnlyList<Point> WaypointsFor(MobaTeam team, int lane)
    {
        var points = Lanes[Math.Clamp(lane, 0, LaneCount - 1)].Waypoints;
        return team == MobaTeam.Red ? points.Reverse().ToArray() : points;
    }

    /// <summary>Gets the index of the lane whose polyline is closest to a position.</summary>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The lane index.</returns>
    public static int NearestLane(double x, double y)
    {
        var best = MidLane;
        var bestDistance = double.MaxValue;
        for (var lane = 0; lane < LaneCount; lane++)
        {
            var distance = DistanceToPolyline(Lanes[lane].Waypoints, x, y);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = lane;
            }
        }

        return best;
    }

    /// <summary>Gets the distance from a position to a polyline.</summary>
    /// <param name="waypoints">The polyline.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The distance in tiles.</returns>
    public static double DistanceToPolyline(IReadOnlyList<Point> waypoints, double x, double y)
    {
        var best = double.MaxValue;
        for (var i = 0; i + 1 < waypoints.Count; i++)
        {
            double ax = waypoints[i].X, ay = waypoints[i].Y, bx = waypoints[i + 1].X, by = waypoints[i + 1].Y;
            double dx = bx - ax, dy = by - ay;
            var length2 = (dx * dx) + (dy * dy);
            var t = length2 <= 0 ? 0 : Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / length2, 0, 1);
            best = Math.Min(best, Math.Sqrt(Math.Pow(x - (ax + (t * dx)), 2) + Math.Pow(y - (ay + (t * dy)), 2)));
        }

        return best;
    }

    /// <summary>Gets the arc length (distance walked from the first waypoint) of the polyline point nearest to a position.</summary>
    /// <param name="waypoints">The polyline.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <returns>The arc length in tiles.</returns>
    public static double ArcOf(IReadOnlyList<Point> waypoints, double x, double y)
    {
        double best = double.MaxValue, bestArc = 0, walked = 0;
        for (var i = 0; i + 1 < waypoints.Count; i++)
        {
            double ax = waypoints[i].X, ay = waypoints[i].Y, bx = waypoints[i + 1].X, by = waypoints[i + 1].Y;
            double dx = bx - ax, dy = by - ay;
            var length2 = (dx * dx) + (dy * dy);
            var length = Math.Sqrt(length2);
            var t = length2 <= 0 ? 0 : Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / length2, 0, 1);
            var distance = Math.Sqrt(Math.Pow(x - (ax + (t * dx)), 2) + Math.Pow(y - (ay + (t * dy)), 2));
            if (distance < best)
            {
                best = distance;
                bestArc = walked + (t * length);
            }

            walked += length;
        }

        return bestArc;
    }

    /// <summary>Gets the polyline point at an arc length (clamped to the polyline).</summary>
    /// <param name="waypoints">The polyline.</param>
    /// <param name="arc">The arc length in tiles.</param>
    /// <returns>The point and the unit direction of the polyline there.</returns>
    public static (double X, double Y, double Dx, double Dy) PointAtArc(IReadOnlyList<Point> waypoints, double arc)
    {
        var (x, y, tx, ty) = PointAt(waypoints, Math.Clamp(arc, 0, LengthOf(waypoints)));
        return (x, y, tx, ty);
    }

    /// <summary>Gets the total length of a polyline.</summary>
    /// <param name="waypoints">The polyline.</param>
    /// <returns>The length in tiles.</returns>
    public static double LengthOf(IReadOnlyList<Point> waypoints)
    {
        double total = 0;
        for (var i = 0; i + 1 < waypoints.Count; i++)
        {
            total += Math.Sqrt(Math.Pow(waypoints[i + 1].X - waypoints[i].X, 2) + Math.Pow(waypoints[i + 1].Y - waypoints[i].Y, 2));
        }

        return total;
    }

    private static IReadOnlyList<MobaInhibitorSpec> BuildInhibitors()
    {
        var data = Loaded.Value;
        var avoid = new HashSet<(int, int)>(data.Towers.Select(t => ((int)t.Position.X, (int)t.Position.Y)))
        {
            (data.BlueNexus.X, data.BlueNexus.Y),
            (data.RedNexus.X, data.RedNexus.Y),
        };

        var result = new List<MobaInhibitorSpec>();
        foreach (var tower in data.Towers.Where(t => t.Tier == 2))
        {
            var lane = data.Lanes[tower.Lane].Waypoints;
            var arc = ArcOf(lane, tower.Position.X, tower.Position.Y);
            var target = tower.Team == MobaTeam.Blue ? arc - InhibitorSetback : arc + InhibitorSetback;
            var (x, y, _, _) = PointAtArc(lane, target);
            var position = FindClear(data.Terrain, x, y, avoid);
            avoid.Add((position.X, position.Y));
            result.Add(new MobaInhibitorSpec(tower.Team, tower.Lane, position));
        }

        return result;
    }

    private static Data Load()
    {
        using var stream = typeof(MobaLayout).Assembly.GetManifestResourceStream("MobaLayout.json")
            ?? throw new InvalidOperationException("MobaLayout.json is not embedded.");
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        Point FindObject(string type, string team, Point fallback)
        {
            foreach (var element in root.GetProperty("objects").EnumerateArray())
            {
                if (element.GetProperty("type").GetString() == type && element.GetProperty("team").GetString() == team)
                {
                    return new Point(element.GetProperty("x").GetByte(), element.GetProperty("y").GetByte());
                }
            }

            return fallback;
        }

        var blueNexus = FindObject("nexus", "blue", new Point(116, 44));
        var redNexus = FindObject("nexus", "red", new Point(116, 224));
        var blueShop = FindObject("shop", "blue", new Point(112, 57));
        var redShop = FindObject("shop", "red", new Point(112, 208));

        var terrain = new GameMapTerrain(new GameMapDefinition { Number = 200 });
        var wallDistance = BuildWallDistance(terrain);
        var lanes = new List<MobaLane>();
        for (var i = 0; i < LaneCount; i++)
        {
            var points = new List<Point>();
            if (root.GetProperty("lanes").TryGetProperty(LaneNames[i], out var array))
            {
                foreach (var pair in array.EnumerateArray())
                {
                    points.Add(new Point(pair[0].GetByte(), pair[1].GetByte()));
                }
            }

            if (points.Count < 2)
            {
                points = new List<Point> { blueNexus, redNexus };
            }

            // Lanes are drawn in either direction in the editor: orient them blue base -> red base.
            var first = points[0];
            var distanceToBlue = Math.Pow(first.X - blueNexus.X, 2) + Math.Pow(first.Y - blueNexus.Y, 2);
            var distanceToRed = Math.Pow(first.X - redNexus.X, 2) + Math.Pow(first.Y - redNexus.Y, 2);
            if (distanceToRed < distanceToBlue)
            {
                points.Reverse();
            }

            lanes.Add(new MobaLane(LaneNames[i], Densify(terrain, wallDistance, points)));
        }

        // Three turrets per lane and team at fixed spots chosen by the user (2026-10-17). Tier 0 = outer (first met from the
        // river), 1 = middle, 2 = base. Blue positions are given; the red ones are the same spots turned 180 degrees about the
        // arena centre (x -> 236 - x, y -> 255 - y), which puts them on the other end of the lane they belong to, and each
        // turret takes the lane whose road is nearest to it. The outer turret of the side lanes sits on the road itself, at
        // the same distance from the river road (y = 127.5) as the outer mid turret (10.5 tiles): blue at y 117, red at y 138.
        const int OuterBlueY = 117;
        const int OuterRedY = 138;
        var spots = new List<(MobaTeam Team, int Tier, Point Position)>();
        var midOuter = new Point(117, OuterBlueY);
        var midMiddle = new Point(117, 86);
        var midBase = new Point(118, 47);
        var topOuter = LaneCentreAtY(lanes[0].Waypoints, OuterBlueY);
        var topMiddle = new Point(48, 55);
        var topBase = new Point(77, 38);   // the mirror image of the bottom lane's base turret across the middle
        var botOuter = LaneCentreAtY(lanes[2].Waypoints, OuterBlueY);
        var botMiddle = new Point(182, 55);
        var botBase = new Point(159, 38);
        foreach (var (tier, point) in new[] { (0, midOuter), (1, midMiddle), (2, midBase), (0, topOuter), (1, topMiddle), (2, topBase), (0, botOuter), (1, botMiddle), (2, botBase) })
        {
            spots.Add((MobaTeam.Blue, tier, point));
        }

        // Red: the turned copy of every blue spot, except the outer side-lane ones, which are centred on the road at y 138.
        foreach (var (tier, point) in new[] { (0, midOuter), (1, midMiddle), (2, midBase), (1, topMiddle), (2, topBase), (1, botMiddle), (2, botBase) })
        {
            spots.Add((MobaTeam.Red, tier, new Point((byte)(236 - point.X), (byte)(255 - point.Y))));
        }

        spots.Add((MobaTeam.Red, 0, LaneCentreAtY(lanes[0].Waypoints, OuterRedY)));
        spots.Add((MobaTeam.Red, 0, LaneCentreAtY(lanes[2].Waypoints, OuterRedY)));

        var towers = new List<MobaTowerSpec>();
        foreach (var (team, tier, point) in spots)
        {
            var lane = lanes.Select((l, i) => (l, i)).OrderBy(x => DistanceToPolyline(x.l.Waypoints, point.X, point.Y)).First().i;
            towers.Add(new MobaTowerSpec(team, lane, tier, FindClear(terrain, point.X, point.Y, new HashSet<(int, int)>())));
        }

        return new Data(lanes, towers, blueNexus, redNexus, blueShop, redShop, terrain);
    }

    /// <summary>The point of a lane's road at a given row: the road is crossed at that y (first crossing).</summary>
    private static Point LaneCentreAtY(IReadOnlyList<Point> waypoints, int y)
    {
        for (var i = 1; i < waypoints.Count; i++)
        {
            var a = waypoints[i - 1];
            var b = waypoints[i];
            if ((a.Y <= y && b.Y >= y) || (a.Y >= y && b.Y <= y))
            {
                var t = a.Y == b.Y ? 0.0 : (y - a.Y) / (double)(b.Y - a.Y);
                return new Point((byte)Math.Round(a.X + (t * (b.X - a.X))), (byte)y);
            }
        }

        return waypoints[waypoints.Count / 2];
    }

    /// <summary>Distance in cells from every cell to the nearest wall (capped at 8), by multi-source BFS.</summary>
    private static byte[] BuildWallDistance(GameMapTerrain terrain)
    {
        var distance = new byte[256 * 256];
        var queue = new Queue<int>();
        for (var i = 0; i < distance.Length; i++)
        {
            if (!terrain.WalkMap[i % 256, i / 256])
            {
                queue.Enqueue(i);
            }
            else
            {
                distance[i] = 255;
            }
        }

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            int x = i % 256, y = i / 256;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx > 255 || ny > 255)
                    {
                        continue;
                    }

                    var n = (ny * 256) + nx;
                    if (distance[n] == 255)
                    {
                        distance[n] = (byte)Math.Min(8, distance[i] + 1);
                        queue.Enqueue(n);
                    }
                }
            }
        }

        return distance;
    }

    /// <summary>
    /// Replaces the hand-drawn polyline by the real walkable path through its points (shortest path that prefers the
    /// middle of corridors), resampled every few cells, so creeps and bots that walk straight between waypoints never
    /// run into a wall.
    /// </summary>
    private static List<Point> Densify(GameMapTerrain terrain, byte[] wallDistance, List<Point> drawn)
    {
        const int sampleEvery = 4;
        var result = new List<Point>();
        var previous = SnapToOpen(terrain, wallDistance, drawn[0]);
        result.Add(previous);
        for (var i = 1; i < drawn.Count; i++)
        {
            var next = SnapToOpen(terrain, wallDistance, drawn[i]);
            var path = FindPath(terrain, wallDistance, previous, next);
            var sinceLast = 0;
            foreach (var cell in path.Skip(1))
            {
                sinceLast++;
                if (sinceLast >= sampleEvery || cell == next)
                {
                    if (result[^1] != cell)
                    {
                        result.Add(cell);
                    }

                    sinceLast = 0;
                }
            }

            previous = next;
        }

        return result;
    }

    private static Point SnapToOpen(GameMapTerrain terrain, byte[] wallDistance, Point point)
    {
        Point best = point;
        var bestScore = double.MaxValue;
        for (var dy = -8; dy <= 8; dy++)
        {
            for (var dx = -8; dx <= 8; dx++)
            {
                int x = point.X + dx, y = point.Y + dy;
                if (x < 1 || y < 1 || x > 254 || y > 254 || !terrain.WalkMap[x, y])
                {
                    continue;
                }

                var score = Math.Sqrt((dx * dx) + (dy * dy)) + (wallDistance[(y * 256) + x] >= 2 ? 0 : 3);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = new Point((byte)x, (byte)y);
                }
            }
        }

        return best;
    }

    /// <summary>Dijkstra over the 8-connected walk map, with a penalty for cells close to walls.</summary>
    private static List<Point> FindPath(GameMapTerrain terrain, byte[] wallDistance, Point from, Point to)
    {
        var cost = new float[256 * 256];
        Array.Fill(cost, float.MaxValue);
        var parent = new int[256 * 256];
        Array.Fill(parent, -1);
        var queue = new PriorityQueue<int, float>();
        var start = (from.Y * 256) + from.X;
        var goal = (to.Y * 256) + to.X;
        cost[start] = 0;
        queue.Enqueue(start, 0);
        while (queue.TryDequeue(out var current, out var currentCost))
        {
            if (current == goal)
            {
                break;
            }

            if (currentCost > cost[current])
            {
                continue;
            }

            int x = current % 256, y = current / 256;
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 1 || ny < 1 || nx > 254 || ny > 254 || !terrain.WalkMap[nx, ny])
                    {
                        continue;
                    }

                    // Do not cut a corner diagonally.
                    if (dx != 0 && dy != 0 && (!terrain.WalkMap[x + dx, y] || !terrain.WalkMap[x, y + dy]))
                    {
                        continue;
                    }

                    var n = (ny * 256) + nx;
                    var close = wallDistance[n];
                    var step = ((dx != 0 && dy != 0) ? 1.414f : 1f) * (1f + (close < 4 ? (4 - close) * 1.5f : 0f));
                    if (currentCost + step < cost[n])
                    {
                        cost[n] = currentCost + step;
                        parent[n] = current;
                        queue.Enqueue(n, cost[n]);
                    }
                }
            }
        }

        var path = new List<Point>();
        if (parent[goal] < 0 && start != goal)
        {
            // Unreachable: keep the straight segment so the lane still exists (the tests flag it).
            path.Add(from);
            path.Add(to);
            return path;
        }

        for (var cell = goal; cell >= 0; cell = parent[cell])
        {
            path.Add(new Point((byte)(cell % 256), (byte)(cell / 256)));
            if (cell == start)
            {
                break;
            }
        }

        path.Reverse();
        return path;
    }

    private static (double X, double Y, double Tx, double Ty) PointAt(IReadOnlyList<Point> points, double arc)
    {
        double walked = 0;
        for (var i = 0; i + 1 < points.Count; i++)
        {
            double dx = points[i + 1].X - points[i].X, dy = points[i + 1].Y - points[i].Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length <= 0)
            {
                continue;
            }

            if (walked + length >= arc || i + 2 == points.Count)
            {
                var t = Math.Clamp((arc - walked) / length, 0, 1);
                return (points[i].X + (dx * t), points[i].Y + (dy * t), dx / length, dy / length);
            }

            walked += length;
        }

        return (points[0].X, points[0].Y, 0, 1);
    }

    /// <summary>Finds the nearest cell whose 3x3 neighbourhood is walkable (turrets reserve a 3x3 footprint).</summary>
    private static Point FindClear(GameMapTerrain terrain, double x, double y, HashSet<(int, int)> avoid)
    {
        Point best = new((byte)Math.Clamp((int)Math.Round(x), 1, 254), (byte)Math.Clamp((int)Math.Round(y), 1, 254));
        var bestDistance = double.MaxValue;
        for (var radius = 0; radius <= 10; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    var px = (int)Math.Round(x) + dx;
                    var py = (int)Math.Round(y) + dy;
                    if (px < 2 || py < 2 || px > 253 || py > 253 || avoid.Any(a => Math.Abs(a.Item1 - px) < 3 && Math.Abs(a.Item2 - py) < 3))
                    {
                        continue;
                    }

                    var clear = true;
                    for (var oy = -1; oy <= 1 && clear; oy++)
                    {
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            if (!terrain.WalkMap[px + ox, py + oy])
                            {
                                clear = false;
                                break;
                            }
                        }
                    }

                    var distance = Math.Sqrt(Math.Pow(px - x, 2) + Math.Pow(py - y, 2));
                    if (clear && distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new Point((byte)px, (byte)py);
                    }
                }
            }

            if (bestDistance < double.MaxValue)
            {
                break;
            }
        }

        return best;
    }

    private sealed record Data(IReadOnlyList<MobaLane> Lanes, IReadOnlyList<MobaTowerSpec> Towers, Point BlueNexus, Point RedNexus, Point BlueShop, Point RedShop, GameMapTerrain Terrain);
}
