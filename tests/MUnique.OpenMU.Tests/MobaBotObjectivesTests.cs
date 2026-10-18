// <copyright file="MobaBotObjectivesTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the choice of the bots that go for a jungle objective.
/// </summary>
[TestFixture]
public class MobaBotObjectivesTests
{
    /// <summary>
    /// The jungler is always called first and the nearest bots follow; far or hurt bots stay out.
    /// </summary>
    [Test]
    public void JunglerFirstThenNearestAndFarOrHurtStayOut()
    {
        var candidates = new[]
        {
            new MobaBotObjectives.Candidate(0, 40, 0.9f, false),
            new MobaBotObjectives.Candidate(1, 80, 0.9f, true),
            new MobaBotObjectives.Candidate(2, 20, 0.9f, false),
            new MobaBotObjectives.Candidate(3, 200, 1.0f, false),
            new MobaBotObjectives.Candidate(4, 10, 0.3f, false),
            new MobaBotObjectives.Candidate(5, 60, 0.8f, false),
        };

        var ids = MobaBotObjectives.ChooseParticipants(candidates, need: 3, take: 4, maxDistance: MobaBotObjectives.MaxTravelTiles, minHealth: MobaBotObjectives.MinHealth);
        Assert.That(ids, Is.EqualTo(new[] { 1, 2, 0, 5 }));
    }

    /// <summary>
    /// With fewer eligible bots than needed nobody is called.
    /// </summary>
    [Test]
    public void NobodyIsCalledWithoutEnoughBots()
    {
        var candidates = new[]
        {
            new MobaBotObjectives.Candidate(0, 40, 0.9f, false),
            new MobaBotObjectives.Candidate(1, 150, 0.9f, true),
        };

        Assert.That(MobaBotObjectives.ChooseParticipants(candidates, 2, 3, MobaBotObjectives.MaxTravelTiles, MobaBotObjectives.MinHealth), Is.Empty);
    }

    /// <summary>
    /// The bigger the objective, the more champions and the higher the level it asks for.
    /// </summary>
    [Test]
    public void BiggerObjectivesAskForMore()
    {
        var medusa = MobaBotObjectives.RequirementsOf(MobaObjectiveKind.Medusa);
        var god = MobaBotObjectives.RequirementsOf(MobaObjectiveKind.GodOfDarkness);
        var kundun = MobaBotObjectives.RequirementsOf(MobaObjectiveKind.Kundun);
        Assert.That(medusa.Required, Is.LessThan(god.Required));
        Assert.That(god.Required, Is.LessThan(kundun.Required));
        Assert.That(medusa.MinLevel, Is.LessThan(kundun.MinLevel));
    }

    /// <summary>
    /// The eggs of Selupan hatch one, two or three spiders with 45, 35 and 20 percent.
    /// </summary>
    [Test]
    public void EggsHatchOneTwoOrThreeSpiders()
    {
        Assert.That(MobaSelupanEvent.SpidersFor(0.0), Is.EqualTo(1));
        Assert.That(MobaSelupanEvent.SpidersFor(0.44), Is.EqualTo(1));
        Assert.That(MobaSelupanEvent.SpidersFor(0.50), Is.EqualTo(2));
        Assert.That(MobaSelupanEvent.SpidersFor(0.79), Is.EqualTo(2));
        Assert.That(MobaSelupanEvent.SpidersFor(0.85), Is.EqualTo(3));
        Assert.That(MobaSelupanEvent.EggSpots, Has.Count.EqualTo(8));
    }

    /// <summary>
    /// A closed pocket of the walk grid is another region than the open field, so a rally point inside it is recognised as unreachable.
    /// </summary>
    [Test]
    public void ClosedPocketIsAnotherRegion()
    {
        var grid = new byte[256, 256];
        for (var x = 0; x < 256; x++)
        {
            for (var y = 0; y < 256; y++)
            {
                grid[x, y] = 1;
            }
        }

        // A 5x5 room walled in at (100..104, 100..104) with a 1-cell wall around it.
        for (var x = 99; x <= 105; x++)
        {
            for (var y = 99; y <= 105; y++)
            {
                grid[x, y] = (x is 99 or 105 || y is 99 or 105) ? (byte)0 : (byte)1;
            }
        }

        var open = MobaNavigation.RegionOf(grid, 10, 10);
        var room = MobaNavigation.RegionOf(grid, 102, 102);
        Assert.That(open, Is.GreaterThan(0));
        Assert.That(room, Is.GreaterThan(0));
        Assert.That(room, Is.Not.EqualTo(open));
        Assert.That(MobaNavigation.RegionOf(grid, 99, 99), Is.EqualTo(0));
        Assert.That(MobaNavigation.RegionOf(grid, 200, 200), Is.EqualTo(open));
    }

    /// <summary>
    /// Against a structure the champion damage no longer collapses at high levels: from level 12 on it only drops slowly.
    /// </summary>
    [Test]
    public void StructureDamageDivisorGrowsSlowly()
    {
        Assert.That(MobaStructures.LevelExtraDivisor(5), Is.EqualTo(1.0).Within(0.001));
        Assert.That(MobaStructures.LevelExtraDivisor(30), Is.LessThan(2.5));
        Assert.That(MobaStructures.LevelExtraDivisor(30), Is.GreaterThan(MobaStructures.LevelExtraDivisor(12)));
    }
}
