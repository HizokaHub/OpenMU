// <copyright file="MobaCreepDrops.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Configuration.Items;

/// <summary>
/// Lane creeps drop (through <see cref="MobaCreepDropGenerator"/>) nothing 35 %, Zen 35 % or a MOBA-shop item 30 % on death - a free bonus on top
/// of the gold economy (<see cref="MobaGold"/>), rolled independently on level and excellent
/// count so a drop can land anywhere from a bare piece to a lucky near-full one. Only items
/// of the match's current phase (<see cref="MobaMatchPhase"/>) can drop - no T3 drops while
/// the match is still in its T1 phase. First-pass numbers; tune with <c>/mobabotfight</c>.
/// </summary>
public static class MobaCreepDrops
{
    /// <summary>Chance that a creep death drops nothing.</summary>
    public const double NothingChance = 0.35;

    /// <summary>Chance that a creep death drops Zen (native <c>DroppedMoney</c>, picked up from the ground).</summary>
    public const double ZenChance = 0.35;

    /// <summary>Chance that a creep death drops an item (the remaining 30 %).</summary>
    public const double ItemChance = 1.0 - NothingChance - ZenChance;

    /// <summary>Share of the item drops that are weapons / shields; the rest are sets.</summary>
    public const double WeaponsAndShieldsShare = 0.65;

    /// <summary>Excellent-option count roll, independent of the level roll. "Full" resolves to the phase tier's own guaranteed count (see <see cref="MobaShop.TierStatsOf"/>).</summary>
    private static readonly (int? Count, double Weight)[] ExcellentCountRoll =
    {
        (null, 0.10),  // full (tier max)
        (3, 0.20),
        (2, 0.30),
        (1, 0.25),
        (0, 0.15),
    };

    /// <summary>Item-level roll, independent of the excellent-option roll, as an offset from the phase tier's guaranteed level.</summary>
    private static readonly (int Offset, double Weight)[] LevelOffsetRoll =
    {
        (0, 0.10),
        (-1, 0.20),
        (-2, 0.30),
        (-3, 0.25),
        (-4, 0.15),
    };

    /// <summary>
    /// Builds the item a dead creep drops for <paramref name="lastHitter"/> (its class filters the items): weapons and
    /// shields 65 % of the time, sets 35 %. The roll of WHETHER something drops (nothing / Zen / item) is made by
    /// <see cref="MobaCreepDropGenerator"/>.
    /// </summary>
    /// <param name="lastHitter">The champion that last-hit the creep.</param>
    /// <returns>The item, or <see langword="null"/> if there is nothing to drop for that champion and phase.</returns>
    public static async ValueTask<Item?> CreateItemAsync(Player lastHitter)
    {
        if (!lastHitter.IsMobaClone)
        {
            return null;
        }

        var tier = MobaMatchPhase.Current;
        var family = MobaPassives.FamilyOf(lastHitter);
        var all = MobaShopCatalog.Entries
            .Where(e => e.Tier == tier && (e.Families is null || e.Families.Contains(family)))
            .ToList();
        var arms = all.Where(e => e.Category is MobaShopCategory.Weapons or MobaShopCategory.Offhand).ToList();
        var sets = all.Where(e => e.Category is MobaShopCategory.Sets or MobaShopCategory.SetsSustain).ToList();
        var candidates = Rand.NextRandomBool(WeaponsAndShieldsShare) ? arms : sets;
        if (candidates.Count == 0)
        {
            candidates = arms.Count > 0 ? arms : sets;
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        // Pick the item first, then one of its variants, so an item with three mixes doesn't
        // out-drop one with a single mix.
        var itemGroups = candidates.GroupBy(e => (e.Group, e.Number)).ToList();
        var variants = itemGroups[Rand.NextInt(0, itemGroups.Count)].ToList();
        var entry = variants[Rand.NextInt(0, variants.Count)];
        if (lastHitter.GameContext.Configuration.Items.FirstOrDefault(d => d.Group == entry.Group && d.Number == entry.Number) is not { } definition)
        {
            return null;
        }

        var (tierLevel, optionLevel, luck, tierExcellent) = MobaShop.TierStatsOf(tier);
        var excellent = Math.Min(RollExcellentCount() ?? tierExcellent, tierExcellent);
        var level = tierLevel + RollLevelOffset();
        var hasSkill = definition.Skill is not null && (tier != MobaShopTier.T1 || entry.Category == MobaShopCategory.Offhand);

        var item = MobaShop.CreateRolledItem(definition, level, optionLevel, luck, excellent, hasSkill, entry.Variant);
        MobaDropReport.NoteItem(lastHitter, item, entry.Category, entry.Variant);
        if (entry.Category == MobaShopCategory.Offhand && MobaItemTraits.AssignDropOptions(item, tier))
        {
            // A new kind of shield: tell every champion so the tooltip shows its options.
            foreach (var champion in (await lastHitter.GameContext.GetPlayersAsync().ConfigureAwait(false)).Where(p => p.IsMobaClone))
            {
                await champion.InvokeViewPlugInAsync<Views.Moba.IMobaShopPlugIn>(p => p.ShowShieldOptionsAsync(MobaItemTraits.ShieldOptionTable)).ConfigureAwait(false);
            }
        }

        return item;
    }

    private static int? RollExcellentCount() => WeightedPick(ExcellentCountRoll);

    private static int RollLevelOffset() => WeightedPick(LevelOffsetRoll);

    private static T WeightedPick<T>((T Value, double Weight)[] options)
    {
        var roll = Rand.NextDouble() * options.Sum(o => o.Weight);
        var acc = 0.0;
        foreach (var (value, weight) in options)
        {
            acc += weight;
            if (roll < acc)
            {
                return value;
            }
        }

        return options[^1].Value;
    }
}

/// <summary>
/// Drop generator of the lane creeps: replaces the native drop groups of the monster definition (the native items
/// are gone) with nothing 35 % / Zen 35 % (native amount: experience + 7) / MOBA item 30 %.
/// </summary>
public sealed class MobaCreepDropGenerator : IDropGenerator
{
    private const int BaseMoneyDrop = 7;

    /// <inheritdoc />
    public async ValueTask<(IEnumerable<Item> Items, uint? Money)> GenerateItemDropsAsync(MonsterDefinition monster, int gainedExperience, Player player)
    {
        var roll = Rand.NextDouble();
        if (roll < MobaCreepDrops.NothingChance)
        {
            MobaDropReport.NoteRoll(player, monster.Designation, "nothing", 0);
            return (Enumerable.Empty<Item>(), null);
        }

        if (roll < MobaCreepDrops.NothingChance + MobaCreepDrops.ZenChance)
        {
            var zen = (uint)Math.Max(1, gainedExperience + BaseMoneyDrop);
            MobaDropReport.NoteRoll(player, monster.Designation, "zen", zen);
            return (Enumerable.Empty<Item>(), zen);
        }

        MobaDropReport.NoteRoll(player, monster.Designation, "item", 0);
        var item = await MobaCreepDrops.CreateItemAsync(player).ConfigureAwait(false);
        return (item is null ? Enumerable.Empty<Item>() : new[] { item }, null);
    }

    /// <inheritdoc />
    public Item? GenerateItemDrop(DropItemGroup group) => null;

    /// <inheritdoc />
    public (Item? Item, uint? Money, ItemDropEffect DropEffect) GenerateItemDrop(IEnumerable<DropItemGroup> groups) => (null, null, default);
}
