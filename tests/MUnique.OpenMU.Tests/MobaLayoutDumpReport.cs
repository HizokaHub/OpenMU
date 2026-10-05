// <copyright file="MobaLayoutDumpReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using System.Text.Json;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Helper (explicit): dumps the real lane waypoints and generated turrets to <c>%TEMP%\moba-layout-dump.json</c>.
/// </summary>
[TestFixture]
public class MobaLayoutDumpReport
{
    /// <summary>Writes the dump.</summary>
    [Test]
    [Explicit("Diagnostic dump, not part of the suite.")]
    public void DumpLayout()
    {
        var data = new
        {
            lanes = MobaLayout.Lanes.Select(l => new { l.Name, points = l.Waypoints.Select(p => new[] { (int)p.X, (int)p.Y }).ToArray() }).ToArray(),
            towers = MobaLayout.Towers.Select(t => new { team = t.Team.ToString(), t.Lane, t.Tier, x = (int)t.Position.X, y = (int)t.Position.Y }).ToArray(),
        };
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "moba-layout-dump.json"), JsonSerializer.Serialize(data));
    }
}
