// <copyright file="MobaLoadoutTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the loadout a player picks for its MOBA champion.
/// </summary>
[TestFixture]
public class MobaLoadoutTests
{
    // One class number of every family: Soul Master, Blade Knight, Muse Elf, Magic Gladiator, Dark Lord, Summoner, Rage Fighter.
    private static readonly byte[] ClassNumbers = { 2, 6, 10, 12, 16, 20, 24 };

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
    /// The default loadout of every class is a valid pick.
    /// </summary>
    [Test]
    public void DefaultLoadoutsAreValid()
    {
        foreach (var number in ClassNumbers)
        {
            var defaults = MobaLoadouts.DefaultFor(number);
            Assert.That(MobaLoadoutStore.Validate(defaults.ToArray(), number), Is.Null, $"class {number}");
        }
    }

    /// <summary>
    /// The skills a player can pick never include buffs or summons, and every class has enough of them.
    /// </summary>
    [Test]
    public void SelectableSkillsHaveNoBuffs()
    {
        short[] buffs = { 16, 233, 18, 48, 26, 27, 28, 77, 234, 30, 36, 63, 64, 217, 218, 266, 267, 268 };
        foreach (var number in ClassNumbers)
        {
            var selectable = MobaLoadouts.SelectableFor(number);
            Assert.That(selectable.Count, Is.GreaterThanOrEqualTo(MobaLoadoutStore.MaxSkills + 2), $"class {number}");
            Assert.That(selectable.Intersect(buffs), Is.Empty, $"class {number}");
        }
    }

    /// <summary>
    /// Too few, too many, repeated or foreign skills are refused.
    /// </summary>
    [Test]
    public void InvalidLoadoutsAreRefused()
    {
        var wizard = MobaLoadouts.SelectableFor(2);
        Assert.That(MobaLoadoutStore.Validate(wizard.Take(3).ToArray(), 2), Is.Not.Null);
        Assert.That(MobaLoadoutStore.Validate(wizard.Take(5).ToArray(), 2), Is.Not.Null);
        Assert.That(MobaLoadoutStore.Validate(new short[] { wizard[0], wizard[0], wizard[1], wizard[2] }, 2), Is.Not.Null);
        Assert.That(MobaLoadoutStore.Validate(new short[] { wizard[0], wizard[1], wizard[2], 260 }, 2), Is.Not.Null);
        Assert.That(MobaLoadoutStore.Validate(wizard.Take(4).ToArray(), 2), Is.Null);
    }

    /// <summary>
    /// What a player saved comes back (also after reading the file again); without a pick the default is used.
    /// </summary>
    [Test]
    public void SavedLoadoutSurvivesAReload()
    {
        Assert.That(MobaLoadoutStore.SelectedFor("Test", 2), Is.EqualTo(MobaLoadouts.DefaultFor(2)));

        var picks = MobaLoadouts.SelectableFor(2).Skip(2).Take(4).ToArray();
        Assert.That(MobaLoadoutStore.TrySave("Test", 2, picks, out var error), Is.True, error);
        Assert.That(MobaLoadoutStore.SelectedFor("Test", 2), Is.EqualTo(picks));

        MobaLoadoutStore.Reset();
        Assert.That(MobaLoadoutStore.SelectedFor("Test", 2), Is.EqualTo(picks));
        Assert.That(MobaLoadoutStore.SelectedFor("Other", 2), Is.EqualTo(MobaLoadouts.DefaultFor(2)));
    }
}
