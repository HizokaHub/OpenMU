// <copyright file="MobaOccupancyGrid.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Collections.Generic;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Process-wide, per-map dynamic tile-occupancy grid for MOBA units (lane creeps and
/// structures). A unit atomically claims the tile it stands on and each tile of the
/// walk chunk it commits to; another unit's claim then fails and it routes around or
/// waits. This is the "hard" collision layer on top of the static terrain grid - it
/// removes the same-tick race two creeps could otherwise lose by both picking the same
/// free tile.
/// </summary>
/// <remarks>
/// Champions are NOT in this grid (they move on client-driven paths); creep AI still
/// avoids them with a cheap position snapshot. A dedicated match context will own this
/// grid per match later.
/// </remarks>
public static class MobaOccupancyGrid
{
    private static readonly ConcurrentDictionary<(GameMap Map, int Tile), object> Owners = new();

    private static int Index(Point p) => (p.Y << 8) | p.X;

    /// <summary>Whether the tile is part of a structure's permanent footprint (turret / nexus).</summary>
    /// <param name="map">The map.</param>
    /// <param name="tile">The tile.</param>
    /// <returns><see langword="true"/> if a structure holds the tile.</returns>
    public static bool IsStructureTile(GameMap map, Point tile)
        => Owners.TryGetValue((map, Index(tile)), out var owner) && owner is MobaStructureIntelligence;

    /// <summary>Whether the tile is free or already owned by <paramref name="self"/>.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tile">The tile.</param>
    /// <param name="self">The unit asking.</param>
    /// <returns><see langword="true"/> if the tile can be stepped onto.</returns>
    public static bool IsFree(GameMap map, Point tile, object self)
        => !Owners.TryGetValue((map, Index(tile)), out var owner) || ReferenceEquals(owner, self);

    /// <summary>
    /// Atomically claims a tile for <paramref name="owner"/>. Succeeds if the tile is free
    /// or already owned by the same owner.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="tile">The tile.</param>
    /// <param name="owner">The claiming unit.</param>
    /// <returns><see langword="true"/> if the tile is now owned by <paramref name="owner"/>.</returns>
    public static bool TryClaim(GameMap map, Point tile, object owner)
    {
        var key = (map, Index(tile));
        if (Owners.TryGetValue(key, out var existing))
        {
            return ReferenceEquals(existing, owner);
        }

        return Owners.TryAdd(key, owner) || (Owners.TryGetValue(key, out existing) && ReferenceEquals(existing, owner));
    }

    /// <summary>Releases a tile, but only if <paramref name="owner"/> currently holds it.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tile">The tile.</param>
    /// <param name="owner">The unit releasing.</param>
    public static void Release(GameMap map, Point tile, object owner)
        => ((ICollection<KeyValuePair<(GameMap, int), object>>)Owners)
            .Remove(new KeyValuePair<(GameMap, int), object>((map, Index(tile)), owner));

    /// <summary>Releases every tile held by <paramref name="owner"/> on the map.</summary>
    /// <param name="map">The map.</param>
    /// <param name="owner">The unit.</param>
    public static void ReleaseAll(GameMap map, object owner)
    {
        foreach (var kv in Owners)
        {
            if (ReferenceEquals(kv.Key.Map, map) && ReferenceEquals(kv.Value, owner))
            {
                ((ICollection<KeyValuePair<(GameMap, int), object>>)Owners).Remove(kv);
            }
        }
    }
}
