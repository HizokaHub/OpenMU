// <copyright file="MobaCreepDrops.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Lane creeps have a small chance to drop a MOBA-shop item on death - a free bonus on top
/// of the gold economy (<see cref="MobaGold"/>), rolled independently on level and excellent
/// count so a drop can land anywhere from a bare piece to a lucky near-full one. Only items
/// of the match's current phase (<see cref="MobaMatchPhase"/>) can drop - no T3 drops while
/// the match is still in its T1 phase. First-pass numbers; tune with <c>/mobabotfight</c>.
/// </summary>
public static class MobaCreepDrops
{
    /// <summary>Chance a creep death drops anything at all.</summary>
    public const double DropChance = 0.08;

    /// <summary>Tiles around the death position a drop may land, so multiple drops from one wave don't stack on one tile.</summary>
    private const int ScatterRadius = 2;

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
    /// Rolls a drop for one dead creep. Call from the creep-death handler alongside the
    /// EXP / gold grants.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="position">Where the creep died.</param>
    /// <param name="lastHitter">The champion that last-hit the creep (also the drop's class filter).</param>
    /// <param name="beneficiaries">Champions who get first pickup priority (the creep's beneficiary team).</param>
    public static async ValueTask TryDropAsync(GameMap map, Point position, Player lastHitter, IReadOnlyList<Player> beneficiaries)
    {
        if (!lastHitter.IsMobaClone || !Rand.NextRandomBool(DropChance))
        {
            return;
        }

        var tier = MobaMatchPhase.Current;
        var family = MobaPassives.FamilyOf(lastHitter);
        var candidates = MobaShopCatalog.Entries
            .Where(e => e.Tier == tier
                        && e.Category is MobaShopCategory.Weapons or MobaShopCategory.Sets or MobaShopCategory.SetsSustain or MobaShopCategory.Wings or MobaShopCategory.WingsUtility or MobaShopCategory.Accessories
                        && (e.Families is null || e.Families.Contains(family)))
            .ToList();
        if (candidates.Count == 0)
        {
            return;
        }

        // Pick the item first, then one of its variants, so an item with three mixes doesn't
        // out-drop one with a single mix.
        var itemGroups = candidates.GroupBy(e => (e.Group, e.Number)).ToList();
        var variants = itemGroups[Rand.NextInt(0, itemGroups.Count)].ToList();
        var entry = variants[Rand.NextInt(0, variants.Count)];
        if (lastHitter.GameContext.Configuration.Items.FirstOrDefault(d => d.Group == entry.Group && d.Number == entry.Number) is not { } definition)
        {
            return;
        }

        var (tierLevel, optionLevel, luck, tierExcellent) = MobaShop.TierStatsOf(tier);
        var excellent = Math.Min(RollExcellentCount() ?? tierExcellent, tierExcellent);
        var level = tierLevel + RollLevelOffset();
        var hasSkill = definition.Skill is not null && tier != MobaShopTier.T1;

        var item = MobaShop.CreateRolledItem(definition, level, optionLevel, luck, excellent, hasSkill, entry.Variant);
        var dropPosition = map.Terrain.GetRandomCoordinate(position, ScatterRadius);
        var dropped = new DroppedItem(item, dropPosition, map, null, beneficiaries.Cast<object>());
        await map.AddAsync(dropped).ConfigureAwait(false);
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
