// <copyright file="MobaRespawnTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the respawn wait of MOBA champions.
/// </summary>
[TestFixture]
public class MobaRespawnTests
{
    /// <summary>
    /// After the first death the wait grows 1.2 s per champion level; the match clock adds nothing before minute 15.
    /// </summary>
    [TestCase(1, 6)]
    [TestCase(6, 12)]
    [TestCase(15, 22.8)]
    [TestCase(30, 40.8)]
    public void DelayGrowsWithChampionLevel(int level, double expectedSeconds)
    {
        Assert.That(MobaRespawn.DelayFor(level, TimeSpan.FromMinutes(10), 1).TotalSeconds, Is.EqualTo(expectedSeconds).Within(0.001));
    }

    /// <summary>
    /// The first death of the match only pays half the wait, never below 6 s.
    /// </summary>
    [TestCase(1, 6)]
    [TestCase(6, 6)]
    [TestCase(15, 11.4)]
    [TestCase(30, 20.4)]
    public void FirstDeathPaysHalf(int level, double expectedSeconds)
    {
        Assert.That(MobaRespawn.DelayFor(level, TimeSpan.FromMinutes(10), 0).TotalSeconds, Is.EqualTo(expectedSeconds).Within(0.001));
    }

    /// <summary>
    /// After minute 15 the wait grows 1 % per minute, up to +25 %.
    /// </summary>
    [TestCase(15, 40.8)]
    [TestCase(25, 44.88)]
    [TestCase(40, 51)]
    [TestCase(120, 51)]
    public void LateGameAddsTimeFactor(double minutes, double expectedSeconds)
    {
        Assert.That(MobaRespawn.DelayFor(30, TimeSpan.FromMinutes(minutes), 3).TotalSeconds, Is.EqualTo(expectedSeconds).Within(0.001));
    }

    /// <summary>
    /// A fresh match (clock at zero) starts at the minimum for a level 1 champion.
    /// </summary>
    [Test]
    public void MatchStartIsTheMinimum()
    {
        Assert.That(MobaRespawn.DelayFor(1, TimeSpan.Zero, 5).TotalSeconds, Is.EqualTo(6));
    }
}
