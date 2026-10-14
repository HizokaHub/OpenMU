// <copyright file="MobaMinimapKindTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.CharacterClasses;

/// <summary>
/// Tests the packed KIND byte of the MOBA minimap entries (packet C1 D5 10).
/// </summary>
[TestFixture]
public class MobaMinimapKindTests
{
    /// <summary>
    /// Every class, including the 2nd and 3rd evolutions, maps to the base class index of its family.
    /// </summary>
    /// <param name="characterClass">The character class.</param>
    /// <param name="expectedIndex">The expected base class index.</param>
    [TestCase(CharacterClassNumber.DarkWizard, 0)]
    [TestCase(CharacterClassNumber.SoulMaster, 0)]
    [TestCase(CharacterClassNumber.GrandMaster, 0)]
    [TestCase(CharacterClassNumber.DarkKnight, 1)]
    [TestCase(CharacterClassNumber.BladeKnight, 1)]
    [TestCase(CharacterClassNumber.BladeMaster, 1)]
    [TestCase(CharacterClassNumber.FairyElf, 2)]
    [TestCase(CharacterClassNumber.MuseElf, 2)]
    [TestCase(CharacterClassNumber.HighElf, 2)]
    [TestCase(CharacterClassNumber.MagicGladiator, 3)]
    [TestCase(CharacterClassNumber.DuelMaster, 3)]
    [TestCase(CharacterClassNumber.DarkLord, 4)]
    [TestCase(CharacterClassNumber.LordEmperor, 4)]
    [TestCase(CharacterClassNumber.Summoner, 5)]
    [TestCase(CharacterClassNumber.BloodySummoner, 5)]
    [TestCase(CharacterClassNumber.DimensionMaster, 5)]
    [TestCase(CharacterClassNumber.RageFighter, 6)]
    [TestCase(CharacterClassNumber.FistMaster, 6)]
    public void ClassMapsToItsBaseIndex(CharacterClassNumber characterClass, int expectedIndex)
    {
        var classNumber = (byte)characterClass;
        Assert.That(MobaMinimapKind.BaseClassIndex(classNumber), Is.EqualTo(expectedIndex));
        Assert.That(MobaMinimapKind.BaseClassIndex(classNumber), Is.EqualTo((int)MobaPassives.FamilyOf(classNumber)), "same order as MobaFamily");

        var packed = MobaMinimapKind.Pack(MobaMinimapKind.Champion, classNumber, false);
        Assert.That(packed, Is.EqualTo(MobaMinimapKind.Champion | (expectedIndex << 4)));
        Assert.That(MobaMinimapKind.KindOf(packed), Is.EqualTo(MobaMinimapKind.Champion));
        Assert.That(MobaMinimapKind.ClassOf(packed), Is.EqualTo(expectedIndex));
        Assert.That(MobaMinimapKind.IsRecalling(packed), Is.False);
    }

    /// <summary>
    /// Unknown class numbers past the Rage Fighter are clamped to the last base index, so they never spill into the recall bit.
    /// </summary>
    [Test]
    public void UnknownClassesAreClamped()
    {
        foreach (var classNumber in new byte[] { 28, 32, 100, 255 })
        {
            Assert.That(MobaMinimapKind.BaseClassIndex(classNumber), Is.EqualTo(MobaMinimapKind.MaxBaseClassIndex), $"class {classNumber}");
            var packed = MobaMinimapKind.Pack(MobaMinimapKind.Champion, classNumber, false);
            Assert.That(MobaMinimapKind.IsRecalling(packed), Is.False, $"class {classNumber}");
            Assert.That(MobaMinimapKind.KindOf(packed), Is.EqualTo(MobaMinimapKind.Champion), $"class {classNumber}");
        }
    }

    /// <summary>
    /// The recall sets bit 7 and keeps the kind and the class intact.
    /// </summary>
    [Test]
    public void RecallSetsTheHighBit()
    {
        var packed = MobaMinimapKind.Pack(MobaMinimapKind.Champion, (byte)CharacterClassNumber.FistMaster, true);
        Assert.That(packed, Is.EqualTo(0x80 | (6 << 4) | 1));
        Assert.That(MobaMinimapKind.IsRecalling(packed), Is.True);
        Assert.That(MobaMinimapKind.KindOf(packed), Is.EqualTo(MobaMinimapKind.Champion));
        Assert.That(MobaMinimapKind.ClassOf(packed), Is.EqualTo(6));

        var wizard = MobaMinimapKind.Pack(MobaMinimapKind.Champion, (byte)CharacterClassNumber.DarkWizard, true);
        Assert.That(wizard, Is.EqualTo(0x81));
        Assert.That(MobaMinimapKind.ClassOf(wizard), Is.EqualTo(0));
    }

    /// <summary>
    /// Turrets, nexus and wards keep bits 4-7 at 0, whatever class or recall flag is passed.
    /// </summary>
    /// <param name="kind">The entry kind.</param>
    [TestCase(MobaMinimapKind.Turret)]
    [TestCase(MobaMinimapKind.Nexus)]
    [TestCase(MobaMinimapKind.Ward)]
    public void NonChampionKindsAreUntouched(byte kind)
    {
        Assert.That(MobaMinimapKind.Pack(kind, 0, false), Is.EqualTo(kind));
        Assert.That(MobaMinimapKind.Pack(kind, (byte)CharacterClassNumber.FistMaster, true), Is.EqualTo(kind));
        Assert.That(MobaMinimapKind.Pack(kind, (byte)CharacterClassNumber.DarkLord, false), Is.EqualTo(kind));
        Assert.That(MobaMinimapKind.KindOf(kind), Is.EqualTo(kind));
        Assert.That(MobaMinimapKind.ClassOf(kind), Is.EqualTo(0));
        Assert.That(MobaMinimapKind.IsRecalling(kind), Is.False);
    }
}
