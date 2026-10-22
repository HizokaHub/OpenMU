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
    /// The format argument of <c>/sala crear</c> names the formats; anything else is the classic one.
    /// </summary>
    [TestCase("5v5", MobaFormat.Classic)]
    [TestCase("3v3", MobaFormat.Trio)]
    [TestCase("1v1", MobaFormat.Duel)]
    [TestCase("ARAM", MobaFormat.Aram)]
    [TestCase(" aram ", MobaFormat.Aram)]
    [TestCase("", MobaFormat.Classic)]
    [TestCase(null, MobaFormat.Classic)]
    [TestCase("x", MobaFormat.Classic)]
    public void FormatArgumentIsParsed(string? text, MobaFormat expected)
    {
        Assert.That(MobaRoomChatCommandPlugIn.FormatOf(text), Is.EqualTo(expected));
    }
}
