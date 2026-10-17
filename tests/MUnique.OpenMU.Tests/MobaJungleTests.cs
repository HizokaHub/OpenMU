// <copyright file="MobaJungleTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Tests the jungle layout.
/// </summary>
[TestFixture]
public class MobaJungleTests
{
    /// <summary>
    /// The jungle has the 15 spots of the blue half and their turn for the red half (Kundun is not copied), all of them on walkable ground.
    /// </summary>
    [Test]
    public void SpotsAreOnWalkableGround()
    {
        Assert.That(MobaJungle.BlueSpots, Has.Count.EqualTo(15));
        Assert.That(MobaJungle.Spots, Has.Count.EqualTo(29));
        foreach (var spot in MobaJungle.BlueSpots)
        {
            Assert.That(MobaLayout.IsWalkable(spot.X, spot.Y), Is.True, $"spot {spot.Name} ({spot.X},{spot.Y})");
        }

        // The mask is not a perfect 180 degree turn any more (walls were closed by hand on one side): the red spot may sit on a wall cell, but next to open ground.
        foreach (var spot in MobaJungle.Spots)
        {
            var near = MobaLayout.NearestWalkable(spot.X, spot.Y);
            Assert.That(Math.Max(Math.Abs(near.X - spot.X), Math.Abs(near.Y - spot.Y)), Is.LessThanOrEqualTo(2), $"spot {spot.Name} ({spot.X},{spot.Y})");
        }

        foreach (var blue in MobaJungle.BlueSpots.Where(s => !s.Name.Contains("Kundun")))
        {
            var red = MobaJungle.Spots.Single(s => s.Name == "R" + blue.Name[1..]);
            Assert.That((red.X, red.Y), Is.EqualTo((236 - blue.X, 255 - blue.Y)), blue.Name);
        }
    }

    /// <summary>
    /// The monsters of every spot stand on different walkable cells close to their spot, small ones in front of the big ones.
    /// </summary>
    [Test]
    public void FormationsAreWalkableDistinctAndSmallFirst()
    {
        foreach (var spot in MobaJungle.Spots)
        {
            var members = MobaJungle.MembersOf(spot);
            Assert.That(members, Is.Not.Empty, spot.Name);
            Assert.That(members.Select(m => (m.Slot.X, m.Slot.Y)).Distinct().Count(), Is.EqualTo(members.Count), $"{spot.Name}: two monsters on one cell");
            foreach (var (kind, _, slot) in members)
            {
                Assert.That(MobaLayout.IsWalkable(slot.X, slot.Y), Is.True, $"{spot.Name} {kind} at ({slot.X},{slot.Y})");
                Assert.That(Math.Max(Math.Abs(slot.X - spot.X), Math.Abs(slot.Y - spot.Y)), Is.LessThanOrEqualTo(10), $"{spot.Name} {kind}");
            }

            // The front row has the smallest monsters: rows never get a smaller size class than the row before.
            var rows = members.GroupBy(m => m.Slot.Row).OrderBy(g => g.Key).Select(g => g.Max(m => MobaJungle.Info[m.Kind].Size)).ToList();
            Assert.That(rows, Is.Ordered, spot.Name);
        }
    }

    /// <summary>
    /// The monsters of the jungle exist in the Season 6 configuration (or the one they are cloned from does).
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async ValueTask JungleMonstersExistInTheConfigurationAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        var configuration = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
        foreach (var info in MobaJungle.Info.Values)
        {
            var name = info.ClonedFrom ?? info.Name;
            Assert.That(configuration.Monsters.Any(m => m.Designation == name), Is.True, name);
        }
    }
}
