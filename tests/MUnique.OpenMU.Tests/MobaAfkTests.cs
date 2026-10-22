// <copyright file="MobaAfkTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the anti-AFK escalation of MOBA champions.
/// </summary>
[TestFixture]
public class MobaAfkTests
{
    /// <summary>
    /// The team is told after 15 s and the champion is sent to its base after 20 s.
    /// </summary>
    [TestCase(0, MobaAfkStage.Active)]
    [TestCase(14.9, MobaAfkStage.Active)]
    [TestCase(15, MobaAfkStage.Idle)]
    [TestCase(19.9, MobaAfkStage.Idle)]
    [TestCase(20, MobaAfkStage.Recalling)]
    [TestCase(600, MobaAfkStage.Recalling)]
    public void StageFollowsTheIdleTime(double idleSeconds, MobaAfkStage expected)
    {
        Assert.That(MobaAfk.StageFor(idleSeconds), Is.EqualTo(expected));
    }
}
