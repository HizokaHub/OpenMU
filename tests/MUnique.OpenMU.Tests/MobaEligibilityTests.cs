// <copyright file="MobaEligibilityTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the MOBA entry requirements and the <c>/moba force</c> argument.
/// </summary>
[TestFixture]
public class MobaEligibilityTests
{
    /// <summary>
    /// Level 400 with the Master class is allowed.
    /// </summary>
    [Test]
    public void LevelFourHundredMasterIsEligible()
    {
        Assert.That(MobaEligibility.Check(400, true), Is.Null);
    }

    /// <summary>
    /// A lower level is rejected with the level in the reason.
    /// </summary>
    [Test]
    public void LowLevelIsRejected()
    {
        Assert.That(MobaEligibility.Check(399, true), Does.Contain("399"));
    }

    /// <summary>
    /// A level-400 character without the Master class is rejected.
    /// </summary>
    [Test]
    public void NonMasterIsRejected()
    {
        Assert.That(MobaEligibility.Check(400, false), Does.Contain("Master"));
    }

    /// <summary>
    /// Only the word "force" (any case) skips the check.
    /// </summary>
    [TestCase("force", true)]
    [TestCase(" FORCE ", true)]
    [TestCase(null, false)]
    [TestCase("blue", false)]
    public void ForceArgumentIsParsed(string? mode, bool expected)
    {
        Assert.That(new MobaEnterChatCommandArgs { Mode = mode }.Force, Is.EqualTo(expected));
    }
}
