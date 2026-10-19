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
    /// The wait is 6 s at the start and grows by half a second per minute of match.
    /// </summary>
    [TestCase(0, 6)]
    [TestCase(10, 11)]
    [TestCase(30, 21)]
    [TestCase(50, 31)]
    public void DelayGrowsLinearlyWithMatchTime(double minutes, double expectedSeconds)
    {
        Assert.That(MobaRespawn.DelayAt(TimeSpan.FromMinutes(minutes)).TotalSeconds, Is.EqualTo(expectedSeconds).Within(0.001));
    }

    /// <summary>
    /// The wait never passes 40 s, however long the match lasts.
    /// </summary>
    [Test]
    public void DelayIsCapped()
    {
        Assert.That(MobaRespawn.DelayAt(TimeSpan.FromMinutes(500)).TotalSeconds, Is.EqualTo(40));
    }
}
