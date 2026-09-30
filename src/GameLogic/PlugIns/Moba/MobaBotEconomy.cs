// <copyright file="MobaBotEconomy.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.ItemConsumeActions;
using MUnique.OpenMU.GameLogic.PlayerActions.Items;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// What a MOBA bot does with its gold and with what lies on the ground, the same things a human champion does:
/// it buys from the team's shop (upgrading one tier at a time across every equipment slot, reselling the
/// replaced piece), keeps a pack of healing and mana potions and drinks them, picks up dropped gear it can
/// use, and rides the teleport scroll to the front line.
/// </summary>
internal static class MobaBotEconomy
{
    /// <summary>Distance (tiles) to the team's vendor within which a bot can shop.</summary>
    public const int ShopRadius = 14;

    /// <summary>Distance (tiles) within which a bot walks to a dropped item.</summary>
    public const int LootRadius = 12;

    /// <summary>A front-line minion must be at least this many tiles away for a teleport to be worth the channel.</summary>
    public const int TeleportMinDistance = 45;

    private const int ResalePercent = 70;
    private const double ManaPotionBelow = 0.30;
    private const double HealthPotionBelow = 0.40;
    private static readonly TimeSpan PotionCooldown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShopInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TeleportChannel = TimeSpan.FromSeconds(MobaRecall.ChannelSeconds + 0.4);

    /// <summary>Equipment slots in buying priority: weapon, armor, pants, helm, gloves, boots, shield / book, wings, pendant, rings.</summary>
    private static readonly byte[] SlotPriority = { 0, 3, 4, 2, 5, 6, 1, 7, 9, 10, 11 };

    private static readonly MobaShopCategory[] GearCategories =
    {
        MobaShopCategory.Weapons, MobaShopCategory.Offhand, MobaShopCategory.Sets, MobaShopCategory.Wings, MobaShopCategory.Accessories,
    };

    private static readonly ConcurrentDictionary<byte, IReadOnlyList<Candidate>> CandidatesByClass = new();
    private static readonly ConditionalWeakTable<Player, BotState> States = new();

    /// <summary>Gets whether the bot should leave the lane and go shopping: it can afford the next upgrade.</summary>
    /// <param name="bot">The bot.</param>
    /// <returns><see langword="true"/> to recall to the shop.</returns>
    public static bool WantsShopTrip(Player bot)
    {
        var state = States.GetOrCreateValue(bot);
        return DateTime.UtcNow >= state.NextShopUtc && PlanNextPurchase(bot) is not null;
    }

    /// <summary>Gets whether the bot stands close enough to its team's vendor to shop.</summary>
    /// <param name="bot">The bot.</param>
    /// <returns><see langword="true"/> if at the shop.</returns>
    public static bool IsAtShop(Player bot)
        => MobaShop.VendorPositionOf(MobaTeams.GetTeam(bot)) is { } vendor && bot.Position.EuclideanDistanceTo(vendor) <= ShopRadius;

    /// <summary>Shops while the bot stands at its vendor: potions first when it has none, then gear upgrades.</summary>
    /// <param name="bot">The bot.</param>
    /// <returns>A task.</returns>
    public static async ValueTask ShopAsync(Player bot)
    {
        var state = States.GetOrCreateValue(bot);
        if (DateTime.UtcNow < state.NextShopUtc || !IsAtShop(bot))
        {
            return;
        }

        state.NextShopUtc = DateTime.UtcNow + ShopInterval;
        for (var i = 0; i < 12 && PlanNextPurchase(bot) is { } plan; i++)
        {
            if (!await BuyAsync(bot, plan).ConfigureAwait(false))
            {
                break;
            }
        }
    }

    /// <summary>Drinks a mana or health potion when the bot is running low.</summary>
    /// <param name="bot">The bot.</param>
    /// <param name="inCombat">Whether the bot is fighting.</param>
    /// <returns>A task.</returns>
    public static async ValueTask DrinkPotionsAsync(Player bot, bool inCombat)
    {
        var state = States.GetOrCreateValue(bot);
        if (bot.Attributes is not { } attributes || DateTime.UtcNow < state.NextPotionUtc)
        {
            return;
        }

        var mana = attributes[Stats.CurrentMana] / Math.Max(1f, attributes[Stats.MaximumMana]);
        var health = attributes[Stats.CurrentHealth] / Math.Max(1f, attributes[Stats.MaximumHealth]);
        Item? potion = null;
        if (mana < ManaPotionBelow)
        {
            potion = FindPotion(bot, 4, 5, 6);
        }

        if (potion is null && inCombat && health < HealthPotionBelow)
        {
            potion = FindPotion(bot, 1, 2, 3);
        }

        if (potion is null)
        {
            return;
        }

        state.NextPotionUtc = DateTime.UtcNow + PotionCooldown;
        var slot = (byte)potion.ItemSlot;
        await new ItemConsumeAction().HandleConsumeRequestAsync(bot, slot, slot, FruitUsage.Undefined).ConfigureAwait(false);
    }

    /// <summary>Finds a dropped piece of gear on the ground near the bot that is better than what it wears.</summary>
    /// <param name="bot">The bot.</param>
    /// <returns>The dropped item, or <see langword="null"/>.</returns>
    public static DroppedItem? FindWantedDrop(Player bot)
    {
        if (bot.CurrentMap is not { } map || bot.SelectedCharacter?.CharacterClass is not { } characterClass)
        {
            return null;
        }

        return map.GetDropsInRange(bot.Position, LootRadius)
            .OfType<DroppedItem>()
            .Where(d => IsUpgrade(bot, d.Item, characterClass) && (d.IsPlayerAnOwner(bot) || !d.IsOwnerPickupPriorityActive))
            .OrderBy(d => d.GetDistanceTo(bot))
            .FirstOrDefault();
    }

    /// <summary>Picks up a dropped item next to the bot and wears it if it is better than the equipped piece.</summary>
    /// <param name="bot">The bot.</param>
    /// <param name="drop">The dropped item.</param>
    /// <returns>A task.</returns>
    public static async ValueTask PickUpAndEquipAsync(Player bot, DroppedItem drop)
    {
        var definition = drop.Item.Definition;
        await new PickupItemAction().PickupItemAsync(bot, drop.Id).ConfigureAwait(false);
        if (bot.Inventory is not { } inventory || definition is null)
        {
            return;
        }

        var owned = inventory.Items.FirstOrDefault(i => i.Definition == definition && i.ItemSlot >= InventoryConstants.EquippableSlotsCount);
        if (owned is null)
        {
            return;
        }

        if (BestSlotFor(bot, owned.Definition!) is not { } slot)
        {
            return;
        }

        var current = inventory.GetItem(slot);
        await inventory.RemoveItemAsync(owned).ConfigureAwait(false);
        if (current is not null)
        {
            await SellAsync(bot, current).ConfigureAwait(false);
        }

        await inventory.AddItemAsync(slot, owned).ConfigureAwait(false);
        bot.Logger.LogInformation("[MOBA-BOT-ECON] \"{Name}\" looted and equipped {Item} (slot {Slot}).", bot.SelectedCharacter?.Name, owned.Definition?.Name, slot);
    }

    /// <summary>
    /// Uses the teleport scroll to jump to the most advanced allied minion, when the bot is far behind the front line.
    /// </summary>
    /// <param name="bot">The bot.</param>
    /// <returns>How long the bot has to stand still for the channel, or <see cref="TimeSpan.Zero"/> if nothing started.</returns>
    public static async ValueTask<TimeSpan> TryTeleportAsync(Player bot)
    {
        var state = States.GetOrCreateValue(bot);
        if (DateTime.UtcNow < state.NextTeleportTryUtc || bot.CurrentMap is not { } map || bot.Inventory is not { } inventory)
        {
            return TimeSpan.Zero;
        }

        state.NextTeleportTryUtc = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        var scroll = inventory.Items.FirstOrDefault(i => i.Definition is { Group: MobaTeleport.ScrollGroup } d && d.Number == MobaTeleport.ScrollNumber);
        if (scroll is null || MobaRecall.IsChanneling(bot))
        {
            return TimeSpan.Zero;
        }

        var team = MobaTeams.GetTeam(bot);
        var front = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<NPC.Monster>()
            .Where(m => m.IsAlive && !MobaStructures.IsStructure(m) && MobaTeams.AreAllies(bot, m))
            .OrderBy(m => team == MobaTeam.Blue ? -m.Position.Y : m.Position.Y)
            .FirstOrDefault();
        if (front is null || front.GetDistanceTo(bot) < TeleportMinDistance)
        {
            return TimeSpan.Zero;
        }

        await MobaTeleport.BeginTargetingAsync(bot, scroll).ConfigureAwait(false);
        await MobaTeleport.SelectTargetAsync(bot, front.Id).ConfigureAwait(false);
        bot.Logger.LogInformation("[MOBA-BOT-TP] \"{Name}\" uses the teleport scroll toward minion {Id} @ {Pos}.", bot.SelectedCharacter?.Name, front.Id, front.Position);
        return MobaRecall.IsChanneling(bot) ? TeleportChannel : TimeSpan.Zero;
    }

    private static Item? FindPotion(Player bot, params short[] numbers)
        => bot.Inventory?.Items
            .Where(i => i.Definition is { Group: 14 } d && numbers.Contains((short)d.Number) && i.Durability > 0)
            .OrderByDescending(i => i.Definition!.Number)
            .FirstOrDefault();

    /// <summary>The next thing the bot would buy and can afford, or <see langword="null"/>.</summary>
    private static Purchase? PlanNextPurchase(Player bot)
    {
        if (bot.SelectedCharacter?.CharacterClass is not { } characterClass || bot.Inventory is not { } inventory)
        {
            return null;
        }

        var gold = bot.Money;
        var candidates = CandidatesFor(bot, characterClass);
        var ranks = SlotRanks(bot, candidates);
        var weaponRank = ranks.GetValueOrDefault((byte)0);

        if (weaponRank >= 1 && PotionPurchase(bot, gold) is { } potions)
        {
            return potions;
        }

        if (ranks.Count == 0)
        {
            return null;
        }

        var lowest = ranks.Values.Min();
        foreach (var slot in SlotPriority)
        {
            if (!ranks.TryGetValue(slot, out var rank) || rank != lowest)
            {
                continue;
            }

            var current = inventory.GetItem(slot);
            var resale = current is null ? 0 : ResaleOf(bot, current);
            var next = candidates
                .Where(c => c.Slots.Contains(slot) && RankOf(candidates, slot, c.Price) == lowest + 1)
                .OrderBy(c => c.Price)
                .FirstOrDefault();
            if (next is not null && gold >= next.Price - resale)
            {
                return new Purchase(next.Template, slot, next.Price, current, resale);
            }
        }

        return null;
    }

    /// <summary>A pack of the best affordable potions the bot is missing (healing, then mana).</summary>
    private static Purchase? PotionPurchase(Player bot, long gold)
    {
        var tier = MobaMatchPhase.Current;
        foreach (var numbers in new[] { new short[] { 1, 2, 3 }, new short[] { 4, 5, 6 } })
        {
            if (FindPotion(bot, numbers) is not null)
            {
                continue;
            }

            var number = numbers[Math.Min((int)tier, numbers.Length - 1)];
            var definition = bot.GameContext.Configuration.Items.FirstOrDefault(d => d.Group == 14 && d.Number == number);
            var entry = new MobaShopEntry(MobaShopCategory.Consumables, null, 14, number, tier, MobaShopCatalog.PotionLevelOf(tier), MobaShopCatalog.PotionPackSize);
            if (definition is null)
            {
                continue;
            }

            var template = MobaShop.CreateItem(definition, entry);
            var price = MobaShop.PriceOf(template);
            if (gold >= price)
            {
                return new Purchase(template, byte.MaxValue, price, null, 0);
            }
        }

        return null;
    }

    private static async ValueTask<bool> BuyAsync(Player bot, Purchase plan)
    {
        if (bot.Inventory is not { } inventory)
        {
            return false;
        }

        var cost = plan.Price - plan.Resale;
        if (bot.Money < cost)
        {
            return false;
        }

        if (plan.Slot == byte.MaxValue)
        {
            var bagSlot = inventory.CheckInvSpace(plan.Template);
            if (bagSlot is null || !bot.TryRemoveMoney((int)plan.Price))
            {
                return false;
            }

            await inventory.AddItemAsync((byte)bagSlot, plan.Template).ConfigureAwait(false);
            bot.Logger.LogInformation("[MOBA-BOT-ECON] \"{Name}\" bought {Item} x{Qty} for {Price}.", bot.SelectedCharacter?.Name, plan.Template.Definition?.Name, plan.Template.Durability, plan.Price);
            return true;
        }

        if (plan.Current is not null)
        {
            await SellAsync(bot, plan.Current).ConfigureAwait(false);
        }

        if (!bot.TryRemoveMoney((int)plan.Price))
        {
            return false;
        }

        await inventory.AddItemAsync(plan.Slot, plan.Template).ConfigureAwait(false);
        bot.Logger.LogInformation("[MOBA-BOT-ECON] \"{Name}\" bought {Item} for {Price} into slot {Slot} (resold {Old} for {Resale}); gold left {Gold}.", bot.SelectedCharacter?.Name, plan.Template.Definition?.Name, plan.Price, plan.Slot, plan.Current?.Definition?.Name ?? "-", plan.Resale, bot.Money);
        return true;
    }

    private static async ValueTask SellAsync(Player bot, Item item)
    {
        var resale = ResaleOf(bot, item);
        bot.TryAddMoney(resale);
        if (bot.Inventory is { } inventory)
        {
            await inventory.RemoveItemAsync(item).ConfigureAwait(false);
        }

        await bot.PersistenceContext.DeleteAsync(item).ConfigureAwait(false);
    }

    private static int ResaleOf(Player bot, Item item)
        => MobaShop.TryGetSellPriceFor(item, out var price) ? price : (int)(MobaShop.PriceOf(item) * ResalePercent / 100);

    /// <summary>The "tier rank" (0 = nothing / starter, 1..3 = shop tier) of what the bot wears in each slot it can buy for.</summary>
    private static Dictionary<byte, int> SlotRanks(Player bot, IReadOnlyList<Candidate> candidates)
    {
        var ranks = new Dictionary<byte, int>();
        foreach (var slot in candidates.SelectMany(c => c.Slots).Distinct())
        {
            var current = bot.Inventory?.GetItem(slot);
            var price = current is null ? 0 : MobaShop.PriceOf(current);
            ranks[slot] = candidates.Where(c => c.Slots.Contains(slot)).Select(c => c.Price).Distinct().Count(p => p <= price);
        }

        // A pair of rings (or any multi-slot piece) is filled slot by slot, so every listed slot competes on its own rank.
        return ranks.Where(r => HasNextTier(candidates, r.Key, r.Value)).ToDictionary(r => r.Key, r => r.Value);
    }

    private static bool HasNextTier(IReadOnlyList<Candidate> candidates, byte slot, int rank)
        => candidates.Any(c => c.Slots.Contains(slot) && RankOf(candidates, slot, c.Price) == rank + 1);

    private static int RankOf(IReadOnlyList<Candidate> candidates, byte slot, long price)
        => candidates.Where(c => c.Slots.Contains(slot)).Select(c => c.Price).Distinct().Count(p => p <= price);

    private static bool IsUpgrade(Player bot, Item item, CharacterClass characterClass)
    {
        if (item.Definition is not { } definition || !item.IsWearable() || definition.QualifiedCharacters.Count > 0 && !definition.QualifiedCharacters.Contains(characterClass))
        {
            return false;
        }

        if (BestSlotFor(bot, definition) is not { } slot)
        {
            return false;
        }

        var current = bot.Inventory?.GetItem(slot);
        return current is null || MobaShop.PriceOf(item) > MobaShop.PriceOf(current);
    }

    /// <summary>The equipment slot a definition would go to: the listed slot holding the cheapest piece.</summary>
    private static byte? BestSlotFor(Player bot, ItemDefinition definition)
    {
        var slots = definition.ItemSlot?.ItemSlots;
        if (slots is null || slots.Count == 0)
        {
            return null;
        }

        return slots.Select(s => (byte)s).OrderBy(s => bot.Inventory?.GetItem(s) is { } worn ? MobaShop.PriceOf(worn) : 0).First();
    }

    private static IReadOnlyList<Candidate> CandidatesFor(Player bot, CharacterClass characterClass)
        => CandidatesByClass.GetOrAdd(characterClass.Number, _ =>
        {
            var list = new List<Candidate>();
            foreach (var category in GearCategories)
            {
                foreach (var template in MobaShop.BuildCategoryItems(bot.GameContext.Configuration, characterClass, category, out int overflow))
                {
                    if (template.Definition?.ItemSlot?.ItemSlots is { Count: > 0 } slots)
                    {
                        list.Add(new Candidate(template, slots.Select(s => (byte)s).ToArray(), MobaShop.PriceOf(template)));
                    }
                }
            }

            return list;
        });

    private sealed record Candidate(Item Template, byte[] Slots, long Price);

    private sealed record Purchase(Item Template, byte Slot, long Price, Item? Current, int Resale);

    private sealed class BotState
    {
        public DateTime NextShopUtc;

        public DateTime NextPotionUtc;

        public DateTime NextTeleportTryUtc;
    }
}
