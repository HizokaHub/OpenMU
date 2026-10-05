// <copyright file="MobaWalkMaskTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;

/// <summary>
/// Tests the MOBA arena walk mask shared by the server and the client.
/// </summary>
[TestFixture]
public class MobaWalkMaskTests
{
    private static readonly (int X, int Y)[] KeyPoints =
    {
        (116, 44), (112, 57), (116, 60), (117, 90), (111, 90), (116, 110), (122, 120), (116, 120),
        (120, 160), (113, 160), (116, 160), (117, 188), (112, 188), (116, 205), (112, 208), (116, 224),
    };

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
        foreach (var (x, y) in KeyPoints)
        {
            Assert.That(terrain.WalkMap[x, y], Is.True, $"({x},{y}) must be walkable");
        }

        var seen = new bool[256, 256];
        var stack = new Stack<(int X, int Y)>();
        stack.Push(KeyPoints[0]);
        seen[KeyPoints[0].X, KeyPoints[0].Y] = true;
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

        foreach (var (x, y) in KeyPoints)
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
