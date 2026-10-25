// <copyright file="MobaGoldenLoot.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views.Inventory;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The loot of the golden monsters (2026-10-25): five items of the game used with the right click in the inventory (the message that comes with the drop says
/// what each one does). The <b>golden chest</b> (Box of Kundun +5) gives a random reward, the <b>golden stone</b> (Higher Refine Stone) adds one level to the worst
/// equipped item (up to +12), the <b>reviving elixir</b> (Cherry Blossom Wine) is used by itself the moment its owner dies (the respawn is instant), the
/// <b>golden ward</b> (Life Stone) places a free vision ward and the <b>golden branch</b> (Golden Cherry Blossom Branch) ends every cooldown of the champion.
/// </summary>
public static class MobaGoldenLoot
{
    /// <summary>The highest level the golden jewel brings an item to.</summary>
    public const int MaxJewelLevel = 12;

    /// <summary>The kinds of loot.</summary>
    public enum Kind
    {
        /// <summary>Random reward.</summary>
        Chest,

        /// <summary>+1 to an equipped item.</summary>
        Jewel,

        /// <summary>Instant respawn.</summary>
        Elixir,

        /// <summary>A free ward.</summary>
        Ward,

        /// <summary>Ends the cooldowns.</summary>
        Clock,
    }

    /// <summary>The item (group, number and level) which represents a kind of loot.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The item.</returns>
    public static (byte Group, short Number, byte Level) ItemOf(Kind kind) => kind switch
    {
        Kind.Chest => (14, 11, 12),   // Box of Kundun +5
        Kind.Jewel => (14, 44, 0),    // Higher Refine Stone
        Kind.Elixir => (14, 85, 0),   // Cherry Blossom Wine
        Kind.Ward => (13, 11, 0),     // Life Stone
        _ => (14, 90, 0),             // Golden Cherry Blossom Branch
    };

    /// <summary>The name the players know a kind of loot by.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The name.</returns>
    public static string LabelOf(Kind kind) => kind switch
    {
        Kind.Chest => "Cofre dorado",
        Kind.Jewel => "Piedra dorada",
        Kind.Elixir => "Elixir de reaparición",
        Kind.Ward => "Ward dorado",
        _ => "Rama dorada",
    };

    /// <summary>What the loot does, for the message of the drop.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The text.</returns>
    public static string HintOf(Kind kind) => kind switch
    {
        Kind.Chest => "clic derecho: una recompensa al azar",
        Kind.Jewel => $"clic derecho: +1 al peor ítem equipado (hasta +{MaxJewelLevel})",
        Kind.Elixir => "se usa solo al morir: reapareces al instante",
        Kind.Ward => "clic derecho: un ward gratis donde estás",
        _ => "clic derecho: termina todos tus enfriamientos",
    };

    /// <summary>Gets the kind of loot an item is, if it is one.</summary>
    /// <param name="item">The item.</param>
    /// <returns>The kind, or <see langword="null"/>.</returns>
    public static Kind? KindOf(Item? item)
    {
        if (item?.Definition is not { } definition)
        {
            return null;
        }

        foreach (var kind in Enum.GetValues<Kind>())
        {
            var (group, number, _) = ItemOf(kind);
            if (definition.Group == group && definition.Number == number)
            {
                return kind;
            }
        }

        return null;
    }

    /// <summary>The chance of each kind of loot for a kill of a tier (the Great Golden Dragon is handled apart: it gives to everybody who hurt it).</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The kinds and their chances.</returns>
    public static IReadOnlyList<(Kind Kind, double Chance)> ChancesOf(MobaGolden.Tier tier) => tier switch
    {
        MobaGolden.Tier.Small => new[] { (Kind.Chest, 0.03) },
        MobaGolden.Tier.Medium => new[] { (Kind.Chest, 0.08), (Kind.Jewel, 0.05), (Kind.Ward, 0.08) },
        MobaGolden.Tier.Large => new[] { (Kind.Chest, 0.25), (Kind.Jewel, 0.15), (Kind.Clock, 0.10), (Kind.Elixir, 0.10), (Kind.Ward, 0.10) },
        _ => new[] { (Kind.Chest, 1.0), (Kind.Jewel, 0.5), (Kind.Elixir, 0.35), (Kind.Clock, 0.25) },
    };

    /// <summary>Drops the loot of a golden monster that just died: to the last hitter, and for a Great Golden Dragon to every champion of its team that hurt it.</summary>
    /// <param name="map">The map.</param>
    /// <param name="tier">The tier of the monster.</param>
    /// <param name="monster">The dead monster.</param>
    /// <param name="killer">The champion that last hit it.</param>
    /// <param name="damage">The damage every champion did to it.</param>
    /// <returns>A task.</returns>
    public static async ValueTask DropForKillAsync(GameMap map, MobaGolden.Tier tier, Monster monster, Player killer, IReadOnlyDictionary<Player, double> damage)
    {
        var team = MobaTeams.GetTeam(killer);
        var receivers = tier == MobaGolden.Tier.Great
            ? damage.Where(d => MobaTeams.GetTeam(d.Key) == team && d.Value > 0).Select(d => d.Key).Append(killer).Distinct().ToList()
            : new List<Player> { killer };
        foreach (var receiver in receivers)
        {
            foreach (var (kind, chance) in ChancesOf(tier))
            {
                if (Random.Shared.NextDouble() < chance)
                {
                    await DropAsync(map, kind, monster.Position, receiver).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Drops a loot item at a point, reserved for a champion.</summary>
    /// <param name="map">The map.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="at">Where.</param>
    /// <param name="champion">Who may pick it up first.</param>
    /// <returns>A task.</returns>
    public static async ValueTask DropAsync(GameMap map, Kind kind, Point at, Player champion)
    {
        var (group, number, level) = ItemOf(kind);
        var definition = champion.GameContext.Configuration.Items.FirstOrDefault(d => d.Group == group && d.Number == number);
        if (definition is null)
        {
            return;
        }

        var item = new TemporaryItem { Definition = definition, Durability = 1, Level = (byte)Math.Min(level, definition.MaximumItemLevel) };
        var coordinates = map.Terrain.GetRandomCoordinate(at, 3);
        await map.AddAsync(new DroppedItem(item, coordinates, map, null, new object[] { champion })).ConfigureAwait(false);
        await champion.ShowBlueMessageAsync($"[MOBA] Te cayó: {LabelOf(kind)} ({definition.Name}). {HintOf(kind)}.").ConfigureAwait(false);
    }

    /// <summary>The right click of a loot item in the inventory.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="slot">The inventory slot.</param>
    /// <returns><see langword="true"/> when the item was loot (used or not).</returns>
    public static async ValueTask<bool> TryUseAsync(Player player, byte slot)
    {
        if (!player.IsMobaClone || player.Inventory?.GetItem(slot) is not { } item || KindOf(item) is not { } kind)
        {
            return false;
        }

        if (!player.IsAlive)
        {
            await player.ShowBlueMessageAsync("[MOBA] Estás muerto.").ConfigureAwait(false);
            return true;
        }

        switch (kind)
        {
            case Kind.Elixir:
                await player.ShowBlueMessageAsync("[MOBA] El elixir de reaparición se usa solo cuando mueres.").ConfigureAwait(false);
                return true;
            case Kind.Ward:
                var wardMessage = MobaVision.TryPlaceWard(player, free: true);
                await player.ShowBlueMessageAsync(wardMessage).ConfigureAwait(false);
                if (wardMessage.Contains("placed", StringComparison.OrdinalIgnoreCase))
                {
                    await ConsumeAsync(player, item).ConfigureAwait(false);
                }

                return true;
            case Kind.Clock:
                await EndCooldownsAsync(player).ConfigureAwait(false);
                await ConsumeAsync(player, item).ConfigureAwait(false);
                await player.ShowBlueMessageAsync("[MOBA] ¡Rama dorada! Todos tus enfriamientos terminaron.").ConfigureAwait(false);
                return true;
            case Kind.Jewel:
                if (await UpgradeWorstItemAsync(player).ConfigureAwait(false))
                {
                    await ConsumeAsync(player, item).ConfigureAwait(false);
                }

                return true;
            default:
                await ConsumeAsync(player, item).ConfigureAwait(false);
                await OpenChestAsync(player).ConfigureAwait(false);
                return true;
        }
    }

    /// <summary>When a champion dies carrying a reviving elixir, it is used and the champion respawns at once.</summary>
    /// <param name="player">The champion that died.</param>
    /// <returns><see langword="true"/> if an elixir was used.</returns>
    public static bool TryConsumeElixirOnDeath(Player player)
    {
        if (!player.IsMobaClone || player.Inventory is not { } inventory)
        {
            return false;
        }

        var elixir = inventory.Items.FirstOrDefault(i => KindOf(i) == Kind.Elixir);
        if (elixir is null)
        {
            return false;
        }

        _ = ConsumeThenTellAsync(player, elixir);
        return true;
    }

    private static async Task ConsumeThenTellAsync(Player player, Item elixir)
    {
        try
        {
            await ConsumeAsync(player, elixir).ConfigureAwait(false);
            await player.ShowBlueMessageAsync("[MOBA] ¡Elixir de reaparición! Vuelves al instante.").ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    private static async ValueTask ConsumeAsync(Player player, Item item)
    {
        if (item.Durability > 1)
        {
            item.Durability--;
            await player.InvokeViewPlugInAsync<IItemDurabilityChangedPlugIn>(p => p.ItemDurabilityChangedAsync(item, true)).ConfigureAwait(false);
        }
        else
        {
            await player.DestroyInventoryItemAsync(item).ConfigureAwait(false);
        }
    }

    private static async ValueTask<bool> UpgradeWorstItemAsync(Player player)
    {
        if (player.Inventory is not { } inventory || player.Attributes is not { } attributes)
        {
            return false;
        }

        var target = inventory.EquippedItems
            .Where(i => i.Definition is { Group: <= 11 } && i.Level < MaxJewelLevel && i.Level < (i.Definition?.MaximumItemLevel ?? 0))
            .OrderBy(i => i.Level)
            .FirstOrDefault();
        if (target is null)
        {
            await player.ShowBlueMessageAsync($"[MOBA] Ningún ítem equipado puede subir más (tope +{MaxJewelLevel}).").ConfigureAwait(false);
            return false;
        }

        target.Level++;
        if (attributes.ItemPowerUps.Remove(target, out var old))
        {
            foreach (var powerUp in old)
            {
                powerUp.Dispose();
            }
        }

        attributes.ItemPowerUps.Add(target, player.GameContext.ItemPowerUpFactory.GetPowerUps(target, attributes).ToList());
        await player.InvokeViewPlugInAsync<IItemUpgradedPlugIn>(p => p.ItemUpgradedAsync(target)).ConfigureAwait(false);
        player.OnAppearanceChanged();
        await player.ShowBlueMessageAsync($"[MOBA] ¡Piedra dorada! {target.Definition?.Name} sube a +{target.Level}.").ConfigureAwait(false);
        return true;
    }

    private static async ValueTask EndCooldownsAsync(Player player)
    {
        var now = DateTime.UtcNow;
        foreach (var skill in player.MobaSkillCooldowns.Keys.ToList())
        {
            player.MobaSkillCooldowns[skill] = now;
            if (player.MobaSkillGraceEnds.ContainsKey(skill))
            {
                player.MobaSkillGraceEnds[skill] = now;
            }

            var number = skill;
            await player.InvokeViewPlugInAsync<IMobaSkillCooldownPlugIn>(p => p.ShowSkillCooldownAsync(number, 0, 0)).ConfigureAwait(false);
        }
    }

    private static async ValueTask OpenChestAsync(Player player)
    {
        if (player.CurrentMap is not { } map)
        {
            return;
        }

        var roll = Random.Shared.NextDouble();
        if (roll < 0.35)
        {
            var gold = 600 + (40 * player.MobaLevel);
            await MobaGold.GrantAsync(player, gold, "golden-chest").ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[MOBA] Cofre dorado: {gold} de oro.").ConfigureAwait(false);
        }
        else if (roll < 0.60)
        {
            var item = await MobaCreepDrops.CreateItemAsync(player, Random.Shared.Next(10, 12), 2).ConfigureAwait(false);
            if (item is not null)
            {
                await map.AddAsync(new DroppedItem(item, map.Terrain.GetRandomCoordinate(player.Position, 2), map, null, new object[] { player })).ConfigureAwait(false);
                await player.ShowBlueMessageAsync("[MOBA] Cofre dorado: ¡un ítem! Está en el suelo, cerca de ti.").ConfigureAwait(false);
            }
        }
        else if (roll < 0.80)
        {
            MobaGolden.Bless(player, TimeSpan.FromSeconds(90), MobaGolden.TokenOf(map));
            await player.ShowBlueMessageAsync("[MOBA] Cofre dorado: bendición dorada durante 90 s (+12 % de daño, -8 % de daño recibido).").ConfigureAwait(false);
        }
        else
        {
            var kinds = new[] { Kind.Jewel, Kind.Clock, Kind.Ward };
            await DropAsync(map, kinds[Random.Shared.Next(kinds.Length)], player.Position, player).ConfigureAwait(false);
        }
    }
}
