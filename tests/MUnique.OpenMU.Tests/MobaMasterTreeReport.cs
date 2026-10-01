// <copyright file="MobaMasterTreeReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Curation helper (not part of the regular suite): lists, per MOBA family, every master skill node
/// (root, rank, max level, target attribute, aggregation, formula) so the nodes that feed the MOBA
/// combat model can be picked from data. Run with
/// <c>dotnet test --filter "FullyQualifiedName~MobaMasterTreeReport"</c>; the report goes to
/// <c>moba-master-tree.txt</c> in the temp folder.
/// </summary>
[TestFixture]
[Explicit("Curation report, run by hand.")]
public class MobaMasterTreeReport
{
    private static readonly byte[] MasterClassNumbers = { 3, 7, 11, 13, 17, 23, 25 };

    private GameConfiguration _gameConfiguration = null!;

    /// <summary>Loads the Season 6 configuration.</summary>
    [OneTimeSetUp]
    public async ValueTask SetupAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        this._gameConfiguration = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
    }

    /// <summary>Writes the master tree report.</summary>
    [Test]
    public void WriteReport()
    {
        var sb = new StringBuilder();
        foreach (var classNumber in MasterClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            var nodes = this._gameConfiguration.Skills
                .Where(s => s.MasterDefinition is not null && s.QualifiedCharacters.Contains(characterClass))
                .OrderBy(s => s.MasterDefinition!.Root?.Name.ToString()).ThenBy(s => s.MasterDefinition!.Rank).ThenBy(s => s.Number)
                .ToList();
            sb.AppendLine($"==================== {MobaPassives.FamilyOf(classNumber)} ({characterClass.Name}) - {nodes.Count} nodes, {nodes.Sum(n => n.MasterDefinition!.MaximumLevel)} points to max ====================");
            foreach (var group in nodes.GroupBy(n => n.MasterDefinition!.Root?.Name.ToString() ?? "?"))
            {
                sb.AppendLine($"-- {group.Key}: {group.Count()} nodes, {group.Sum(n => n.MasterDefinition!.MaximumLevel)} points");
                foreach (var skill in group)
                {
                    var d = skill.MasterDefinition!;
                    var kind = d.ReplacedSkill is not null ? "ACTIVE(replaces " + d.ReplacedSkill.Number + ")" : (d.TargetAttribute is null ? "ACTIVE" : "passive");
                    sb.AppendLine($"   #{skill.Number,3} r{d.Rank} max{d.MaximumLevel,2} min{d.MinimumLevel} {MobaMasterTree.KindOf(skill),-12} {(MobaMasterTree.IsAllowed(skill) ? "ON " : "off")} {kind,-22} {skill.Name} -> {d.TargetAttribute?.Designation ?? "-"} [{d.Aggregation}] f='{d.ValueFormula}'");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("==================== Resumen por tipo (capacidad en puntos de nodo; tope = lo que da todo el término árbol) ====================");
        foreach (var classNumber in MasterClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            sb.AppendLine($"{MobaPassives.FamilyOf(classNumber),-16}" + string.Join(" | ", Enum.GetValues<MobaTreeKind>().Select(k =>
            {
                var nodes = this._gameConfiguration.Skills.Where(s => s.MasterDefinition is not null && MobaMasterTree.KindOf(s) == k && s.QualifiedCharacters.Contains(characterClass)).ToList();
                return $"{k} {nodes.Count}n/{nodes.Sum(n => n.MasterDefinition!.MaximumLevel)}p/tope {MobaMasterTree.CapFor(this._gameConfiguration.Skills, characterClass, k)}";
            })));
        }

        sb.AppendLine();
        sb.AppendLine("==================== Nodos por tipo (nombre -> atributo) ====================");
        foreach (var group in this._gameConfiguration.Skills.Where(s => s.MasterDefinition is not null).GroupBy(MobaMasterTree.KindOf).OrderBy(g => g.Key))
        {
            sb.AppendLine($"-- {group.Key} ({(MobaMasterTree.AllowedKinds.Contains(group.Key) ? "habilitado" : "no habilitado")})");
            foreach (var name in group.Select(s => $"{s.Name} -> {s.MasterDefinition!.TargetAttribute?.Designation ?? (s.MasterDefinition.ReplacedSkill is { } r ? "reemplaza " + r.Number : "-")}").Distinct().OrderBy(n => n))
            {
                sb.AppendLine($"     {name}");
            }
        }

        var path = Path.Combine(Path.GetTempPath(), "moba-master-tree.txt");
        File.WriteAllText(path, sb.ToString());
        TestContext.Out.WriteLine(sb.ToString());
        Assert.That(File.Exists(path));
    }
}
