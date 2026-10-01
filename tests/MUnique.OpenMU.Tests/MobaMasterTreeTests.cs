// <copyright file="MobaMasterTreeTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Checks that the curated MOBA master tree is usable for every class: enough offense nodes to reach the
/// whole tree term of the damage blend, and none of the allowed nodes replaces a skill.
/// </summary>
[TestFixture]
public class MobaMasterTreeTests
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

    /// <summary>Every class can put at least <see cref="MobaMasterTree.OffensePointsForMax"/> points in offense nodes.</summary>
    [Test]
    public void EveryClassHasEnoughOffenseNodes()
    {
        foreach (var classNumber in MasterClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            var nodes = this._gameConfiguration.Skills
                .Where(s => s.MasterDefinition is not null && s.QualifiedCharacters.Contains(characterClass))
                .ToList();
            var offense = nodes.Where(MobaMasterTree.IsOffense).Sum(s => s.MasterDefinition!.MaximumLevel);
            Assert.That(offense, Is.GreaterThanOrEqualTo(80), $"class {classNumber} offense capacity");
            Assert.That(MobaMasterTree.OffenseCapFor(this._gameConfiguration.Skills, characterClass), Is.InRange(64, MobaMasterTree.OffensePointsForMax), $"class {classNumber} cap");
            Assert.That(nodes.Any(s => MobaMasterTree.IsAllowed(s) && !MobaMasterTree.IsOffense(s)), Is.True, $"class {classNumber} has health/mana nodes");
        }
    }

    /// <summary>No allowed node replaces a skill (the MOBA tables know skills by number).</summary>
    [Test]
    public void AllowedNodesNeverReplaceASkill()
    {
        foreach (var skill in this._gameConfiguration.Skills.Where(s => s.MasterDefinition is not null))
        {
            if (skill.MasterDefinition!.ReplacedSkill is not null)
            {
                Assert.That(MobaMasterTree.IsAllowed(skill), Is.False, skill.Number.ToString());
            }
        }
    }

    /// <summary>The first two ranks are free, the rest need 10 points per rank above 2.</summary>
    [Test]
    public void RankRuleScalesByTen()
    {
        var byRank = this._gameConfiguration.Skills
            .Where(s => s.MasterDefinition is not null)
            .GroupBy(s => s.MasterDefinition!.Rank)
            .ToDictionary(g => g.Key, g => g.First());
        Assert.That(MobaMasterTree.PointsNeededInRoot(byRank[1]), Is.EqualTo(0));
        Assert.That(MobaMasterTree.PointsNeededInRoot(byRank[2]), Is.EqualTo(0));
        Assert.That(MobaMasterTree.PointsNeededInRoot(byRank[3]), Is.EqualTo(10));
        Assert.That(MobaMasterTree.PointsNeededInRoot(byRank[5]), Is.EqualTo(30));
    }
}
