// <copyright file="MobaLayoutTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the MOBA arena layout (lanes, generated turrets, nexuses, shops).
/// </summary>
[TestFixture]
public class MobaLayoutTests
{
    /// <summary>
    /// Every lane has waypoints from the blue base to the red base.
    /// </summary>
    [Test]
    public void LanesRunFromBlueBaseToRedBase()
    {
        Assert.That(MobaLayout.Lanes, Has.Count.EqualTo(3));
        var blue = MobaLayout.NexusOf(MobaTeam.Blue);
        var red = MobaLayout.NexusOf(MobaTeam.Red);
        foreach (var lane in MobaLayout.Lanes)
        {
            Assert.That(lane.Waypoints.Count, Is.GreaterThanOrEqualTo(2), lane.Name);
            var first = lane.Waypoints[0];
            var last = lane.Waypoints[^1];
            Assert.That(Dist(first, blue), Is.LessThan(Dist(first, red)), $"{lane.Name} must start near the blue base");
            Assert.That(Dist(last, red), Is.LessThan(Dist(last, blue)), $"{lane.Name} must end near the red base");
        }
    }

    /// <summary>
    /// Each lane gets two pairs of turrets per team (outer farther from the own base than inner).
    /// </summary>
    [Test]
    public void EveryLaneHasFourTurretsPerTeam()
    {
        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            var nexus = MobaLayout.NexusOf(team);
            for (var lane = 0; lane < MobaLayout.LaneCount; lane++)
            {
                var towers = MobaLayout.Towers.Where(t => t.Team == team && t.Lane == lane).ToArray();
                Assert.That(towers, Has.Length.EqualTo(4), $"{team} lane {lane}");
                Assert.That(towers.Count(t => t.Tier == 0), Is.EqualTo(2));
                Assert.That(towers.Count(t => t.Tier == 1), Is.EqualTo(2));
                Assert.That(towers.Where(t => t.Tier == 0).Min(t => Dist(t.Position, nexus)),
                    Is.GreaterThan(towers.Where(t => t.Tier == 1).Max(t => Dist(t.Position, nexus)) - 1), $"{team} lane {lane}: outer turrets must be farther than the inner ones");
            }
        }

        Assert.That(MobaLayout.Towers, Has.Count.EqualTo(24));
    }

    /// <summary>
    /// Turrets stand near their lane and apart from each other.
    /// </summary>
    [Test]
    public void TurretsStandOnTheirLaneAndDoNotOverlap()
    {
        foreach (var tower in MobaLayout.Towers)
        {
            var distance = MobaLayout.DistanceToPolyline(MobaLayout.Lanes[tower.Lane].Waypoints, tower.Position.X, tower.Position.Y);
            Assert.That(distance, Is.LessThan(8), $"{tower}");
        }

        var towers = MobaLayout.Towers;
        for (var i = 0; i < towers.Count; i++)
        {
            for (var j = i + 1; j < towers.Count; j++)
            {
                Assert.That(Dist(towers[i].Position, towers[j].Position), Is.GreaterThanOrEqualTo(3), $"{towers[i]} / {towers[j]}");
            }
        }
    }

    /// <summary>
    /// Creeps and bots walk straight between waypoints, so the segment between two consecutive waypoints must be walkable.
    /// </summary>
    [Test]
    public void LaneSegmentsNeverCrossWalls()
    {
        var terrain = new MUnique.OpenMU.GameLogic.GameMapTerrain(new MUnique.OpenMU.DataModel.Configuration.GameMapDefinition { Number = 200 });
        foreach (var lane in MobaLayout.Lanes)
        {
            for (var i = 0; i + 1 < lane.Waypoints.Count; i++)
            {
                var a = lane.Waypoints[i];
                var b = lane.Waypoints[i + 1];
                var steps = (int)Math.Ceiling(Dist(a, b) * 2);
                for (var s = 0; s <= steps; s++)
                {
                    var x = (int)Math.Round(a.X + ((b.X - a.X) * s / (double)Math.Max(1, steps)));
                    var y = (int)Math.Round(a.Y + ((b.Y - a.Y) * s / (double)Math.Max(1, steps)));
                    Assert.That(terrain.WalkMap[x, y], Is.True, $"{lane.Name}: segment {a}->{b} crosses a wall at ({x},{y})");
                }
            }
        }
    }

    /// <summary>
    /// Lane waypoints are close together so bots that skip ahead cannot cut corners.
    /// </summary>
    [Test]
    public void LaneWaypointsAreDense()
    {
        foreach (var lane in MobaLayout.Lanes)
        {
            for (var i = 0; i + 1 < lane.Waypoints.Count; i++)
            {
                Assert.That(Dist(lane.Waypoints[i], lane.Waypoints[i + 1]), Is.LessThanOrEqualTo(8), lane.Name);
            }
        }
    }

    private static double Dist(MUnique.OpenMU.Pathfinding.Point a, MUnique.OpenMU.Pathfinding.Point b)
        => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
