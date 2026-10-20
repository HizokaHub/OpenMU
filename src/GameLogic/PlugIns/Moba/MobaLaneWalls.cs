// <copyright file="MobaLaneWalls.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// A rectangle of terrain tiles which is turned into wall.
/// </summary>
/// <param name="X">The left tile.</param>
/// <param name="Y">The top tile.</param>
/// <param name="Width">The width in tiles.</param>
/// <param name="Height">The height in tiles.</param>
public readonly record struct MobaWallRect(byte X, byte Y, byte Width, byte Height);

/// <summary>
/// The walls which close the side lanes in the formats which play the middle lane only (1v1 and ARAM): two long walls along
/// both sides of the middle lane and one across each end behind the bases, which turn the middle of the arena into a corridor.
/// They are plain tile rectangles, so the client builds exactly the same walls from the list the server sends every second
/// (the same way as the barrier of the countdown). They stand for the whole match.
/// </summary>
public static class MobaLaneWalls
{
    /// <summary>The tile column the corridor is centred on (the middle lane runs down the middle of the arena).</summary>
    public const int CenterX = 118;

    /// <summary>Tiles from the centre to the outer edge of each wall; the corridor is twice that, plus one, wide.</summary>
    public const int HalfWidth = 14;

    /// <summary>Thickness of the walls, in tiles.</summary>
    public const int Thickness = 2;

    /// <summary>The first tile row of the corridor (behind the blue base).</summary>
    public const int TopY = 3;

    /// <summary>The last tile row of the corridor (behind the red base).</summary>
    public const int BottomY = 252;

    /// <summary>
    /// Gets the wall rectangles of a format.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <returns>The rectangles; none for the formats which play every lane.</returns>
    public static IReadOnlyList<MobaWallRect> RectsFor(MobaFormat format)
    {
        if (!MobaFormats.MiddleLaneOnly(format))
        {
            return [];
        }

        var left = CenterX - HalfWidth;
        var right = CenterX + HalfWidth - Thickness + 1;
        var width = (2 * HalfWidth) + 1;
        var height = BottomY - TopY + 1;
        return
        [
            new MobaWallRect((byte)left, (byte)TopY, (byte)Thickness, (byte)height),
            new MobaWallRect((byte)right, (byte)TopY, (byte)Thickness, (byte)height),
            new MobaWallRect((byte)left, (byte)TopY, (byte)width, (byte)Thickness),
            new MobaWallRect((byte)left, (byte)(BottomY - Thickness + 1), (byte)width, (byte)Thickness),
        ];
    }

    /// <summary>
    /// Gets whether a tile is part of the walls of a format.
    /// </summary>
    /// <param name="format">The format.</param>
    /// <param name="x">The tile column.</param>
    /// <param name="y">The tile row.</param>
    /// <returns><see langword="true"/> if the tile is inside a wall rectangle.</returns>
    public static bool IsWall(MobaFormat format, int x, int y)
        => RectsFor(format).Any(r => x >= r.X && x < r.X + r.Width && y >= r.Y && y < r.Y + r.Height);

    /// <summary>
    /// Puts the walls of the arena's format up on the terrain of the map (does nothing for the formats without walls, or if they are up).
    /// </summary>
    /// <param name="map">The arena map.</param>
    public static void Raise(GameMap map)
    {
        var arena = MobaArena.Of(map);
        if (arena.LaneWallTiles.Count > 0)
        {
            return;
        }

        foreach (var rect in RectsFor(arena.Format))
        {
            for (var x = rect.X; x < rect.X + rect.Width; x++)
            {
                for (var y = rect.Y; y < rect.Y + rect.Height; y++)
                {
                    if (!map.Terrain.WalkMap[x, y])
                    {
                        continue;
                    }

                    map.Terrain.WalkMap[x, y] = false;
                    map.Terrain.UpdateAiGridValue((byte)x, (byte)y);
                    arena.LaneWallTiles.Add(new Point((byte)x, (byte)y));
                }
            }
        }
    }

    /// <summary>
    /// Takes the walls of an arena down (the match is over and the arena is being cleaned).
    /// </summary>
    /// <param name="map">The arena map.</param>
    public static void Lower(GameMap map)
    {
        var arena = MobaArena.Of(map);
        foreach (var tile in arena.LaneWallTiles)
        {
            map.Terrain.WalkMap[tile.X, tile.Y] = true;
            map.Terrain.UpdateAiGridValue(tile.X, tile.Y);
        }

        arena.LaneWallTiles.Clear();
    }

    /// <summary>
    /// Tells the champions of every arena which has walls where the walls are (about once a second; the client drops them when the news stop).
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        var players = (await gameContext.GetPlayersAsync().ConfigureAwait(false)).Where(p => p.IsMobaClone && p.CurrentMap is not null).ToList();
        foreach (var group in players.GroupBy(p => p.CurrentMap!))
        {
            if (group.Key.MapId != MobaCloneFactory.ArenaMapNumber || MobaArena.Of(group.Key).LaneWallTiles.Count == 0)
            {
                continue;
            }

            var rects = RectsFor(MobaArena.Of(group.Key).Format);
            foreach (var player in group)
            {
                await player.InvokeViewPlugInAsync<IMobaWallsPlugIn>(p => p.ShowWallsAsync(rects)).ConfigureAwait(false);
            }
        }
    }
}
