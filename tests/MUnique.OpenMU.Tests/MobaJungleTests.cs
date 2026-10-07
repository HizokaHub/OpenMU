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
/// Tests the jungle camp layout.
/// </summary>
[TestFixture]
public class MobaJungleTests
{
    /// <summary>
    /// Every camp has a valid rank and lies on or right next to walkable terrain.
    /// </summary>
    [Test]
    public void CampsAreOnWalkableGround()
    {
        Assert.That(MobaJungle.Camps, Is.Not.Empty);
        foreach (var camp in MobaJungle.Camps)
        {
            Assert.That(camp.Rank, Is.InRange(1, 3));
            var spot = MobaLayout.NearestWalkable(camp.X, camp.Y);
            Assert.That(Math.Max(Math.Abs(spot.X - camp.X), Math.Abs(spot.Y - camp.Y)), Is.LessThanOrEqualTo(2), $"camp {camp}");
        }
    }

    /// <summary>
    /// The monsters of the camps exist in the Season 6 configuration.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async ValueTask JungleMonstersExistInTheConfigurationAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        var configuration = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
        foreach (var name in new[] { "Mutant", "Bloody Wolf", "Tantallos" })
        {
            Assert.That(configuration.Monsters.Any(m => m.Designation == name), Is.True, name);
        }
    }
}
