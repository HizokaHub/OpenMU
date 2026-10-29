// <copyright file="MobaShopGuide.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// The recommended purchase path of the MOBA shop (2026-10-28): what a champion who knows nothing about items should buy, in order, so that the
/// "Para ti" tab and the left column of the shop panel can guide it. The steps are the same for every class (weapon, first set, shield or accessory, wings,
/// second set, second weapon) but they are the items of the class and the primary (Standard) set of each tier; the client marks what the champion already has.
/// </summary>
public static class MobaShopGuide
{
    private const byte ArmorGroup = 8;
    private const int ClientGroupSize = 512;

    /// <summary>The recommended steps of a class, in the order to buy them.</summary>
    /// <param name="configuration">The game configuration.</param>
    /// <param name="characterClass">The class of the champion.</param>
    /// <returns>The steps (the ones whose item does not exist are left out).</returns>
    public static IReadOnlyList<MobaShopRecommendation> StepsFor(GameConfiguration configuration, CharacterClass characterClass)
    {
        var family = MobaPassives.FamilyOf(characterClass.Number);
        var entries = MobaShopCatalog.Entries.Where(e => e.Families is { Length: > 0 } && e.Families[0] == family).ToList();
        var steps = new List<MobaShopRecommendation>();

        void Add(MobaShopRecommendation? step)
        {
            if (step is { } value)
            {
                steps.Add(value);
            }
        }

        Add(Single(configuration, entries, MobaShopCategory.Weapons, MobaShopTier.T1, MobaShopCatalog.WeaponsPage));
        Add(PrimarySet(configuration, entries, MobaShopTier.T1));
        Add(Single(configuration, entries, MobaShopCategory.Offhand, MobaShopTier.T1, MobaShopCatalog.OffhandPage)
            ?? Accessory(configuration, MobaShopTier.T1));
        Add(Single(configuration, entries, MobaShopCategory.Wings, MobaShopTier.T1, MobaShopCatalog.WingsPage));
        Add(PrimarySet(configuration, entries, MobaShopTier.T2));
        Add(Single(configuration, entries, MobaShopCategory.Weapons, MobaShopTier.T2, MobaShopCatalog.WeaponsPage));
        return steps;
    }

    /// <summary>The role (the variant) of every set the class is offered, by the armor piece of the set.</summary>
    /// <param name="characterClass">The class of the champion.</param>
    /// <returns>The roles.</returns>
    public static IReadOnlyList<MobaShopSetRole> SetRolesFor(CharacterClass characterClass)
    {
        var family = MobaPassives.FamilyOf(characterClass.Number);
        return MobaShopCatalog.Entries
            .Where(e => e.Category is MobaShopCategory.Sets or MobaShopCategory.SetsSustain && e.Group == ArmorGroup && e.Families is { Length: > 0 } && e.Families[0] == family)
            .Select(e => new MobaShopSetRole((ushort)((e.Group * ClientGroupSize) + e.Number), (byte)(int)e.Variant))
            .ToList();
    }

    private static MobaShopRecommendation? Single(GameConfiguration configuration, List<MobaShopEntry> entries, MobaShopCategory category, MobaShopTier tier, int page)
    {
        var entry = entries.FirstOrDefault(e => e.Category == category && e.Tier == tier && e.Variant == MobaShopVariant.Standard && e.Quantity == 1);
        return entry is null ? null : Step(configuration, entry, page, MobaShop.PriceOfEntry(configuration, entry));
    }

    private static MobaShopRecommendation? Accessory(GameConfiguration configuration, MobaShopTier tier)
    {
        var entry = MobaShopCatalog.Entries.FirstOrDefault(e => e.Category == MobaShopCategory.Accessories && e.Tier == tier);
        return entry is null ? null : Step(configuration, entry, MobaShopCatalog.WingsPage, MobaShop.PriceOfEntry(configuration, entry));
    }

    private static MobaShopRecommendation? PrimarySet(GameConfiguration configuration, List<MobaShopEntry> entries, MobaShopTier tier)
    {
        var armor = entries.FirstOrDefault(e => e.Category == MobaShopCategory.Sets && e.Tier == tier && e.Variant == MobaShopVariant.Standard && e.Group == ArmorGroup);
        if (armor is null)
        {
            return null;
        }

        var price = entries.Where(e => e.Category == MobaShopCategory.Sets && e.Tier == tier && e.Number == armor.Number).Sum(e => MobaShop.PriceOfEntry(configuration, e));
        return Step(configuration, armor, MobaShopCatalog.FirstSetsPage + (int)tier - 1, price);
    }

    private static MobaShopRecommendation? Step(GameConfiguration configuration, MobaShopEntry entry, int page, long price)
    {
        if (price <= 0 || !configuration.Items.Any(d => d.Group == entry.Group && d.Number == entry.Number))
        {
            return null;
        }

        return new MobaShopRecommendation((byte)page, (ushort)((entry.Group * ClientGroupSize) + entry.Number), (byte)(int)entry.Tier, (uint)Math.Clamp(price, 0, uint.MaxValue));
    }
}
