// <copyright file="MobaClientBuildTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The minimum client build: a client older than the minimum (or one that never reported its build) cannot play a match; a minimum of 0 accepts everyone.
/// </summary>
[TestFixture]
public class MobaClientBuildTests
{
    /// <summary>
    /// Who may play.
    /// </summary>
    /// <param name="reported">The build the client reported.</param>
    /// <param name="minimum">The minimum build.</param>
    /// <param name="mayPlay">The expected result.</param>
    [TestCase(0L, 0L, true)]
    [TestCase(2610270001L, 0L, true)]
    [TestCase(2610270001L, 2610270001L, true)]
    [TestCase(2610270002L, 2610270001L, true)]
    [TestCase(2610260900L, 2610270001L, false)]
    [TestCase(0L, 2610270001L, false)]
    public void OldClientsAreRefused(long reported, long minimum, bool mayPlay)
    {
        var reason = MobaClientBuild.Check(reported, minimum);
        Assert.That(reason is null, Is.EqualTo(mayPlay), reason);
    }

    /// <summary>
    /// The reason names both builds, and says how to update, when the client has one.
    /// </summary>
    [Test]
    public void TheReasonSaysHowToUpdate()
    {
        var reason = MobaClientBuild.Check(2610260900L, 2610270001L);
        Assert.That(reason, Does.Contain("2610260900").And.Contain("2610270001").And.Contain("acceso directo"));
    }

    /// <summary>
    /// The file written by the publisher is read; a broken or empty file means "any client".
    /// </summary>
    [Test]
    public void TheFileIsParsed()
    {
        Assert.That(MobaClientBuild.Parse("{\"minimumBuild\":2610270001,\"latestBuild\":2610270005}"), Is.EqualTo((2610270001L, 2610270005L)));
        Assert.That(MobaClientBuild.Parse("{\"latestBuild\":5}"), Is.EqualTo((0L, 5L)));
        Assert.That(MobaClientBuild.Parse("no es json"), Is.EqualTo((0L, 0L)));
        Assert.That(MobaClientBuild.Parse("{\"minimumBuild\":-3}"), Is.EqualTo((0L, 0L)));
    }
}
