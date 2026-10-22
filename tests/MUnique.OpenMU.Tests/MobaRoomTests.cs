// <copyright file="MobaRoomTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the private rooms of the MOBA mode.
/// </summary>
[TestFixture]
public class MobaRoomTests
{
    /// <summary>
    /// The shape argument of <c>/sala crear</c> names the players per team and the map; anything else is 5v5 on three lanes.
    /// </summary>
    [TestCase("5v5", 5, false)]
    [TestCase("3v3", 3, false)]
    [TestCase("2v2", 2, false)]
    [TestCase("1v1", 1, false)]
    [TestCase("3v3aram", 3, true)]
    [TestCase("aram", 5, true)]
    [TestCase(" ARAM ", 5, true)]
    [TestCase("", 5, false)]
    [TestCase(null, 5, false)]
    [TestCase("9v9", 5, false)]
    public void ShapeArgumentIsParsed(string? text, int expectedSize, bool expectedMidOnly)
    {
        var (size, midOnly) = MobaRoomChatCommandPlugIn.ShapeOf(text);
        Assert.That(size, Is.EqualTo(expectedSize));
        Assert.That(midOnly, Is.EqualTo(expectedMidOnly));
    }

    /// <summary>
    /// A room names its match by the players per team and the map, and plays the arena layout of that map.
    /// </summary>
    [Test]
    public void RoomDescribesItsMatch()
    {
        var room = new MobaRoom { Id = 101, Name = "Prueba", TeamSize = 3, MidOnly = true };
        Assert.That(room.Label, Is.EqualTo("3v3 carril central"));
        Assert.That(room.Format, Is.EqualTo(MobaFormat.Aram));

        var full = new MobaRoom { Id = 102, Name = "Otra", TeamSize = 2, MidOnly = false };
        Assert.That(full.Label, Is.EqualTo("2v2 3 carriles"));
        Assert.That(full.Format, Is.EqualTo(MobaFormat.Classic));
    }
}
