// <copyright file="MobaSkillRankTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the LoL-style points of the champion skills: one per level up to level 20 for the attacks, the
/// buffs have their own points at the milestone levels.
/// </summary>
[TestFixture]
public class MobaSkillRankTests
{
    /// <summary>
    /// The attack skills earn a point per level up to level 20 and none after it.
    /// </summary>
    [Test]
    public void AttackPointsStopAtLevelTwenty()
    {
        var total = Enumerable.Range(2, MobaLevels.MaxLevel - 1).Sum(level => MobaSkills.PointsForReaching((byte)2, level).Skill);

        // The champion starts with the point of level 1: 20 in all, exactly 4 skills x 5 ranks.
        Assert.That(total + 1, Is.EqualTo(4 * MobaSkills.SkillLevelCap));
    }

    /// <summary>
    /// The buff points come only at the milestone levels, and by how many buffs the class has.
    /// </summary>
    /// <param name="classNumber">The class number.</param>
    /// <param name="expected">The points per milestone.</param>
    [TestCase((byte)2, 2)]   // Soul Master: 2 buffs
    [TestCase((byte)6, 2)]   // Blade Knight: 2 buffs
    [TestCase((byte)12, 2)]  // Magic Gladiator: 1 buff
    [TestCase((byte)16, 2)]  // Dark Lord: 2 buffs
    [TestCase((byte)20, 3)]  // Summoner: 3 buffs
    [TestCase((byte)24, 3)]  // Rage Fighter: 3 buffs
    [TestCase((byte)10, 4)]  // Muse Elf: 5 buffs
    public void BuffPointsFollowTheNumberOfBuffs(byte classNumber, int expected)
    {
        foreach (var level in MobaSkills.BuffPointLevels)
        {
            Assert.That(MobaSkills.PointsForReaching(classNumber, level).Buff, Is.EqualTo(expected), $"class {classNumber} level {level}");
        }

        Assert.That(MobaSkills.PointsForReaching(classNumber, 7).Buff, Is.EqualTo(0));
    }

    /// <summary>
    /// The points of all the milestones nearly max every buff (the leftovers are lost).
    /// </summary>
    [Test]
    public void BuffPointsReachTheCapOfEveryBuff()
    {
        foreach (var classNumber in new byte[] { 2, 6, 10, 12, 16, 20, 24 })
        {
            var total = MobaSkills.BuffPointLevels.Sum(level => MobaSkills.PointsForReaching(classNumber, level).Buff);
            var needed = MobaLoadouts.BuffsFor(classNumber).Count * MobaSkills.SkillLevelCap;
            Assert.That(total, Is.GreaterThanOrEqualTo(needed - MobaSkills.SkillLevelCap), $"class {classNumber}");
        }
    }
}
