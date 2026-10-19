// <copyright file="MobaArenaTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests that the state of a MOBA arena is its own: two arenas never share a clock, phase or captures.
/// </summary>
[TestFixture]
public class MobaArenaTests
{
    /// <summary>
    /// The clock of one arena does not move the other's, and a reset only touches its own.
    /// </summary>
    [Test]
    public void ClocksAreIndependent()
    {
        var first = MobaArena.Standalone();
        var second = MobaArena.Standalone();
        first.MatchStartUtc = DateTime.UtcNow.AddMinutes(-12);

        Assert.That(first.Elapsed.TotalMinutes, Is.EqualTo(12).Within(0.1));
        Assert.That(second.Elapsed, Is.EqualTo(TimeSpan.Zero));

        second.MatchStartUtc = DateTime.UtcNow.AddMinutes(-3);
        first.ResetClock();
        Assert.That(first.Elapsed, Is.EqualTo(TimeSpan.Zero));
        Assert.That(second.Elapsed.TotalMinutes, Is.EqualTo(3).Within(0.1));
    }

    /// <summary>
    /// Phase and leader level are per arena, and a phase reset puts the arena back to T1.
    /// </summary>
    [Test]
    public void PhaseIsPerArena()
    {
        var first = MobaArena.Standalone();
        var second = MobaArena.Standalone();
        first.Phase = MobaShopTier.T3;
        first.LeaderLevel = 25;

        Assert.That(second.Phase, Is.EqualTo(MobaShopTier.T1));
        Assert.That(second.LeaderLevel, Is.EqualTo(1));

        MobaMatchPhase.Reset(first);
        Assert.That(first.Phase, Is.EqualTo(MobaShopTier.T1));
        Assert.That(first.LeaderLevel, Is.EqualTo(1));
    }

    /// <summary>
    /// Fountain captures and the objective board are per arena.
    /// </summary>
    [Test]
    public void CapturesAndBoardsAreSeparate()
    {
        var first = MobaArena.Standalone();
        var second = MobaArena.Standalone();
        MobaFountains.StateOf(first, 0).Advance(MobaFountainState.CaptureSeconds, true, false);

        Assert.That(MobaFountains.StateOf(first, 0).Owner, Is.EqualTo(MobaTeam.Blue));
        Assert.That(MobaFountains.StateOf(second, 0).Owner, Is.EqualTo(MobaTeam.None));
        Assert.That(first.ObjectiveBoard, Is.Not.SameAs(second.ObjectiveBoard));

        MobaFountains.Reset(first);
        Assert.That(MobaFountains.StateOf(first, 0).Owner, Is.EqualTo(MobaTeam.None));
    }

    /// <summary>
    /// Objects which are on no map share one stand-in, never a real arena.
    /// </summary>
    [Test]
    public void DetachedObjectsGetTheStandIn()
    {
        Assert.That(MobaArena.Of((MUnique.OpenMU.GameLogic.GameMap?)null), Is.SameAs(MobaArena.Of((MUnique.OpenMU.GameLogic.GameMap?)null)));
        Assert.That(MobaArena.Standalone(), Is.Not.SameAs(MobaArena.Of((MUnique.OpenMU.GameLogic.GameMap?)null)));
    }
}
