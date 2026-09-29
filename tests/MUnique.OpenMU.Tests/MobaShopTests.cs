// <copyright file="MobaShopTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.GameServer.RemoteView;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Tests the MOBA shop catalog and prices against the real Season 6 configuration.
/// </summary>
[TestFixture]
public class MobaShopTests
{
    private const byte HasOptionFlag = 0x01;
    private const byte HasLuckFlag = 0x02;
    private const byte HasExcellentFlag = 0x08;
    private const int OptionsByteIndex = 4;
    private const int OptionByteIndex = 5;
    private const byte ExcellentMask = 0x3F;

    // Third-class representative of every champion family.
    private static readonly byte[] FamilyClassNumbers = { 3, 7, 11, 13, 17, 23, 25 };

    private GameConfiguration _gameConfiguration = null!;

    /// <summary>Loads the Season 6 configuration.</summary>
    [OneTimeSetUp]
    public async ValueTask SetupAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        this._gameConfiguration = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
    }

    /// <summary>Every non-set catalog entry points to an existing item definition.</summary>
    [Test]
    public void CatalogItemsExist()
    {
        var missing = MobaShopCatalog.Entries
            .Where(e => e.Category is not (MobaShopCategory.Sets or MobaShopCategory.SetsSustain))
            .Where(e => !this._gameConfiguration.Items.Any(d => d.Group == e.Group && d.Number == e.Number))
            .Select(e => $"{e.Category} {e.Group},{e.Number}")
            .ToList();

        Assert.That(missing, Is.Empty);
    }

    /// <summary>Every family sees items in every category, and they all fit on the merchant grid.</summary>
    /// <param name="classNumber">The class number.</param>
    [TestCaseSource(nameof(FamilyClassNumbers))]
    public void EveryCategoryHasItemsAndFits(byte classNumber)
    {
        var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
        foreach (var category in Enum.GetValues<MobaShopCategory>())
        {
            var items = MobaShop.BuildCategoryItems(this._gameConfiguration, characterClass, category, out var overflow);
            Assert.Multiple(() =>
            {
                Assert.That(items, Is.Not.Empty, $"{characterClass.Name}: {category}");
                Assert.That(overflow, Is.Zero, $"{characterClass.Name}: {category}");
                Assert.That(items.Select(i => i.ItemSlot).Distinct().Count(), Is.EqualTo(items.Count), $"{characterClass.Name}: {category} slots");
            });
        }
    }

    /// <summary>Higher tiers of the same category cost more.</summary>
    [Test]
    public void HigherTiersCostMore()
    {
        var grandMaster = this._gameConfiguration.CharacterClasses.First(c => c.Number == 3);
        var weapons = MobaShop.BuildCategoryItems(this._gameConfiguration, grandMaster, MobaShopCategory.Weapons, out _);
        var prices = weapons.Select(MobaShop.PriceOf).ToList();

        Assert.That(prices, Is.Ordered.Ascending);
        Assert.That(prices.First(), Is.GreaterThan(0));
    }

    /// <summary>
    /// The client looks prices up by the properties it reads from the item packet; the
    /// serialized item must carry exactly the upgrades the shop gave the item.
    /// </summary>
    [Test]
    public void SerializedItemsMatchPriceKey()
    {
        var serializer = new ItemSerializerExtended();
        var buffer = new byte[serializer.NeededSpace];
        foreach (var classNumber in FamilyClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            foreach (var category in Enum.GetValues<MobaShopCategory>())
            {
                foreach (var item in MobaShop.BuildCategoryItems(this._gameConfiguration, characterClass, category, out _))
                {
                    serializer.SerializeItem(buffer, item);
                    var flags = buffer[OptionsByteIndex];
                    var hasOption = (flags & HasOptionFlag) != 0;
                    var optionLevel = hasOption ? buffer[OptionByteIndex] & 0xF : 0;
                    var excellentByte = (flags & HasExcellentFlag) != 0 ? buffer[OptionByteIndex + (hasOption ? 1 : 0)] : 0;
                    var excellentCount = System.Numerics.BitOperations.PopCount((uint)(excellentByte & ExcellentMask));

                    var expectedOption = item.ItemOptions.FirstOrDefault(o => o.ItemOption?.OptionType == ItemOptionTypes.Option)?.Level ?? 0;
                    var expectedExcellent = item.ItemOptions.Count(o => o.ItemOption?.OptionType == ItemOptionTypes.Excellent || o.ItemOption?.OptionType == ItemOptionTypes.Wing);
                    var expectedLuck = item.ItemOptions.Any(o => o.ItemOption?.OptionType == ItemOptionTypes.Luck);
                    var name = $"{characterClass.Name} {category} {item.Definition!.Name}";
                    Assert.Multiple(() =>
                    {
                        Assert.That(buffer[2], Is.EqualTo(item.Level), name + " level");
                        Assert.That(optionLevel, Is.EqualTo(expectedOption), name + " option");
                        Assert.That((flags & HasLuckFlag) != 0, Is.EqualTo(expectedLuck), name + " luck");
                        Assert.That(excellentCount, Is.EqualTo(expectedExcellent), name + " excellent");
                    });
                }
            }
        }
    }

    /// <summary>
    /// Variants of the same piece must really differ in the options they carry (T1/T2 weapons), while keeping the same option count so they cost the same.
    /// </summary>
    /// <param name="classNumber">The class number.</param>
    [TestCaseSource(nameof(FamilyClassNumbers))]
    public void VariantsCarryDifferentOptionsAtSamePrice(byte classNumber)
    {
        var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
        foreach (var category in new[] { MobaShopCategory.Weapons })
        {
            var items = MobaShop.BuildCategoryItems(this._gameConfiguration, characterClass, category, out _).ToList();

            var byType = items
                .Where(i => i.Definition!.Group != 4 || i.Definition.Number != 15)
                .GroupBy(i => (i.Definition!.Group, i.Definition.Number, i.Level))
                .Where(g => g.Count() > 1)
                .ToList();
            Assert.That(byType, Is.Not.Empty, $"{characterClass.Name}: {category} has no variants");
            foreach (var group in byType)
            {
                var signatures = group
                    .Select(i => string.Join(",", i.ItemOptions.Where(o => o.ItemOption?.OptionType != ItemOptionTypes.Luck && o.ItemOption?.OptionType != ItemOptionTypes.Option).Select(o => o.ItemOption!.Number).OrderBy(n => n)))
                    .ToList();
                Assert.Multiple(() =>
                {
                    Assert.That(signatures.Distinct().Count(), Is.EqualTo(signatures.Count), $"{characterClass.Name}: {category} {group.Key} variants are identical: {string.Join(" | ", signatures)}");
                    Assert.That(group.Select(MobaShop.PriceOf).Distinct().Count(), Is.EqualTo(1), $"{characterClass.Name}: {category} {group.Key} variants differ in price");
                });
            }
        }
    }

    /// <summary>Wings are sold full option and without variants: one entry per wing and tier.</summary>
    /// <param name="classNumber">The class number.</param>
    [TestCaseSource(nameof(FamilyClassNumbers))]
    public void WingsAreFullOptionWithoutVariants(byte classNumber)
    {
        var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
        var wings = MobaShop.BuildCategoryItems(this._gameConfiguration, characterClass, MobaShopCategory.Wings, out _);
        Assert.That(wings.GroupBy(w => (w.Definition!.Group, w.Definition.Number)).All(g => g.Count() == 1), Is.True, $"{characterClass.Name}: repeated wing");
        foreach (var wing in wings)
        {
            var name = $"{characterClass.Name} {wing.Definition!.Name}";
            Assert.Multiple(() =>
            {
                Assert.That(wing.Level, Is.EqualTo(wing.Definition.MaximumItemLevel), name + " level");
                Assert.That(wing.ItemOptions.Any(o => o.ItemOption?.OptionType == ItemOptionTypes.Luck), Is.True, name + " luck");
                Assert.That(wing.ItemOptions.First(o => o.ItemOption?.OptionType == ItemOptionTypes.Option).Level, Is.EqualTo(4), name + " option");
                Assert.That(wing.ItemOptions.Count(o => o.ItemOption?.OptionType == ItemOptionTypes.Wing), Is.LessThanOrEqualTo(1), name + " wing option");
            });
        }
    }
}
