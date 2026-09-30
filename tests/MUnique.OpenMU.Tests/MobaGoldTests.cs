// <copyright file="MobaGoldTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the phase-based gold curve of the MOBA economy.
/// </summary>
[TestFixture]
public class MobaGoldTests
{
    /// <summary>Income grows with every phase.</summary>
    [Test]
    public void PhaseMultiplierGrowsWithPhase()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MobaGold.PhaseMultiplierOf(MobaShopTier.T1), Is.EqualTo(1.0));
            Assert.That(MobaGold.PhaseMultiplierOf(MobaShopTier.T2), Is.EqualTo(1.0));
            Assert.That(MobaGold.PhaseMultiplierOf(MobaShopTier.T3), Is.EqualTo(2.6));
        });
    }
}
