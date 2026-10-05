// <copyright file="MobaWalkMaskTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the MOBA arena walk mask shared by the server and the client.
/// </summary>
[TestFixture]
public class MobaWalkMaskTests
{
    private static (int X, int Y)[] KeyPoints()
    {
        var points = new List<(int, int)>();
        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            points.Add((MobaLayout.NexusOf(team).X, MobaLayout.NexusOf(team).Y));
            points.Add((MobaLayout.ShopOf(team).X, MobaLayout.ShopOf(team).Y));
        }

        points.AddRange(MobaLayout.Lanes.SelectMany(l => l.Waypoints).Select(p => ((int)p.X, (int)p.Y)));
        points.AddRange(MobaLayout.Towers.Select(tw => ((int)tw.Position.X, (int)tw.Position.Y)));
        return points.ToArray();
    }

    /// <summary>
    /// The arena always uses the embedded mask, whatever the database holds.
    /// </summary>
    [Test]
    public void ArenaUsesEmbeddedMaskInsteadOfDatabaseCopy()
    {
        var map = new GameMapDefinition { Number = 200, TerrainData = new byte[3 + 256 * 256] };
        var data = GameMapTerrain.GetTerrainData(map);

        Assert.That(data, Is.Not.Null);
        Assert.That(data!.Length, Is.EqualTo(3 + 256 * 256));
        Assert.That(data.Take(3), Is.EqualTo(new byte[] { 0, 255, 255 }));
        Assert.That(data.Skip(3).Count(b => b == 4), Is.GreaterThan(0), "the embedded mask must contain walls");
    }

    /// <summary>
    /// Other maps keep their database terrain.
    /// </summary>
    [Test]
    public void OtherMapsKeepTheirTerrain()
    {
        var bytes = new byte[3 + 256 * 256];
        var map = new GameMapDefinition { Number = 34, TerrainData = bytes };

        Assert.That(GameMapTerrain.GetTerrainData(map), Is.SameAs(bytes));
    }

    /// <summary>
    /// Structures, shops and lane waypoints must be walkable and connected to each other.
    /// </summary>
    [Test]
    public void KeyPointsAreWalkableAndConnected()
    {
        var terrain = new GameMapTerrain(new GameMapDefinition { Number = 200 });
        var keyPoints = KeyPoints();
        foreach (var (x, y) in keyPoints)
        {
            Assert.That(terrain.WalkMap[x, y], Is.True, $"({x},{y}) must be walkable");
        }

        var seen = new bool[256, 256];
        var stack = new Stack<(int X, int Y)>();
        stack.Push(keyPoints[0]);
        seen[keyPoints[0].X, keyPoints[0].Y] = true;
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx is >= 0 and < 256 && ny is >= 0 and < 256 && !seen[nx, ny] && terrain.WalkMap[nx, ny])
                {
                    seen[nx, ny] = true;
                    stack.Push((nx, ny));
                }
            }
        }

        foreach (var (x, y) in keyPoints)
        {
            Assert.That(seen[x, y], Is.True, $"({x},{y}) must be reachable from the blue nexus");
        }
    }

    /// <summary>
    /// The client's copy of the mask (when the client repository is next to this one) must be identical.
    /// </summary>
    [Test]
    public void ClientCopyMatchesServerMask()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? clientFile = null;
        while (dir is not null && clientFile is null)
        {
            var candidate = Path.Combine(dir.FullName, "mu-main", "src", "bin", "Data", "World200", "MobaWalkMask.att");
            if (File.Exists(candidate))
            {
                clientFile = candidate;
            }

            dir = dir.Parent;
        }

        if (clientFile is null)
        {
            Assert.Ignore("The client repository (mu-main) was not found next to this one.");
        }

        var server = GameMapTerrain.GetTerrainData(new GameMapDefinition { Number = 200 });
        Assert.That(File.ReadAllBytes(clientFile!), Is.EqualTo(server), "client and server walk masks differ");
    }
}
