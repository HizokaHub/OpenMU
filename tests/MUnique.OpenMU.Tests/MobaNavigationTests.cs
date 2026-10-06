// <copyright file="MobaNavigationTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Tests the whole-map A* of the MOBA bots on the real arena mask.
/// </summary>
[TestFixture]
public class MobaNavigationTests
{
    /// <summary>
    /// Paths exist between the spots where the first 3-lane game left bots stuck, and every step is a legal move.
    /// </summary>
    [Test]
    public void FindsLegalPathsAcrossTheArena()
    {
        var grid = new GameMapTerrain(new GameMapDefinition { Number = 200 }).AIgrid;
        (Point From, Point To)[] cases =
        {
            (new Point(70, 153), new Point(24, 165)),
            (new Point(35, 98), new Point(111, 241)),
            (new Point(25, 108), new Point(118, 232)),
            (new Point(220, 110), new Point(25, 100)),
        };

        foreach (var (from, to) in cases)
        {
            var path = MobaNavigation.FindPath(grid, from, to);
            Assert.That(path, Is.Not.Null, $"{from} -> {to}");
            for (var i = 0; i < path!.Count; i++)
            {
                Assert.That(grid[path[i].X, path[i].Y], Is.Not.EqualTo(0));
                if (i > 0)
                {
                    Assert.That(Math.Max(Math.Abs(path[i].X - path[i - 1].X), Math.Abs(path[i].Y - path[i - 1].Y)), Is.EqualTo(1));
                }
            }
        }
    }
}
