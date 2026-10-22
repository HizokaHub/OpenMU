// <copyright file="MobaJungleDrops.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic.NPC;

/// <summary>
/// The drops of the jungle (2026-10-17, requested by the user). Camps drop items of +6 to +9, the eggs of Selupan +6 / +7 and its spiders +7 to +9.
/// The mini-bosses (Medusa, Dark Phoenix, God of Darkness) drop +9 / +10 and the great ones (Kundun, Selupan) +11 / +12, and not at random: every champion of the
/// team that landed the last hit AND hurt the boss gets one item, and the more of the damage it did, the better that item is (level and excellent options).
/// </summary>
public static class MobaJungleDrops
{
    /// <summary>A champion must have done this fraction of the boss's life in damage to get an item (a poke does not count).</summary>
    public const double MinDamageShare = 0.02;

    private static readonly Dictionary<MobaJungle.Kind, IDropGenerator> Generators = new();

    /// <summary>Whether the drops of the kind are given to the champions that hurt it (the bosses) instead of rolled for the last hitter.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="true"/> for Medusa, Dark Phoenix, God of Darkness, Kundun and Selupan.</returns>
    public static bool SplitsByDamage(MobaJungle.Kind kind)
        => kind is MobaJungle.Kind.Medusa or MobaJungle.Kind.DarkPhoenix or MobaJungle.Kind.GodOfDarkness or MobaJungle.Kind.Kundun or MobaJungle.Kind.Selupan;

    /// <summary>The item levels a kind drops (inclusive).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The lowest and the highest level.</returns>
    public static (int Min, int Max) LevelsOf(MobaJungle.Kind kind) => kind switch
    {
        MobaJungle.Kind.SpiderEgg1 or MobaJungle.Kind.SpiderEgg2 or MobaJungle.Kind.SpiderEgg3 => (6, 7),
        MobaJungle.Kind.Spider => (7, 9),
        MobaJungle.Kind.Medusa or MobaJungle.Kind.DarkPhoenix or MobaJungle.Kind.GodOfDarkness => (9, 10),
        MobaJungle.Kind.Kundun or MobaJungle.Kind.Selupan => (11, 12),
        _ => (6, 9),
    };

    /// <summary>The chance that a death of the kind drops an item (camps, eggs and spiders).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The chance, 0 to 1.</returns>
    public static double ItemChanceOf(MobaJungle.Kind kind) => kind switch
    {
        MobaJungle.Kind.SpiderEgg1 or MobaJungle.Kind.SpiderEgg2 or MobaJungle.Kind.SpiderEgg3 => 0.50,
        MobaJungle.Kind.Spider => 0.35,
        MobaJungle.Kind.Tantallos or MobaJungle.Kind.BloodyDeathRider => 0.18,
        _ => 0.12,
    };

    /// <summary>Gets the drop generator of a kind (one instance per kind).</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The generator.</returns>
    public static IDropGenerator GeneratorOf(MobaJungle.Kind kind)
    {
        lock (Generators)
        {
            if (!Generators.TryGetValue(kind, out var generator))
            {
                generator = new MobaJungleDropGenerator(kind);
                Generators[kind] = generator;
            }

            return generator;
        }
    }

    /// <summary>
    /// The quality of the item a champion gets from a boss, from the share of the top damage it did: the level is the boss's higher one from half of the top
    /// damage on (else the lower one) and the excellent options go from a quarter to all of the tier's.
    /// </summary>
    /// <param name="kind">The boss.</param>
    /// <param name="damage">What the champion did.</param>
    /// <param name="topDamage">What the champion that did the most did.</param>
    /// <param name="tierExcellent">The excellent options the match phase gives.</param>
    /// <returns>The item level and the number of excellent options.</returns>
    public static (int Level, int Excellent) QualityOf(MobaJungle.Kind kind, double damage, double topDamage, int tierExcellent)
    {
        var ratio = topDamage <= 0 ? 1.0 : Math.Clamp(damage / topDamage, 0.0, 1.0);
        var (low, high) = LevelsOf(kind);
        var level = ratio >= 0.5 ? high : low;
        var excellent = Math.Max(1, (int)Math.Round(tierExcellent * (0.25 + (0.75 * ratio))));
        return (level, Math.Min(excellent, tierExcellent));
    }

    /// <summary>
    /// Drops one item for every champion of the team of the last hitter that hurt the boss (best item for the most damage), each one reserved for its champion
    /// for the usual pick-up priority time.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="kind">The boss.</param>
    /// <param name="boss">The dead boss.</param>
    /// <param name="death">How it died.</param>
    /// <param name="damage">The damage every champion did to it.</param>
    /// <returns>A task.</returns>
    public static async ValueTask DropBossItemsAsync(GameMap map, MobaJungle.Kind kind, Monster boss, DeathInformation death, IReadOnlyDictionary<Player, double> damage)
    {
        if (MobaSummons.CreditedChampion(map.GetObject(death.KillerId)) is not { IsMobaClone: true } killer)
        {
            return;
        }

        var team = MobaTeams.GetTeam(killer);
        var maxHealth = Math.Max(1.0, boss.Attributes[GameLogic.Attributes.Stats.MaximumHealth]);
        var winners = damage
            .Where(d => d.Value >= maxHealth * MinDamageShare && (ReferenceEquals(d.Key, killer) || (team != MobaTeam.None && MobaTeams.GetTeam(d.Key) == team)))
            .OrderByDescending(d => d.Value)
            .ToList();
        if (!winners.Any(w => ReferenceEquals(w.Key, killer)))
        {
            winners.Add(new KeyValuePair<Player, double>(killer, Math.Max(1.0, damage.TryGetValue(killer, out var own) ? own : 1.0)));
        }

        var top = winners.Max(w => w.Value);
        var (_, _, _, tierExcellent) = MobaShop.TierStatsOf(MobaArena.Of(killer).Phase);
        var dropped = new List<string>();
        foreach (var (champion, dealt) in winners)
        {
            var (level, excellent) = QualityOf(kind, dealt, top, tierExcellent);
            var item = await MobaCreepDrops.CreateItemAsync(champion, level, excellent).ConfigureAwait(false);
            if (item is null)
            {
                continue;
            }

            var coordinates = map.Terrain.GetRandomCoordinate(boss.Position, 3);
            await map.AddAsync(new DroppedItem(item, coordinates, map, null, new object[] { champion })).ConfigureAwait(false);
            dropped.Add($"{champion.Name} +{level} x{excellent}exc ({dealt / maxHealth:P0})");
        }

        killer.Logger.LogInformation("[MOBA-JUNGLE] {Kind} drops {Count} item(s) for the team of {Killer}: {List}.", kind, dropped.Count, killer.Name, string.Join(", ", dropped));
    }
}

/// <summary>Drop generator of a jungle monster: nothing, Zen or an item of the levels of its kind.</summary>
public sealed class MobaJungleDropGenerator : IDropGenerator
{
    private const int BaseMoneyDrop = 7;

    private readonly MobaJungle.Kind _kind;

    /// <summary>Initializes a new instance of the <see cref="MobaJungleDropGenerator"/> class.</summary>
    /// <param name="kind">The kind of monster.</param>
    public MobaJungleDropGenerator(MobaJungle.Kind kind) => this._kind = kind;

    /// <inheritdoc />
    public async ValueTask<(IEnumerable<Item> Items, uint? Money)> GenerateItemDropsAsync(MonsterDefinition monster, int gainedExperience, Player player)
    {
        var itemChance = MobaJungleDrops.ItemChanceOf(this._kind);
        var roll = Rand.NextDouble();
        if (roll < itemChance)
        {
            var (low, high) = MobaJungleDrops.LevelsOf(this._kind);
            var level = low == 6 && high == 9
                ? MobaCreepDrops.RollCreepLevel()
                : Rand.NextInt(low, high + 1);
            MobaDropReport.NoteRoll(player, monster.Designation, "item", 0);
            var item = await MobaCreepDrops.CreateItemAsync(player, level).ConfigureAwait(false);
            return (item is null ? Enumerable.Empty<Item>() : new[] { item }, null);
        }

        if (roll < itemChance + 0.30)
        {
            var zen = (uint)Math.Max(1, gainedExperience + BaseMoneyDrop);
            MobaDropReport.NoteRoll(player, monster.Designation, "zen", zen);
            return (Enumerable.Empty<Item>(), zen);
        }

        MobaDropReport.NoteRoll(player, monster.Designation, "nothing", 0);
        return (Enumerable.Empty<Item>(), null);
    }

    /// <inheritdoc />
    public Item? GenerateItemDrop(DropItemGroup group) => null;

    /// <inheritdoc />
    public (Item? Item, uint? Money, ItemDropEffect DropEffect) GenerateItemDrop(IEnumerable<DropItemGroup> groups) => (null, null, default);
}
