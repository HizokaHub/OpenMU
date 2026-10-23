// <copyright file="MobaCreepPathReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using System.Text.Json;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Helper (explicit): dumps what a creep's march needs (the final waypoints per team and lane, the towers) to
/// <c>%TEMP%\moba-creep-path.json</c>, so the march can be replayed offline.
/// </summary>
[TestFixture]
public class MobaCreepPathReport
{
    /// <summary>Writes the dump.</summary>
    [Test]
    [Explicit("Diagnostic dump, not part of the suite.")]
    public void DumpPaths()
    {
        var data = new
        {
            paths = new[] { MobaTeam.Blue, MobaTeam.Red }.SelectMany(team => Enumerable.Range(0, MobaLayout.LaneCount)
                .Select(lane => new { team = team.ToString(), lane, points = MobaLayout.WaypointsFor(team, lane).Select(p => new[] { (int)p.X, (int)p.Y }).ToArray() })).ToArray(),
            towers = MobaLayout.Towers.Select(t => new { team = t.Team.ToString(), t.Lane, t.Tier, x = (int)t.Position.X, y = (int)t.Position.Y }).ToArray(),
        };
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "moba-creep-path.json"), JsonSerializer.Serialize(data));
    }
}
