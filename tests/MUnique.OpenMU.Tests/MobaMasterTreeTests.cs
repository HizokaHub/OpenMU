// <copyright file="MobaMasterTreeTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
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
            Assert.That(offense, Is.GreaterThanOrEqualTo(60), $"class {classNumber} offense capacity");
            Assert.That(MobaMasterTree.OffenseCapFor(this._gameConfiguration.Skills, characterClass), Is.InRange(48, MobaMasterTree.OffensePointsForMax), $"class {classNumber} cap");
            Assert.That(nodes.Any(s => MobaMasterTree.IsAllowed(s) && !MobaMasterTree.IsOffense(s)), Is.True, $"class {classNumber} has health/mana nodes");
        }
    }

    /// <summary>Every allowed strengthener node ends in a damaging skill of the MOBA tables (the others stay off).</summary>
    [Test]
    public void AllowedStrengthenersBoostATabledSkill()
    {
        var allowed = 0;
        foreach (var skill in this._gameConfiguration.Skills.Where(s => s.MasterDefinition?.ReplacedSkill is not null && MobaMasterTree.IsAllowed(s)))
        {
            allowed++;
            Assert.That(MobaSkillDamage.HasDamageEntry((short)skill.GetBaseSkill().Number), Is.True, $"{skill.Name} ({skill.Number})");
        }

        Assert.That(allowed, Is.GreaterThan(20));
    }

    /// <summary>Every class has defense and critical nodes, and the tree terms of both can be completed with few points.</summary>
    [Test]
    public void EveryClassHasDefenseAndCritNodes()
    {
        foreach (var classNumber in MasterClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            var defense = MobaMasterTree.CapFor(this._gameConfiguration.Skills, characterClass, MobaTreeKind.Defense);
            var crit = MobaMasterTree.CapFor(this._gameConfiguration.Skills, characterClass, MobaTreeKind.Crit);
            Assert.That(defense, Is.InRange(60, MobaMasterTree.OffensePointsForMax), $"class {classNumber} defense cap");
            Assert.That(crit, Is.InRange(1, MobaMasterTree.OffensePointsForMax), $"class {classNumber} crit cap");
        }
    }

    /// <summary>The classifier puts the troublesome designations in the right kind.</summary>
    [TestCase("Maximum Health", false, MobaTreeKind.Health)]
    [TestCase("Maximum Mana", false, MobaTreeKind.Mana)]
    [TestCase("Item Duration Increase", false, MobaTreeKind.Durability)]
    [TestCase("Base Defense", false, MobaTreeKind.Defense)]
    [TestCase("Defense Rate (PvM)", false, MobaTreeKind.Defense)]
    [TestCase("Poison Resistance", false, MobaTreeKind.Defense)]
    [TestCase("Maximum Shield", false, MobaTreeKind.Shield)]
    [TestCase("Health recover after Monster kill, multiplier of max health", false, MobaTreeKind.KillRecovery)]
    [TestCase("Ability Recovery Multiplier", false, MobaTreeKind.Other)]
    [TestCase("Pet Duration Increase", false, MobaTreeKind.Useless)]
    [TestCase("Critical Damage Chance", false, MobaTreeKind.Crit)]
    [TestCase("Critical Damage Bonus", false, MobaTreeKind.Crit)]
    [TestCase("Raven exc damage chance", false, MobaTreeKind.Crit)]
    [TestCase("Spear Mastery Double Damage Chance (MST)", false, MobaTreeKind.Crit)]
    [TestCase("Attack Rate (PvP)", false, MobaTreeKind.Crit)]
    [TestCase("Minimum Physical Base Damage", false, MobaTreeKind.Offense)]
    [TestCase("Two Handed Staff Mastery PvP Bonus Damage (MST)", false, MobaTreeKind.Offense)]
    [TestCase("Soul Barrier Damage Receive Decrement", false, MobaTreeKind.Other)]
    [TestCase("Weakness Physical Damage Decrement", false, MobaTreeKind.Other)]
    [TestCase("Health Recovery Multiplier", false, MobaTreeKind.Recovery)]
    [TestCase("Mana Usage Reduction", false, MobaTreeKind.Utility)]
    [TestCase("Weapon Mastery Bonus Attack Speed (MST)", false, MobaTreeKind.Utility)]
    [TestCase("Flame Strengthener", true, MobaTreeKind.Strengthener)]
    [TestCase(null, false, MobaTreeKind.Strengthener)]
    public void ClassifierPutsNodesInTheRightKind(string? designation, bool replaces, MobaTreeKind expected)
        => Assert.That(MobaMasterTree.Classify(replaces, designation), Is.EqualTo(expected));

    /// <summary>Every class has Aegis (shield), durability, recovery and utility nodes to spend points on.</summary>
    [Test]
    public void EveryClassHasTheNewKinds()
    {
        foreach (var classNumber in MasterClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            foreach (var kind in new[] { MobaTreeKind.Shield, MobaTreeKind.Durability, MobaTreeKind.Recovery, MobaTreeKind.Utility })
            {
                Assert.That(MobaMasterTree.CapFor(this._gameConfiguration.Skills, characterClass, kind), Is.GreaterThan(0), $"class {classNumber} {kind}");
            }
        }
    }

    /// <summary>The blend is 45/20/35 and renormalizes when the class has no nodes of the kind.</summary>
    [Test]
    public void BlendUsesTheUserWeights()
    {
        Assert.That(MobaMasterTree.Blend(1.0, 1.0, 1.0), Is.EqualTo(1.0).Within(1e-9));
        Assert.That(MobaMasterTree.Blend(0.0, 1.0, 1.0), Is.EqualTo(0.80).Within(1e-9));
        Assert.That(MobaMasterTree.Blend(1.0, 0.0, 0.0), Is.EqualTo(0.20).Within(1e-9));
        Assert.That(MobaMasterTree.Blend((double?)null, 1.0, 1.0), Is.EqualTo(1.0).Within(1e-9));
        Assert.That(MobaMasterTree.Blend((double?)null, 1.0, 0.0), Is.EqualTo(0.45 / 0.80).Within(1e-9));
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
    /// <summary>The MOBA kill recovery runs linearly from 2 % (1 point) to 10 % (20 points) of the maximum.</summary>
    [Test]
    public void KillRecoveryRunsFromTwoToTenPercent()
    {
        Assert.That(MobaMasterTree.KillRecoveryFraction(0), Is.EqualTo(0));
        Assert.That(MobaMasterTree.KillRecoveryFraction(1), Is.EqualTo(0.02).Within(1e-9));
        Assert.That(MobaMasterTree.KillRecoveryFraction(20), Is.EqualTo(0.10).Within(1e-9));
        Assert.That(MobaMasterTree.KillRecoveryFraction(50), Is.EqualTo(0.10).Within(1e-9));
        Assert.That(MobaMasterTree.KillRecoveryFraction(10), Is.EqualTo(0.02 + (0.08 * 9 / 19.0)).Within(1e-9));
    }

    /// <summary>Every master class has kill-recovery nodes for HP and mana, all of them recognized by the clone filter and allowed.</summary>
    [Test]
    public void EveryClassHasKillRecoveryNodes()
    {
        foreach (var classNumber in MasterClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            var nodes = this._gameConfiguration.Skills
                .Where(s => s.MasterDefinition is not null && s.QualifiedCharacters.Contains(characterClass) && MobaMasterTree.KindOf(s) == MobaTreeKind.KillRecovery)
                .ToList();
            Assert.That(nodes, Is.Not.Empty, characterClass.Name);
            Assert.That(nodes.All(n => MobaMasterTree.IsKillRecoveryTarget(n.MasterDefinition!.TargetAttribute) && MobaMasterTree.IsAllowed(n)), Is.True, characterClass.Name);
        }
    }
}
