// <copyright file="MobaShopTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.DataModel;
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

    /// <summary>Every family sees items on every menu page, they all fit on the merchant grid and no page is nearly empty.</summary>
    /// <param name="classNumber">The class number.</param>
    [TestCaseSource(nameof(FamilyClassNumbers))]
    public void EveryPageHasItemsAndFits(byte classNumber)
    {
        var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
        for (var page = 0; page < MobaShopCatalog.Pages.Count; page++)
        {
            var items = MobaShop.BuildPageItems(this._gameConfiguration, characterClass, page, out var overflow);
            var cells = items.Sum(i => i.Definition!.Width * i.Definition.Height);
            var name = $"{characterClass.Name}: {MobaShopCatalog.Pages[page].Name}";
            TestContext.Out.WriteLine($"{name} -> {cells} celdas");

            // Each category starts on its own row: the row ranges of the categories never overlap
            // and follow the page's category order.
            var rowRanges = items
                .GroupBy(i => MobaShopCatalog.Entries.First(e => MobaShopCatalog.Pages[page].Categories.Contains(e.Category) && e.Group == i.Definition!.Group && e.Number == i.Definition.Number).Category)
                .Select(g => (Category: g.Key, Min: g.Min(i => i.ItemSlot / MobaShop.GridColumns), Max: g.Max(i => (i.ItemSlot / MobaShop.GridColumns) + i.Definition!.Height - 1)))
                .OrderBy(r => MobaShopCatalog.Pages[page].Categories.ToList().IndexOf(r.Category))
                .ToList();
            for (var i = 1; i < rowRanges.Count; i++)
            {
                Assert.That(rowRanges[i].Min, Is.GreaterThan(rowRanges[i - 1].Max), name + " categories overlap in rows");
            }

            Assert.Multiple(() =>
            {
                Assert.That(items, Is.Not.Empty, name);
                Assert.That(overflow, Is.Zero, name);
                Assert.That(cells, Is.GreaterThanOrEqualTo(20), name + " is nearly empty");
                Assert.That(items.Select(i => i.ItemSlot).Distinct().Count(), Is.EqualTo(items.Count), name + " slots");
            });
        }
    }

    /// <summary>Each tier of a slot costs 2.5 times the previous one (a 150 % gap): 100 / 250 / 625.</summary>
    [Test]
    public void TiersHaveAOneHundredFiftyPercentGap()
    {
        foreach (var family in new[] { MobaFamily.Wizard, MobaFamily.Knight, MobaFamily.Elf, MobaFamily.Summoner })
        {
            var weapons = MobaShopCatalog.Entries
                .Where(e => e.Category == MobaShopCategory.Weapons && e.Families!.Contains(family) && e.Variant == MobaShopVariant.Standard && e.Quantity == 1)
                .GroupBy(e => e.Tier)
                .ToDictionary(g => g.Key, g => MobaShop.PriceOfEntry(this._gameConfiguration, g.First()));
            Assert.Multiple(() =>
            {
                Assert.That(weapons[MobaShopTier.T2], Is.EqualTo(weapons[MobaShopTier.T1] * MobaShop.TierPriceRatio).Within(20), family.ToString());
                Assert.That(weapons[MobaShopTier.T3], Is.EqualTo(weapons[MobaShopTier.T1] * MobaShop.TierPriceRatio * MobaShop.TierPriceRatio).Within(50), family.ToString());
            });
        }
    }

    /// <summary>The Aegis barrier grows with the off-hand tier.</summary>
    [Test]
    public void AegisBarrierGrowsWithOffhandTier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(MobaCastEffects.AegisFractionOf(0), Is.EqualTo(0.05));
            Assert.That(MobaCastEffects.AegisFractionOf(1), Is.EqualTo(0.10));
            Assert.That(MobaCastEffects.AegisFractionOf(2), Is.EqualTo(0.15));
            Assert.That(MobaCastEffects.AegisFractionOf(3), Is.EqualTo(0.20));
        });
    }

    /// <summary>Every family that can carry an off-hand item sees one per tier; Elf and Rage Fighter see none.</summary>
    [Test]
    public void OffhandItemsExistPerTierForShieldClasses()
    {
        foreach (var family in new[] { MobaFamily.Wizard, MobaFamily.Knight, MobaFamily.MagicGladiator, MobaFamily.DarkLord, MobaFamily.Summoner })
        {
            var tiers = MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Offhand && e.Families!.Contains(family)).Select(e => e.Tier).OrderBy(t => t);
            Assert.That(tiers, Is.EqualTo(new[] { MobaShopTier.T1, MobaShopTier.T2, MobaShopTier.T3 }), family.ToString());
        }

        Assert.That(MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Offhand && (e.Families!.Contains(MobaFamily.Elf) || e.Families.Contains(MobaFamily.RageFighter))), Is.Empty);
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
            for (var category = 0; category < MobaShopCatalog.Pages.Count; category++)
            {
                foreach (var item in MobaShop.BuildPageItems(this._gameConfiguration, characterClass, category, out _))
                {
                    serializer.SerializeItem(buffer, item);
                    var flags = buffer[OptionsByteIndex];
                    var hasOption = (flags & HasOptionFlag) != 0;
                    var optionLevel = hasOption ? buffer[OptionByteIndex] & 0xF : 0;
                    var excellentByte = (flags & HasExcellentFlag) != 0 ? buffer[OptionByteIndex + (hasOption ? 1 : 0)] : 0;
                    var excellentCount = System.Numerics.BitOperations.PopCount((uint)(excellentByte & ExcellentMask));

                    var expectedOption = item.ItemOptions.FirstOrDefault(o => o.ItemOption?.OptionType == ItemOptionTypes.Option)?.Level ?? 0;
                    var expectedExcellent = MobaShop.ExcellentCountOf(item);
                    var expectedLuck = item.ItemOptions.Any(o => o.ItemOption?.OptionType == ItemOptionTypes.Luck);
                    var name = $"{characterClass.Name} {category} {item.Definition!.Name}";
                    Assert.Multiple(() =>
                    {
                        Assert.That(buffer[2], Is.EqualTo(item.IsTrainablePet() ? 0 : item.Level), name + " level");
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

    /// <summary>Dumps how many of the 120 grid cells each category page uses, per class family.</summary>
    [Test]
    [Explicit("Report only")]
    public void CellsPerPageReport()
    {
        var lines = new List<string>();
        foreach (var classNumber in FamilyClassNumbers)
        {
            var characterClass = this._gameConfiguration.CharacterClasses.First(c => c.Number == classNumber);
            var parts = Enumerable.Range(0, MobaShopCatalog.Pages.Count).Select(page =>
            {
                var items = MobaShop.BuildPageItems(this._gameConfiguration, characterClass, page, out var overflow);
                return $"{MobaShopCatalog.Pages[page].Name}={items.Sum(i => i.Definition!.Width * i.Definition.Height)}(+{overflow} overflow)";
            });
            lines.Add($"{characterClass.Name}: {string.Join("  ", parts)}");
        }

        System.IO.File.WriteAllLines(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "moba-cells.txt"), lines);
    }
}
