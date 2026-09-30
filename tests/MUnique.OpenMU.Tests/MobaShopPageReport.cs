// <copyright file="MobaShopPageReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.IO;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>Lists what every menu page of the MOBA shop holds for a class (not part of the regular suite).</summary>
[TestFixture]
[Explicit("Curation report, run by hand.")]
public class MobaShopPageReport
{
    /// <summary>Writes <c>moba-shop-pages.txt</c> to the temp folder.</summary>
    /// <returns>A task.</returns>
    [Test]
    public async ValueTask WriteReportAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        var config = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
        var sb = new StringBuilder();
        foreach (var classNumber in new byte[] { 17 })
        {
            var characterClass = config.CharacterClasses.First(c => c.Number == classNumber);
            for (var page = 0; page < MobaShopCatalog.Pages.Count; page++)
            {
                var items = MobaShop.BuildPageItems(config, characterClass, page, out var overflow);
                sb.AppendLine($"== {characterClass.Name} page {page} '{MobaShopCatalog.Pages[page].Name}' items={items.Count} overflow={overflow}");
                foreach (var item in items)
                {
                    sb.AppendLine($"  slot {item.ItemSlot}: {item.Definition?.Group}/{item.Definition?.Number} {item.Definition?.Name} +{item.Level} price={MobaShop.PriceOf(item)}");
                }
            }
        }

        File.WriteAllText(Path.Combine(Path.GetTempPath(), "moba-shop-pages.txt"), sb.ToString());
    }
}
