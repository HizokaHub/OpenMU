// <copyright file="MobaSummonTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the Elf's creature: the appearance picked along with the 4 skills, the 5th skill's points and the strength by rank.
/// </summary>
[TestFixture]
public class MobaSummonTests
{
    private const byte Elf = 10;
    private const byte SoulMaster = 2;

    private string _file = string.Empty;

    /// <summary>
    /// Points the store at a temporary file.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this._file = Path.Combine(Path.GetTempPath(), $"moba-loadouts-{Guid.NewGuid():N}.json");
        MobaLoadoutStore.FilePath = this._file;
        MobaLoadoutStore.Reset();
    }

    /// <summary>
    /// Removes the temporary file.
    /// </summary>
    [TearDown]
    public void TearDown()
    {
        if (File.Exists(this._file))
        {
            File.Delete(this._file);
        }
    }

    /// <summary>
    /// Only the Elf has creatures to summon.
    /// </summary>
    [Test]
    public void OnlyTheElfHasSummons()
    {
        Assert.That(MobaLoadouts.SummonsFor(Elf), Has.Count.EqualTo(7));
        foreach (var classNumber in new byte[] { 2, 6, 12, 16, 20, 24 })
        {
            Assert.That(MobaLoadouts.SummonsFor(classNumber), Is.Empty, $"class {classNumber}");
        }
    }

    /// <summary>
    /// The Elf's loadout always carries an appearance: the default one, or the one she picked.
    /// </summary>
    [Test]
    public void TheElfLoadoutCarriesAnAppearance()
    {
        var picks = MobaLoadouts.DefaultFor(Elf);
        Assert.That(MobaLoadoutStore.SelectedFor("Elfa", Elf).Count(MobaLoadouts.IsCreatureSummon), Is.EqualTo(1));

        Assert.That(MobaLoadoutStore.TrySave("Elfa", Elf, picks.Concat(new short[] { 34 }).ToList(), out var error), Is.True, error);
        Assert.That(MobaLoadoutStore.SelectedFor("Elfa", Elf), Does.Contain((short)34));
        Assert.That(MobaLoadoutStore.SelectedFor("Elfa", Elf).Count(MobaLoadouts.IsCreatureSummon), Is.EqualTo(1));
    }

    /// <summary>
    /// A class without a summon can't pick one, and nobody can pick two.
    /// </summary>
    [Test]
    public void TheAppearanceIsValidated()
    {
        var wizard = MobaLoadouts.DefaultFor(SoulMaster).Concat(new short[] { 30 }).ToList();
        Assert.That(MobaLoadoutStore.Validate(wizard, SoulMaster), Is.Not.Null);

        var twoSummons = MobaLoadouts.DefaultFor(Elf).Concat(new short[] { 30, 31 }).ToList();
        Assert.That(MobaLoadoutStore.Validate(twoSummons, Elf), Is.Not.Null);
        Assert.That(MobaLoadoutStore.Validate(MobaLoadouts.DefaultFor(Elf), Elf), Is.Null);
    }

    /// <summary>
    /// The Elf earns a point per level up to level 25 (five skills of five ranks); the others stop at 20.
    /// </summary>
    [Test]
    public void TheElfHasFiveSkillsToRank()
    {
        int Total(byte classNumber) => 1 + Enumerable.Range(2, MobaLevels.MaxLevel - 1).Sum(level => MobaSkills.PointsForReaching(classNumber, level).Skill);

        Assert.That(Total(Elf), Is.EqualTo(5 * MobaSkills.SkillLevelCap));
        Assert.That(Total(SoulMaster), Is.EqualTo(4 * MobaSkills.SkillLevelCap));
    }

    /// <summary>
    /// The creature gets stronger with the rank and with the champion level.
    /// </summary>
    [Test]
    public void TheCreatureGrowsWithRankAndLevel()
    {
        for (var rank = 1; rank < MobaSkills.SkillLevelCap; rank++)
        {
            Assert.That(MobaSummons.HealthAt(10, rank + 1), Is.GreaterThan(MobaSummons.HealthAt(10, rank)));
            Assert.That(MobaSummons.DamageMultiplierAt(10, rank + 1), Is.GreaterThan(MobaSummons.DamageMultiplierAt(10, rank)));
        }

        Assert.That(MobaSummons.HealthAt(20, 3), Is.GreaterThan(MobaSummons.HealthAt(5, 3)));
        Assert.That(MobaSummons.DamageMultiplierAt(20, 3), Is.GreaterThan(MobaSummons.DamageMultiplierAt(5, 3)));
    }
}
