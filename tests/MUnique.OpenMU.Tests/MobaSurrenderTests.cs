// <copyright file="MobaSurrenderTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The surrender vote needs three quarters of the humans of the team, rounded up, and never less than one.
/// </summary>
[TestFixture]
public class MobaSurrenderTests
{
    /// <summary>
    /// The votes needed by the usual team sizes.
    /// </summary>
    /// <param name="humans">The humans of the team.</param>
    /// <param name="expected">The votes needed.</param>
    [TestCase(0, 1)]
    [TestCase(1, 1)]
    [TestCase(2, 2)]
    [TestCase(3, 3)]
    [TestCase(4, 3)]
    [TestCase(5, 4)]
    public void RequiredVotesIsThreeQuartersRoundedUp(int humans, int expected)
    {
        Assert.That(MobaSurrender.RequiredVotes(humans), Is.EqualTo(expected));
    }

    /// <summary>
    /// The announcer events are part of the packet the client decodes: their numbers must not move.
    /// </summary>
    [Test]
    public void AnnouncementNumbersAreStable()
    {
        Assert.That((byte)MobaAnnouncement.Kill, Is.EqualTo(1));
        Assert.That((byte)MobaAnnouncement.FirstBlood, Is.EqualTo(2));
        Assert.That((byte)MobaAnnouncement.PentaKill, Is.EqualTo(6));
        Assert.That((byte)MobaAnnouncement.Legendary, Is.EqualTo(12));
        Assert.That((byte)MobaAnnouncement.Victory, Is.EqualTo(16));
        Assert.That((byte)MobaAnnouncement.GreatDragonSlain, Is.EqualTo(20));
    }
}
