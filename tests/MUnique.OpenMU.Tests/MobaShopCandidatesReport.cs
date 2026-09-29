// <copyright file="MobaShopCandidatesReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Curation helper (not part of the regular suite): for every MOBA family and tier lists the
/// real candidate weapons / sets / wings - filtered by <see cref="ItemDefinition.QualifiedCharacters"/>
/// and by a drop level close to the item the catalog already uses for that tier - so variants
/// are picked from data instead of counting columns of the initialization code by hand.
/// Run with: <c>dotnet test --filter "FullyQualifiedName~MobaShopCandidatesReport"</c>; the report
/// goes to the test output and to <c>moba-shop-candidates.txt</c> in the temp folder.
/// </summary>
[TestFixture]
[Explicit("Curation report, run by hand.")]
public class MobaShopCandidatesReport
{
    private const int DropLevelWindow = 12;
    private static readonly byte[] FamilyClassNumbers = { 3, 7, 11, 13, 17, 23, 25 };

    private GameConfiguration _gameConfiguration = null!;

    /// <summary>Loads the Season 6 configuration.</summary>
    [OneTimeSetUp]
    public async ValueTask SetupAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        this._gameConfiguration = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
    }

    /// <summary>Writes the candidate report.</summary>
    [Test]
    public void WriteReport()
    {
        var sb = new StringBuilder();
        foreach (var classNumber in FamilyClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            var family = MobaPassives.FamilyOf(classNumber);
            sb.AppendLine($"==================== {family} ({characterClass.Name}) ====================");
            foreach (var category in new[] { MobaShopCategory.Weapons, MobaShopCategory.Sets, MobaShopCategory.Wings })
            {
                foreach (var tier in new[] { MobaShopTier.T1, MobaShopTier.T2, MobaShopTier.T3 })
                {
                    this.ReportTier(sb, characterClass, family, category, tier);
                }
            }
        }

        var text = sb.ToString();
        TestContext.Out.WriteLine(text);
        File.WriteAllText(Path.Combine(Path.GetTempPath(), "moba-shop-candidates.txt"), text);
        Assert.Pass();
    }

    private void ReportTier(StringBuilder sb, CharacterClass characterClass, MobaFamily family, MobaShopCategory category, MobaShopTier tier)
    {
        var current = MobaShopCatalog.Entries
            .Where(e => e.Category == category && e.Tier == tier && (e.Families?.Contains(family) ?? false) && e.Quantity == 1)
            .ToList();
        if (current.Count == 0)
        {
            return;
        }

        // One reference drop level per tier: the mean of the pieces the catalog already uses.
        var refDefinitions = current
            .Select(e => this._gameConfiguration.Items.FirstOrDefault(d => d.Group == e.Group && d.Number == e.Number))
            .Where(d => d is not null)
            .Select(d => d!)
            .ToList();
        if (refDefinitions.Count == 0)
        {
            sb.AppendLine($"  [{category} {tier}] catálogo apunta a ítems INEXISTENTES");
            return;
        }

        var refLevel = (int)refDefinitions.Average(d => d.DropLevel);
        sb.AppendLine($"  [{category} {tier}] actual: {string.Join(", ", refDefinitions.Select(d => $"{d.Group},{d.Number} {d.Name} (DL {d.DropLevel})"))}  → ref DL {refLevel}");

        var groups = category switch
        {
            MobaShopCategory.Weapons => new byte[] { 0, 1, 2, 3, 4, 5 },
            MobaShopCategory.Sets => new byte[] { 7, 8, 9, 10, 11 },
            _ => new byte[] { 12, 13 },
        };

        var candidates = this._gameConfiguration.Items
            .Where(d => groups.Contains(d.Group) && d.QualifiedCharacters.Contains(characterClass))
            .Where(d => Math.Abs(d.DropLevel - refLevel) <= DropLevelWindow)
            .ToList();

        if (category == MobaShopCategory.Sets)
        {
            foreach (var set in candidates.GroupBy(d => d.Number).OrderBy(g => g.Average(d => d.DropLevel)))
            {
                var pieces = string.Join("/", set.OrderBy(d => d.Group).Select(d => d.Group));
                sb.AppendLine($"      set #{set.Key,-3} piezas[{pieces}] DL~{(int)set.Average(d => d.DropLevel)}  {set.OrderBy(d => d.Group).First().Name}");
            }
        }
        else
        {
            foreach (var d in candidates.OrderBy(d => d.DropLevel))
            {
                var excellent = d.PossibleItemOptions.SelectMany(o => o.PossibleOptions)
                    .Where(o => o.OptionType == ItemOptionTypes.Excellent)
                    .Select(o => o.PowerUpDefinition?.TargetAttribute?.Designation ?? "?");
                if (d.Group >= 12)
                {
                    var all = d.PossibleItemOptions.SelectMany(o => o.PossibleOptions).Select(o => $"{o.OptionType?.Name}:{o.Number}:{o.PowerUpDefinition?.TargetAttribute?.Designation ?? "?"}");
                    sb.AppendLine($"          opts[{string.Join(" | ", all)}]");
                }

                if (d.Group >= 12)
                {
                    var all = d.PossibleItemOptions.SelectMany(o => o.PossibleOptions).Select(o => $"{o.OptionType?.Name}:{o.Number}:{o.PowerUpDefinition?.TargetAttribute?.Designation ?? "?"}");
                    sb.AppendLine($"          opts[{string.Join(" | ", all)}]");
                }

                sb.AppendLine($"      {d.Group,2},{d.Number,-3} DL {d.DropLevel,-3} {d.Width}x{d.Height} skill={(d.Skill is null ? "-" : d.Skill.Name)} {d.Name}   exc[{string.Join(", ", excellent)}]");
            }
        }
    }
}
