// <copyright file="MobaShop.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.GameLogic.Views.NPC;

/// <summary>
/// The MOBA item shop: one vendor NPC (Hanzo) per team base. Talking to it opens a text
/// menu of categories in the NPC dialogue window; picking one opens the standard merchant
/// window with only the items of that category the champion's class can use. Prices are
/// computed by <see cref="PriceOf"/> and sent to the client, which shows them in the
/// tooltips instead of its native price. The currency is the clone's Zen, so everything
/// bought here is discarded with the clone when the match ends.
/// </summary>
public static class MobaShop
{
    /// <summary>The NPC number of the vendor (Hanzo the Blacksmith).</summary>
    public const short VendorNumber = 251;

    /// <summary>The percentage of the price paid back when selling an item to the vendor.</summary>
    public const byte SellPercent = 70;

    /// <summary>The identifier of the category menu, echoed back by the client.</summary>
    public const byte CategoryMenuId = 1;

    private const string VendorTitle = "Hanzo el herrero";
    private const string VendorText = "¡Bienvenido, campeón! Todo se paga con el Zen de la partida. ¿Qué necesitas?";

    private const int GridColumns = 8;
    private const int GridRows = 15;

    // Zen per price point: equipment is scored by grade + upgrades, consumables are cheap.
    private const int PricePerPoint = 10;
    private const int ConsumablePricePerPoint = 2;

    private static readonly (byte X, byte Y) BlueVendorPos = (112, 57);
    private static readonly (byte X, byte Y) RedVendorPos = (112, 208);

    private static readonly ConcurrentDictionary<ushort, List<NonPlayerCharacter>> VendorsByMap = new();
    private static readonly ConditionalWeakTable<Player, ShopView> Views = new();

    /// <summary>
    /// Spawns the vendors on the arena map, if they aren't there yet.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public static async ValueTask EnsureVendorsAsync(IGameContext gameContext)
    {
        var map = await gameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false);
        if (map is null
            || gameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == VendorNumber) is not { } definition)
        {
            return;
        }

        var vendors = new List<NonPlayerCharacter>();
        if (!VendorsByMap.TryAdd(map.MapId, vendors))
        {
            return;
        }

        foreach (var position in new[] { BlueVendorPos, RedVendorPos })
        {
            var area = new MonsterSpawnArea
            {
                GameMap = map.Definition,
                MonsterDefinition = definition,
                SpawnTrigger = SpawnTrigger.OnceAtEventStart,
                Quantity = 1,
                X1 = position.X,
                X2 = position.X,
                Y1 = position.Y,
                Y2 = position.Y,
                Direction = Direction.South,
            };

            var npc = new NonPlayerCharacter(area, definition, map);
            npc.Initialize();
            await map.AddAsync(npc).ConfigureAwait(false);
            npc.OnSpawn();
            vendors.Add(npc);
        }
    }

    /// <summary>
    /// Opens the category menu if the player is a MOBA champion talking to a vendor.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="npc">The NPC the player talks to.</param>
    /// <returns><c>true</c>, if the talk was handled by the shop.</returns>
    public static async ValueTask<bool> TryOpenMenuAsync(Player player, NonPlayerCharacter npc)
    {
        if (!player.IsMobaClone || !IsVendor(npc))
        {
            return false;
        }

        Views.Remove(player);
        await player.InvokeViewPlugInAsync<IMobaShopPlugIn>(p => p.ShowMenuAsync(CategoryMenuId, VendorTitle, VendorText, MobaShopCatalog.CategoryNames)).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Handles the option the player picked in the category menu: opens the merchant
    /// window with the items of that category for the player's class.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="menuId">The menu identifier.</param>
    /// <param name="optionIndex">The index of the chosen option.</param>
    public static async ValueTask SelectOptionAsync(Player player, byte menuId, byte optionIndex)
    {
        if (menuId != CategoryMenuId
            || !player.IsMobaClone
            || player.OpenedNpc is not { } npc
            || !IsVendor(npc)
            || optionIndex >= MobaShopCatalog.CategoryNames.Count)
        {
            return;
        }

        var category = (MobaShopCategory)optionIndex;
        var view = BuildView(player, category);
        if (view.Items.Count == 0)
        {
            await player.ShowBlueMessageAsync("[Tienda] No hay ítems de esa categoría para tu clase.").ConfigureAwait(false);
            await TryOpenMenuAsync(player, npc).ConfigureAwait(false);
            return;
        }

        Views.AddOrUpdate(player, view);
        await player.InvokeViewPlugInAsync<IOpenNpcWindowPlugIn>(p => p.OpenNpcWindowAsync(NpcWindow.Merchant)).ConfigureAwait(false);
        await player.InvokeViewPlugInAsync<IShowMerchantStoreItemListPlugIn>(p => p.ShowMerchantStoreItemListAsync(view.Items.Values.ToList(), StoreKind.Normal)).ConfigureAwait(false);
        await SendPricesAsync(player, view).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the item the player wants to buy from the currently shown shop category.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="slot">The slot in the merchant window.</param>
    /// <param name="item">The item template.</param>
    /// <param name="price">The price.</param>
    /// <returns><c>true</c>, if the player buys from the MOBA shop.</returns>
    public static bool TryGetOffer(Player player, byte slot, [NotNullWhen(true)] out Item? item, out long price)
    {
        item = null;
        price = 0;
        if (!player.IsMobaClone
            || player.OpenedNpc is not { } npc
            || !IsVendor(npc)
            || !Views.TryGetValue(player, out var view)
            || !view.Items.TryGetValue(slot, out item))
        {
            return false;
        }

        price = PriceOf(item);
        return true;
    }

    /// <summary>
    /// Gets the price the vendor pays back for an item, if the player sells to the MOBA shop.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="item">The item to sell.</param>
    /// <param name="price">The selling price.</param>
    /// <returns><c>true</c>, if the player sells to the MOBA shop.</returns>
    public static bool TryGetSellPrice(Player player, Item item, out int price)
    {
        price = 0;
        if (!player.IsMobaClone || player.OpenedNpc is not { } npc || !IsVendor(npc))
        {
            return false;
        }

        price = (int)(PriceOf(item) * SellPercent / 100);
        return true;
    }

    /// <summary>
    /// Calculates the shop price of an item: proportional to its grade (drop level, which is
    /// what the base stats of MU items scale with), its level and its options. Stacks are
    /// priced per unit.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>The price in Zen.</returns>
    public static long PriceOf(Item item)
    {
        var unit = UnitPriceOf(item);
        return item.IsStackable() ? unit * (long)Math.Max(1, item.Durability) : unit;
    }

    /// <summary>
    /// Builds the items a class sees in a shop category, laid out on the merchant grid
    /// (<see cref="Item.ItemSlot"/> set), cheapest tier first.
    /// </summary>
    /// <param name="configuration">The game configuration.</param>
    /// <param name="characterClass">The class of the champion.</param>
    /// <param name="category">The category.</param>
    /// <param name="overflow">The number of catalog items which didn't fit on the grid.</param>
    /// <returns>The items of the category.</returns>
    public static IReadOnlyList<Item> BuildCategoryItems(GameConfiguration configuration, CharacterClass characterClass, MobaShopCategory category, out int overflow)
    {
        var family = MobaPassives.FamilyOf(characterClass.Number);
        var candidates = MobaShopCatalog.Entries
            .Where(e => e.Category == category && (e.Families is null || e.Families.Contains(family)))
            .Select(e => (Entry: e, Definition: configuration.Items.FirstOrDefault(d => d.Group == e.Group && d.Number == e.Number)))
            .Where(c => c.Definition is not null && IsUsableBy(c.Definition, characterClass))
            .OrderBy(c => c.Entry.Tier)
            .ThenBy(c => c.Definition!.Group)
            .ThenBy(c => c.Definition!.Number)
            .ToList();

        var items = new List<Item>();
        var occupied = new bool[GridRows, GridColumns];
        overflow = 0;
        foreach (var (entry, definition) in candidates)
        {
            if (TryPlace(occupied, definition!.Width, definition.Height) is not { } slot)
            {
                overflow++;
                continue;
            }

            var item = CreateItem(definition, entry);
            item.ItemSlot = slot;
            items.Add(item);
        }

        return items;
    }

    private static long UnitPriceOf(Item item)
    {
        if (item.Definition is not { } definition)
        {
            return 0;
        }

        var score = (definition.DropLevel + 10) * (1 + (item.Level * 0.1));
        score += OptionLevelOf(item) * 8;
        score += HasLuck(item) ? 15 : 0;
        score += ExcellentCountOf(item) * 25;

        var perPoint = item.IsStackable() ? ConsumablePricePerPoint : PricePerPoint;
        var price = (long)Math.Round(score * perPoint);
        return item.IsStackable() ? Math.Max(1, price) : Math.Max(10, (price + 5) / 10 * 10);
    }

    // Consumables (potions etc.) have no qualified classes: everyone may use them.
    private static bool IsUsableBy(ItemDefinition definition, CharacterClass characterClass)
        => definition.QualifiedCharacters.Count == 0 || definition.QualifiedCharacters.Contains(characterClass);

    private static bool IsVendor(NonPlayerCharacter npc)
        => npc.CurrentMap is { } map
           && VendorsByMap.TryGetValue(map.MapId, out var vendors)
           && vendors.Contains(npc);

    private static ShopView BuildView(Player player, MobaShopCategory category)
    {
        var view = new ShopView();
        if (player.SelectedCharacter?.CharacterClass is not { } characterClass)
        {
            return view;
        }

        var items = BuildCategoryItems(player.GameContext.Configuration, characterClass, category, out var overflow);
        if (overflow > 0)
        {
            player.Logger.LogWarning("[MOBA-SHOP] {Overflow} item(s) of category {Category} didn't fit on the grid.", overflow, category);
        }

        foreach (var item in items)
        {
            view.Items[item.ItemSlot] = item;
        }

        return view;
    }

    private static byte? TryPlace(bool[,] occupied, int width, int height)
    {
        for (var y = 0; y + height <= GridRows; y++)
        {
            for (var x = 0; x + width <= GridColumns; x++)
            {
                if (IsFree(occupied, x, y, width, height))
                {
                    for (var dy = 0; dy < height; dy++)
                    {
                        for (var dx = 0; dx < width; dx++)
                        {
                            occupied[y + dy, x + dx] = true;
                        }
                    }

                    return (byte)((y * GridColumns) + x);
                }
            }
        }

        return null;
    }

    private static bool IsFree(bool[,] occupied, int x, int y, int width, int height)
    {
        for (var dy = 0; dy < height; dy++)
        {
            for (var dx = 0; dx < width; dx++)
            {
                if (occupied[y + dy, x + dx])
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Pick priority for excellent options, by the attribute they boost (lower = picked
    /// first when a tier doesn't take the whole pool). Weapon options and armor options
    /// never appear on the same item, so both rank tables share one dictionary safely.
    /// Order: raw offense / HP-and-mitigation first, resource-sustain and the Zen bonus
    /// last - so a T1/T2 item reads as "damage/tanky" before it reads as "sustain/utility".
    /// </summary>
    private static readonly Dictionary<AttributeDefinition, int> ExcellentPickPriority = new()
    {
        [Stats.PhysicalBaseDmgIncrease] = 1,
        [Stats.WizardryBaseDmgIncrease] = 1,
        [Stats.MaximumHealth] = 1,
        [Stats.PhysicalBaseDmg] = 2,
        [Stats.WizardryBaseDmg] = 2,
        [Stats.ArmorDamageDecrease] = 2,
        [Stats.ExcellentDamageChance] = 3,
        [Stats.DefenseRatePvm] = 3,
        [Stats.AttackSpeedAny] = 4,
        [Stats.DamageReflection] = 4,
        [Stats.HealthAfterMonsterKillMultiplier] = 5,
        [Stats.MaximumMana] = 5,
        [Stats.ManaAfterMonsterKillMultiplier] = 6,
        [Stats.MoneyAmountRate] = 6,
    };

    private static TemporaryItem CreateItem(ItemDefinition definition, MobaShopEntry entry)
    {
        var item = new TemporaryItem { Definition = definition };

        // Per tier: item level, "Option" (+dmg/+def flat) level 1-4, whether it carries
        // Luck, and how many of the item's 6 possible excellent options it gets - T3 is
        // "full" (all 6), so by then every build converges; T1/T2 pick the top few by
        // ExcellentPickPriority, giving each tier real (if partial) identity.
        var (level, optionLevel, luck, excellent) = entry.Tier switch
        {
            MobaShopTier.T2 => (14, 3, true, 4),
            MobaShopTier.T3 => (15, 4, true, 6),
            _ => (13, 2, true, 3),
        };

        item.Level = (byte)Math.Min(entry.Level ?? level, definition.MaximumItemLevel);
        item.Durability = definition.Durability > 1 && !item.IsWearable()
            ? Math.Clamp((int)entry.Quantity, 1, definition.Durability)
            : definition.Durability;

        var possibleOptions = definition.PossibleItemOptions.SelectMany(o => o.PossibleOptions).ToList();
        if (optionLevel > 0 && possibleOptions.FirstOrDefault(o => o.OptionType == ItemOptionTypes.Option) is { } option)
        {
            item.ItemOptions.Add(new ItemOptionLink { ItemOption = option, Level = Math.Min(optionLevel, option.LevelDependentOptions.Count > 0 ? option.LevelDependentOptions.Max(l => l.Level) : optionLevel) });
        }

        if (luck && possibleOptions.FirstOrDefault(o => o.OptionType == ItemOptionTypes.Luck) is { } luckOption)
        {
            item.ItemOptions.Add(new ItemOptionLink { ItemOption = luckOption });
        }

        var excellentOptions = possibleOptions.Where(o => o.OptionType == ItemOptionTypes.Excellent)
            .OrderBy(o => o.PowerUpDefinition?.TargetAttribute is { } attribute && ExcellentPickPriority.TryGetValue(attribute, out var priority) ? priority : int.MaxValue)
            .ThenBy(o => o.Number)
            .Take(excellent);
        foreach (var excellentOption in excellentOptions)
        {
            item.ItemOptions.Add(new ItemOptionLink { ItemOption = excellentOption });
        }

        item.HasSkill = definition.Skill is not null && entry.Tier != MobaShopTier.T1;
        return item;
    }

    private static async ValueTask SendPricesAsync(Player player, ShopView view)
    {
        var prices = new Dictionary<(ushort, byte, byte, bool, byte), MobaShopPrice>();
        var inventoryItems = player.Inventory?.Items ?? Enumerable.Empty<Item>();
        foreach (var item in view.Items.Values.Concat(inventoryItems))
        {
            if (item.Definition is not { } definition)
            {
                continue;
            }

            var perUnit = item.IsStackable();
            var price = new MobaShopPrice(
                (ushort)((definition.Group * 512) + definition.Number),
                item.Level,
                (byte)OptionLevelOf(item),
                HasLuck(item),
                (byte)ExcellentCountOf(item),
                perUnit,
                (uint)Math.Clamp(perUnit ? UnitPriceOf(item) : PriceOf(item), 0, uint.MaxValue));
            prices[(price.ItemType, price.Level, price.OptionLevel, price.HasLuck, price.ExcellentCount)] = price;
        }

        await player.InvokeViewPlugInAsync<IMobaShopPlugIn>(p => p.ShowPricesAsync(prices.Values, SellPercent)).ConfigureAwait(false);
    }

    private static int OptionLevelOf(Item item)
        => item.ItemOptions.FirstOrDefault(o => o.ItemOption?.OptionType == ItemOptionTypes.Option)?.Level ?? 0;

    private static bool HasLuck(Item item)
        => item.ItemOptions.Any(o => o.ItemOption?.OptionType == ItemOptionTypes.Luck);

    private static int ExcellentCountOf(Item item)
        => item.ItemOptions.Count(o => o.ItemOption?.OptionType == ItemOptionTypes.Excellent);

    private sealed class ShopView
    {
        public Dictionary<byte, Item> Items { get; } = new();
    }
}
