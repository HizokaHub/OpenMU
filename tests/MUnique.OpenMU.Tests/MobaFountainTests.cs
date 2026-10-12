// <copyright file="MobaFountainTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the MOBA fountains: the capture accumulation and their placement.
/// </summary>
[TestFixture]
public class MobaFountainTests
{
    /// <summary>
    /// A team needs the full 15 seconds of channelling to take a neutral fountain.
    /// </summary>
    [Test]
    public void CaptureNeedsFifteenSeconds()
    {
        var state = new MobaFountainState();
        for (var i = 0; i < 14; i++)
        {
            Assert.That(state.Advance(1, true, false), Is.EqualTo(MobaTeam.None), $"second {i + 1}");
        }

        Assert.That(state.Owner, Is.EqualTo(MobaTeam.None));
        Assert.That(state.Advance(1, true, false), Is.EqualTo(MobaTeam.Blue));
        Assert.That(state.Owner, Is.EqualTo(MobaTeam.Blue));
    }

    /// <summary>
    /// Releasing the key keeps the accumulated seconds, so a fight in the middle does not lose the progress.
    /// </summary>
    [Test]
    public void ProgressIsKeptWhenTheKeyIsReleased()
    {
        var state = new MobaFountainState();
        state.Advance(2, true, false);
        state.Advance(5, false, false);
        state.Advance(2, true, false);
        Assert.That(state.BlueProgress, Is.EqualTo(4).Within(0.001));
        Assert.That(state.Advance(11, true, false), Is.EqualTo(MobaTeam.Blue));
    }

    /// <summary>
    /// When both teams channel nobody progresses, and each team keeps its own progress.
    /// </summary>
    [Test]
    public void ContestedFountainDoesNotProgress()
    {
        var state = new MobaFountainState();
        state.Advance(6, true, false);
        state.Advance(3, false, true);
        state.Advance(4, true, true);
        Assert.That(state.Contested, Is.True);
        Assert.That(state.BlueProgress, Is.EqualTo(6).Within(0.001));
        Assert.That(state.RedProgress, Is.EqualTo(3).Within(0.001));
        state.Advance(1, true, false);
        Assert.That(state.Contested, Is.False);
        Assert.That(state.BlueProgress, Is.EqualTo(7).Within(0.001));
    }

    /// <summary>
    /// Capturing resets both teams' progress; the former owner can take it back with another full channel.
    /// </summary>
    [Test]
    public void CapturingResetsProgressAndTheOtherTeamCanTakeItBack()
    {
        var state = new MobaFountainState();
        state.Advance(4, false, true);
        state.Advance(15, true, false);
        Assert.That(state.Owner, Is.EqualTo(MobaTeam.Blue));
        Assert.That(state.RedProgress, Is.EqualTo(0));
        Assert.That(state.Advance(10, false, true), Is.EqualTo(MobaTeam.None));
        Assert.That(state.Advance(5, true, false), Is.EqualTo(MobaTeam.None), "the owner channelling does nothing");
        Assert.That(state.Advance(5, false, true), Is.EqualTo(MobaTeam.Red));
        Assert.That(state.Owner, Is.EqualTo(MobaTeam.Red));
    }

    /// <summary>
    /// The fountains sit on the horizontal road, turned 180 degrees about the arena centre of each other, on open ground.
    /// </summary>
    [Test]
    public void FountainsAreSymmetricAndOnOpenGround()
    {
        var a = MobaFountains.Positions[0];
        var b = MobaFountains.Positions[1];
        Assert.That(a.X + b.X, Is.EqualTo(236));
        Assert.That(a.Y + b.Y, Is.EqualTo(255));

        var terrain = new GameMapTerrain(new GameMapDefinition { Number = 200 });
        foreach (var (x, y) in MobaFountains.Positions)
        {
            for (var dy = -3; dy <= 3; dy++)
            {
                for (var dx = -3; dx <= 3; dx++)
                {
                    Assert.That(terrain.WalkMap[x + dx, y + dy], Is.True, $"({x + dx},{y + dy}) next to the fountain at ({x},{y})");
                }
            }
        }
    }
}
