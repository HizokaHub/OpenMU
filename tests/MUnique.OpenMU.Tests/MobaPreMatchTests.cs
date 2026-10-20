// <copyright file="MobaPreMatchTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Tests the barrier which holds the champions in their base during the countdown before a match.
/// </summary>
[TestFixture]
public class MobaPreMatchTests
{
    private static readonly Point Center = new(118, 40);

    /// <summary>
    /// Every tile of the ring lies between the radius and the radius plus the thickness.
    /// </summary>
    [Test]
    public void RingLiesAtTheRadius()
    {
        var tiles = MobaPreMatch.BarrierTiles(Center, (_, _) => true);

        Assert.That(tiles, Is.Not.Empty);
        foreach (var tile in tiles)
        {
            var distance = Math.Sqrt(Math.Pow(tile.X - Center.X, 2) + Math.Pow(tile.Y - Center.Y, 2));
            Assert.That(distance, Is.GreaterThanOrEqualTo(MobaPreMatch.BarrierRadius).And.LessThan(MobaPreMatch.BarrierRadius + MobaPreMatch.BarrierThickness));
        }
    }

    /// <summary>
    /// The ring is closed: nobody can walk (even diagonally) from the centre to a tile beyond it.
    /// </summary>
    [Test]
    public void RingHoldsEverybodyIn()
    {
        var wall = MobaPreMatch.BarrierTiles(Center, (_, _) => true).Select(t => ((int)t.X, (int)t.Y)).ToHashSet();
        var seen = new HashSet<(int X, int Y)> { (Center.X, Center.Y) };
        var open = new Queue<(int X, int Y)>();
        open.Enqueue((Center.X, Center.Y));
        while (open.Count > 0)
        {
            var (x, y) = open.Dequeue();
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    var next = (X: x + dx, Y: y + dy);
                    if ((dx == 0 && dy == 0) || next.X is < 0 or > 255 || next.Y is < 0 or > 255 || wall.Contains(next) || !seen.Add(next))
                    {
                        continue;
                    }

                    open.Enqueue(next);
                }
            }
        }

        var farthest = seen.Max(p => Math.Sqrt(Math.Pow(p.X - Center.X, 2) + Math.Pow(p.Y - Center.Y, 2)));
        Assert.That(farthest, Is.LessThan(MobaPreMatch.BarrierRadius), "nothing beyond the ring can be reached");
    }

    /// <summary>
    /// Tiles which are not walkable anyway get no wall.
    /// </summary>
    [Test]
    public void OnlyWalkableTilesGetAWall()
    {
        var tiles = MobaPreMatch.BarrierTiles(Center, (x, _) => x < Center.X);

        Assert.That(tiles, Is.Not.Empty);
        Assert.That(tiles.All(t => t.X < Center.X), Is.True);
    }

    /// <summary>
    /// The countdown is over once the time ran out, and an arena without countdown is never in it.
    /// </summary>
    [Test]
    public void ArenaKnowsWhenTheCountdownIsOn()
    {
        var arena = MobaArena.Standalone();
        Assert.That(arena.IsPreMatch, Is.False);

        arena.PreMatchUntilUtc = DateTime.UtcNow.AddSeconds(5);
        Assert.That(arena.IsPreMatch, Is.True);

        arena.PreMatchUntilUtc = DateTime.UtcNow.AddSeconds(-1);
        Assert.That(arena.IsPreMatch, Is.False);
    }
}
