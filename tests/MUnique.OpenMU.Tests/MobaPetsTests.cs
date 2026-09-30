// <copyright file="MobaPetsTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>Tests of the MOBA pet balance.</summary>
[TestFixture]
public class MobaPetsTests
{
    /// <summary>Dark Horse (Earthshake) + Dark Raven are about 25 % of a Dark Lord's damage at every stage of a match.</summary>
    /// <param name="level">The champion level.</param>
    /// <param name="rank">The skill rank.</param>
    [TestCase(8, 2)]
    [TestCase(15, 3)]
    [TestCase(22, 4)]
    [TestCase(30, 5)]
    public void PetsAreAboutAQuarterOfDarkLordDamage(int level, int rank)
    {
        var (earthshake, raven) = MobaPets.DarkLordPetShare(level, rank);
        TestContext.Out.WriteLine($"nivel {level} rango {rank}: Earthshake {earthshake:P1}, Cuervo {raven:P1}, total {earthshake + raven:P1}");
        Assert.Multiple(() =>
        {
            Assert.That(earthshake + raven, Is.InRange(0.22, 0.28), "total");
            Assert.That(earthshake, Is.InRange(0.11, 0.19), "Earthshake");
            Assert.That(raven, Is.InRange(0.06, 0.14), "Cuervo");
        });
    }

    /// <summary>The pet experience formula matches the pet definitions.</summary>
    [Test]
    public void MaxLevelPetExperienceFitsAnInt()
        => Assert.That(MobaPets.PetExperienceOfLevel(MobaPets.MaxPetLevel), Is.EqualTo(750_000_000));
}
