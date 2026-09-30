// <copyright file="MobaOffhandReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>Curation helper: lists off-hand items (shields, books) and pets with their qualified classes.</summary>
[TestFixture]
[Explicit("Curation report, run by hand.")]
public class MobaOffhandReport
{
    /// <summary>Writes the report to the temp folder.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async ValueTask WriteReportAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        var config = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
        var sb = new StringBuilder();
        foreach (var d in config.Items.Where(i => i.Group == 6 || (i.Group == 5 && i.QualifiedCharacters.Any(c => c.Number == 23)) || i.Group == 13 && i.Number <= 130 && i.Number != 8 && i.Number != 12).OrderBy(i => i.Group).ThenBy(i => i.Number))
        {
            var classes = string.Join(",", d.QualifiedCharacters.Select(c => c.Number).OrderBy(n => n));
            sb.AppendLine($"{d.Group}/{d.Number} '{d.Name}' drop={d.DropLevel} size={d.Width}x{d.Height} maxLvl={d.MaximumItemLevel} skill={d.Skill?.Number} classes=[{classes}]");
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "moba-offhand.txt"), sb.ToString());
    }
}
