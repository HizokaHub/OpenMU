// <copyright file="MobaStressArena.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The window of the map chosen for the stress test: its corner, its size and what it holds.
/// </summary>
/// <param name="X0">The west edge.</param>
/// <param name="Y0">The north edge.</param>
/// <param name="Size">The side in tiles.</param>
/// <param name="Walkable">The walkable tiles.</param>
/// <param name="Blocked">The tiles of the window which the caller marked as blocked (structure footprints).</param>
/// <param name="Hazard">The tiles of the window which the caller marked as hazardous (within reach of a turret).</param>
/// <param name="MeetsThreshold">Whether the walkable share reaches the wanted minimum.</param>
public readonly record struct MobaStressArenaPick(int X0, int Y0, int Size, int Walkable, int Blocked, int Hazard, bool MeetsThreshold)
{
    /// <summary>Gets the share of walkable tiles (0 to 1).</summary>
    public double WalkableFraction => this.Walkable / (double)(this.Size * this.Size);

    /// <summary>Gets the centre of the window.</summary>
    public (double X, double Y) Center => (this.X0 + ((this.Size - 1) / 2.0), this.Y0 + ((this.Size - 1) / 2.0));
}

/// <summary>
/// Pure geometry of the stress test arena (no map, no players): which 50x50 window of the terrain to use and where the
/// two teams stand. Kept free of game types so it can be tested on synthetic grids.
/// </summary>
public static class MobaStressArena
{
    /// <summary>The side of the arena window in tiles.</summary>
    public const int Size = 50;

    /// <summary>The walkable share the window should reach.</summary>
    public const double MinWalkableFraction = 0.90;

    private const int MapSize = 256;

    private const double MapCenter = 127.5;

    /// <summary>
    /// Picks the window of the terrain for the test. The order is total and deterministic: fewest blocked tiles, then windows
    /// which reach the walkable minimum (when none does, the one with the most walkable tiles), fewest hazard tiles, the highest
    /// walkable share (in steps of <paramref name="fractionQuantum"/>), the nearest to the map centre, then the lowest Y and X.
    /// </summary>
    /// <param name="aiGrid">The terrain grid, 256x256; a tile is walkable when its value is not 0.</param>
    /// <param name="blocked">Tiles a creep may not stand on (structure footprints), or <see langword="null"/>.</param>
    /// <param name="hazard">Tiles which would hurt the test (turret reach), or <see langword="null"/>.</param>
    /// <param name="size">The side of the window.</param>
    /// <param name="minFraction">The walkable share the window should reach.</param>
    /// <param name="fractionQuantum">The step in which the walkable share is compared (so a tiny difference does not outweigh centrality).</param>
    /// <returns>The chosen window.</returns>
    public static MobaStressArenaPick Pick(
        byte[,] aiGrid,
        bool[,]? blocked = null,
        bool[,]? hazard = null,
        int size = Size,
        double minFraction = MinWalkableFraction,
        double fractionQuantum = 0.01)
    {
        ValidateGrid(aiGrid);
        if (size is < 8 or > 128)
        {
            throw new ArgumentException("The size must be between 8 and 128.", nameof(size));
        }

        var walkSum = Integral((x, y) => aiGrid[x, y] != 0);
        var blockedSum = Integral((x, y) => blocked is not null && blocked[x, y]);
        var hazardSum = Integral((x, y) => hazard is not null && hazard[x, y]);
        var last = MapSize - size;
        var area = size * size;
        var needed = (int)Math.Ceiling((minFraction * area) - 1e-9);

        // First pass: the least blocked count, and whether a window with that count reaches the minimum.
        var minBlocked = int.MaxValue;
        for (var y0 = 0; y0 <= last; y0++)
        {
            for (var x0 = 0; x0 <= last; x0++)
            {
                minBlocked = Math.Min(minBlocked, Window(blockedSum, x0, y0, size));
            }
        }

        var anyMeets = false;
        for (var y0 = 0; y0 <= last && !anyMeets; y0++)
        {
            for (var x0 = 0; x0 <= last; x0++)
            {
                if (Window(blockedSum, x0, y0, size) == minBlocked && Window(walkSum, x0, y0, size) >= needed)
                {
                    anyMeets = true;
                    break;
                }
            }
        }

        // Second pass: the best of those windows.
        MobaStressArenaPick best = default;
        (long A, long B, double Distance) bestKey = default;
        var haveBest = false;
        for (var y0 = 0; y0 <= last; y0++)
        {
            for (var x0 = 0; x0 <= last; x0++)
            {
                if (Window(blockedSum, x0, y0, size) != minBlocked)
                {
                    continue;
                }

                var walkable = Window(walkSum, x0, y0, size);
                if (anyMeets && walkable < needed)
                {
                    continue;
                }

                var hz = Window(hazardSum, x0, y0, size);
                var fraction = (long)Math.Floor((walkable / (double)area / fractionQuantum) + 1e-9);

                // Lower is better in every component. When nothing reaches the minimum, the most walkable window wins before anything else.
                var key = anyMeets
                    ? (A: (long)hz, B: -fraction, Distance: DistanceToCenter(x0, y0, size))
                    : (A: -(long)walkable, B: (long)hz, Distance: DistanceToCenter(x0, y0, size));

                if (!haveBest || Better(key, bestKey))
                {
                    haveBest = true;
                    best = new MobaStressArenaPick(x0, y0, size, walkable, minBlocked, hz, walkable >= needed);
                    bestKey = key;
                }
            }
        }

        return best;
    }

    /// <summary>Marks a square of tiles around each centre (clipped to the map).</summary>
    /// <param name="centers">The centres.</param>
    /// <param name="margin">Tiles around each centre; the square is <c>2 * margin + 1</c> wide.</param>
    /// <returns>A 256x256 mask.</returns>
    public static bool[,] FootprintMask(IEnumerable<(int X, int Y)> centers, int margin)
    {
        var mask = new bool[MapSize, MapSize];
        foreach (var (cx, cy) in centers)
        {
            for (var y = Math.Max(0, cy - margin); y <= Math.Min(MapSize - 1, cy + margin); y++)
            {
                for (var x = Math.Max(0, cx - margin); x <= Math.Min(MapSize - 1, cx + margin); x++)
                {
                    mask[x, y] = true;
                }
            }
        }

        return mask;
    }

    /// <summary>
    /// Gets the walkable tiles of the west or east third of the window, from the outer edge toward the centre, and inside each
    /// column from the middle row outward.
    /// </summary>
    /// <param name="aiGrid">The terrain grid.</param>
    /// <param name="arena">The window.</param>
    /// <param name="westSide"><see langword="true"/> for the west third, <see langword="false"/> for the east third.</param>
    /// <returns>The tiles in the order they should be filled.</returns>
    public static IReadOnlyList<(int X, int Y)> SpawnSlots(byte[,] aiGrid, MobaStressArenaPick arena, bool westSide)
    {
        ValidateGrid(aiGrid);
        var third = arena.Size / 3;
        var yc = arena.Y0 + ((arena.Size - 1) / 2);
        var slots = new List<(int X, int Y)>();
        for (var i = 0; i < third; i++)
        {
            var x = westSide ? arena.X0 + i : arena.X0 + arena.Size - 1 - i;
            var rows = Enumerable.Range(arena.Y0, arena.Size)
                .Where(y => aiGrid[x, y] != 0)
                .OrderBy(y => Math.Abs(y - yc))
                .ThenBy(y => y);
            foreach (var y in rows)
            {
                slots.Add((x, y));
            }
        }

        return slots;
    }

    /// <summary>Finds the walkable tile nearest to a position by rings (like the wave spawner does).</summary>
    /// <param name="aiGrid">The terrain grid.</param>
    /// <param name="x">The x coordinate.</param>
    /// <param name="y">The y coordinate.</param>
    /// <param name="radius">The largest ring.</param>
    /// <returns>The tile, or the clamped position if none is near.</returns>
    public static (int X, int Y) NearestWalkable(byte[,] aiGrid, int x, int y, int radius = 6)
    {
        ValidateGrid(aiGrid);
        int cx = Math.Clamp(x, 0, MapSize - 1), cy = Math.Clamp(y, 0, MapSize - 1);
        for (var r = 0; r <= radius; r++)
        {
            for (var dy = -r; dy <= r; dy++)
            {
                for (var dx = -r; dx <= r; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx is >= 0 and < MapSize && ny is >= 0 and < MapSize && Math.Max(Math.Abs(dx), Math.Abs(dy)) == r && aiGrid[nx, ny] != 0)
                    {
                        return (nx, ny);
                    }
                }
            }
        }

        return (cx, cy);
    }

    /// <summary>Gets the tile a creep marches to: three tiles from the far edge of the window, in its own row.</summary>
    /// <param name="aiGrid">The terrain grid.</param>
    /// <param name="arena">The window.</param>
    /// <param name="fromWest"><see langword="true"/> if the creep was born in the west (so it marches east).</param>
    /// <param name="row">The row (Y) the creep was born on.</param>
    /// <returns>A walkable tile.</returns>
    public static (int X, int Y) Goal(byte[,] aiGrid, MobaStressArenaPick arena, bool fromWest, int row)
    {
        var x = fromWest ? arena.X0 + arena.Size - 4 : arena.X0 + 3;
        return NearestWalkable(aiGrid, x, row);
    }

    /// <summary>Gets a walkable tile just outside the north edge of the window, where the GM watches from.</summary>
    /// <param name="aiGrid">The terrain grid.</param>
    /// <param name="arena">The window.</param>
    /// <returns>A walkable tile.</returns>
    public static (int X, int Y) EdgeSpot(byte[,] aiGrid, MobaStressArenaPick arena)
    {
        var y = arena.Y0 - 6 >= 0 ? arena.Y0 - 6 : Math.Min(MapSize - 1, arena.Y0 + arena.Size + 6);
        return NearestWalkable(aiGrid, arena.X0 + (arena.Size / 2), y);
    }

    private static bool Better((long A, long B, double Distance) candidate, (long A, long B, double Distance) best)
    {
        // The scan order (Y then X ascending) breaks full ties, because only a strictly better key replaces the best.
        if (candidate.A != best.A)
        {
            return candidate.A < best.A;
        }

        if (candidate.B != best.B)
        {
            return candidate.B < best.B;
        }

        return candidate.Distance < best.Distance;
    }

    private static double DistanceToCenter(int x0, int y0, int size)
    {
        var dx = x0 + ((size - 1) / 2.0) - MapCenter;
        var dy = y0 + ((size - 1) / 2.0) - MapCenter;
        return (dx * dx) + (dy * dy);
    }

    private static void ValidateGrid(byte[,] aiGrid)
    {
        ArgumentNullException.ThrowIfNull(aiGrid);
        if (aiGrid.GetLength(0) != MapSize || aiGrid.GetLength(1) != MapSize)
        {
            throw new ArgumentException("The terrain grid must be 256x256.", nameof(aiGrid));
        }
    }

    /// <summary>Builds a summed-area table: <c>sum[x + 1, y + 1]</c> holds the count of marked tiles in <c>[0..x, 0..y]</c>.</summary>
    private static int[,] Integral(Func<int, int, bool> marked)
    {
        var sum = new int[MapSize + 1, MapSize + 1];
        for (var y = 0; y < MapSize; y++)
        {
            var row = 0;
            for (var x = 0; x < MapSize; x++)
            {
                row += marked(x, y) ? 1 : 0;
                sum[x + 1, y + 1] = sum[x + 1, y] + row;
            }
        }

        return sum;
    }

    private static int Window(int[,] sum, int x0, int y0, int size)
        => sum[x0 + size, y0 + size] - sum[x0, y0 + size] - sum[x0 + size, y0] + sum[x0, y0];
}
