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
    public const int LootRadius = 25;

    /// <summary>Radius in which a bot still grabs a drop while an enemy champion is near or it is in combat.</summary>
    public const int FightLootRadius = 4;

    /// <summary>A front-line minion must be at least this many tiles away for a teleport to be worth the channel.</summary>
    public const int TeleportMinDistance = 45;

    private const int ResalePercent = 70;
    private const int MaxLootAttempts = 3;
    private const double ManaPotionBelow = 0.30;
    private const double HealthPotionBelow = 0.40;

    // In a balance duel the bot drinks like a diligent player: as soon as it misses a bit of life, not only when it is about to die.
    private const double DuelHealthPotionBelow = 0.95;
    private static readonly TimeSpan PotionCooldown = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan ShopInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan TeleportChannel = TimeSpan.FromSeconds(MobaRecall.ChannelSeconds + 0.4);

    /// <summary>Equipment slots in buying priority: weapon, armor, pants, helm, gloves, boots, shield / book, wings, pendant, rings.</summary>
    private static readonly byte[] SlotPriority = { 0, 3, 4, 2, 5, 6, 1, 7, 9, 10, 11 };

    /// <summary>The same priorities for the Elf, whose weapon (the bow) goes to the other hand: the arrows stay in the first one.</summary>
    private static readonly byte[] ElfSlotPriority = { 1, 3, 4, 2, 5, 6, 0, 7, 9, 10, 11 };

    private static readonly Lazy<HashSet<(byte Group, short Number)>> CatalogWeapons = new(() => CatalogItemsOf(MobaShopCategory.Weapons));
    private static readonly Lazy<HashSet<(byte Group, short Number)>> CatalogOffhands = new(() => CatalogItemsOf(MobaShopCategory.Offhand));

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
            if (await BuyAsync(bot, plan).ConfigureAwait(false) is null)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Gives a champion a budget and lets it shop exactly like a bot (same priorities, resale included), as if it had spent that gold during the match. Used by the
    /// balance duels. What is left of the budget is removed.
    /// </summary>
    /// <param name="bot">The champion.</param>
    /// <param name="budget">The gold to spend.</param>
    /// <param name="potionTier">The shop phase, which decides the potions on sale.</param>
    /// <param name="keepPotions">Whether the potions stay in the bag; otherwise they are paid for and thrown away (duels without drinking).</param>
    /// <returns>The gear and pets bought that are still worn, and the gold that could not be spent.</returns>
    public static async ValueTask<(IReadOnlyList<Item> Worn, int Left)> BuyWithBudgetAsync(Player bot, int budget, MobaShopTier potionTier, bool keepPotions)
    {
        if (bot.Money > 0)
        {
            bot.TryRemoveMoney(bot.Money);
        }

        bot.TryAddMoney(budget);
        var before = bot.Inventory?.Items.ToList() ?? new List<Item>();
        for (var i = 0; i < 80 && PlanNextPurchase(bot, potionTier) is { } plan; i++)
        {
            if (await BuyAsync(bot, plan).ConfigureAwait(false) is null)
            {
                break;
            }
        }

        while (!keepPotions && bot.Inventory is { } inventory && FindPotion(bot, 1, 2, 3, 4, 5, 6) is { } potion)
        {
            await inventory.RemoveItemAsync(potion).ConfigureAwait(false);
            await bot.PersistenceContext.DeleteAsync(potion).ConfigureAwait(false);
        }

        var left = bot.Money;
        if (left > 0)
        {
            bot.TryRemoveMoney(left);
        }

        // what is worn now and was not before (equipment slots only; the starting weapon that was not replaced does not count)
        // (plus the Horse and the Raven, which work from the bag)
        var worn = bot.Inventory?.Items
            .Where(i => (i.ItemSlot <= InventoryConstants.LastEquippableItemSlotIndex || (i.Definition is { } d && MobaPets.IsInventoryPet(d))) && !before.Contains(i))
            .ToList() ?? new List<Item>();
        return (worn, left);
    }

    /// <summary>Drinks a mana or health potion when the bot is running low.</summary>
    /// <param name="bot">The bot.</param>
    /// <param name="inCombat">Whether the bot is fighting.</param>
    /// <returns>A task.</returns>
    public static async ValueTask DrinkPotionsAsync(Player bot, bool inCombat)
        => await DrinkAsync(bot, inCombat, HealthPotionBelow).ConfigureAwait(false);

    /// <summary>Drinks during a balance duel (see <see cref="MobaDuel.Drinking"/>).</summary>
    /// <param name="bot">The duelist.</param>
    /// <returns>Whether a health potion, a mana potion or nothing was used.</returns>
    public static ValueTask<PotionDrunk> DrinkInDuelAsync(Player bot) => DrinkAsync(bot, true, DuelHealthPotionBelow);

    private static async ValueTask<PotionDrunk> DrinkAsync(Player bot, bool inCombat, double healthBelow)
    {
        var state = States.GetOrCreateValue(bot);
        if (bot.Attributes is not { } attributes || DateTime.UtcNow < state.NextPotionUtc || DateTime.UtcNow < bot.PotionCooldownUntil)
        {
            return PotionDrunk.None;
        }

        var mana = attributes[Stats.CurrentMana] / Math.Max(1f, attributes[Stats.MaximumMana]);
        var health = attributes[Stats.CurrentHealth] / Math.Max(1f, attributes[Stats.MaximumHealth]);
        Item? potion = null;
        if (mana < ManaPotionBelow)
        {
            potion = FindPotion(bot, 4, 5, 6);
        }

        if (potion is null && inCombat && health < healthBelow)
        {
            potion = FindPotion(bot, 1, 2, 3);
        }

        if (potion is null)
        {
            return PotionDrunk.None;
        }

        state.NextPotionUtc = DateTime.UtcNow + PotionCooldown;
        var kind = potion.Definition!.Number <= 3 ? PotionDrunk.Health : PotionDrunk.Mana;
        var doses = potion.Durability;
        var slot = (byte)potion.ItemSlot;
        await new ItemConsumeAction().HandleConsumeRequestAsync(bot, slot, slot, FruitUsage.Undefined).ConfigureAwait(false);
        return potion.Durability < doses ? kind : PotionDrunk.None;
    }

    /// <summary>What a drinking attempt used.</summary>
    public enum PotionDrunk
    {
        /// <summary>Nothing.</summary>
        None,

        /// <summary>A health potion.</summary>
        Health,

        /// <summary>A mana potion.</summary>
        Mana,
    }

    /// <summary>
    /// Finds something on the ground near the bot worth picking up: dropped gold, or a piece of gear that is
    /// better than what it wears.
    /// </summary>
    /// <param name="bot">The bot.</param>
    /// <returns>The loot, or <see langword="null"/>.</returns>
    public static ILocateable? FindWantedDrop(Player bot, int radius = LootRadius)
    {
        if (bot.CurrentMap is not { } map || bot.SelectedCharacter?.CharacterClass is not { } characterClass)
        {
            return null;
        }

        var attempts = States.GetOrCreateValue(bot).LootAttempts;
        return map.GetDropsInRange(bot.Position, radius)
            .Where(d => attempts.GetValueOrDefault(d) < MaxLootAttempts)
            .Where(d => d is DroppedMoney
                        || d is DroppedItem item && IsUpgrade(bot, item.Item, characterClass) && (item.IsPlayerAnOwner(bot) || !item.IsOwnerPickupPriorityActive))
            .OrderBy(d => d.GetDistanceTo(bot))
            .FirstOrDefault();
    }

    /// <summary>Picks up the loot next to the bot: gold goes into the purse, gear is worn if it is better.</summary>
    /// <param name="bot">The bot.</param>
    /// <param name="loot">The loot found by <see cref="FindWantedDrop"/>.</param>
    /// <returns>A task.</returns>
    public static async ValueTask PickUpAsync(Player bot, ILocateable loot)
    {
        var attempts = States.GetOrCreateValue(bot).LootAttempts;
        attempts[loot] = attempts.GetValueOrDefault(loot) + 1; // gold reserved for someone else is given up after a few tries
        switch (loot)
        {
            case DroppedMoney money:
                await new PickupItemAction().PickupItemAsync(bot, money.Id).ConfigureAwait(false);
                break;
            case DroppedItem item:
                await PickUpAndEquipAsync(bot, item).ConfigureAwait(false);
                break;
        }
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
            bot.Logger.LogInformation("[MOBA-BOT-ECON] \"{Name}\" could not pick up {Item} (reserved for someone else, inventory full or taken).", bot.SelectedCharacter?.Name, definition.Name);
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

    /// <summary>Tiles from its lane a minion may be and still count as being on that lane.</summary>
    private const double TeleportLaneTolerance = 12;

    /// <summary>
    /// Uses the teleport scroll to jump to the most advanced allied minion, when the bot is far behind the front line.
    /// </summary>
    /// <param name="bot">The bot.</param>
    /// <param name="lane">The bot's lane: only minions on that lane are valid targets (-1 = any lane).</param>
    /// <returns>How long the bot has to stand still for the channel, or <see cref="TimeSpan.Zero"/> if nothing started.</returns>
    public static async ValueTask<TimeSpan> TryTeleportAsync(Player bot, int lane = -1)
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
            .Where(m => lane < 0 || MobaLayout.DistanceToPolyline(MobaLayout.Lanes[lane].Waypoints, m.Position.X, m.Position.Y) <= TeleportLaneTolerance)
            .OrderBy(m => lane < 0
                ? (team == MobaTeam.Blue ? -m.Position.Y : m.Position.Y)
                : (team == MobaTeam.Blue ? -1 : 1) * MobaLayout.ArcOf(MobaLayout.Lanes[lane].Waypoints, m.Position.X, m.Position.Y))
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
    private static Purchase? PlanNextPurchase(Player bot, MobaShopTier? potionTier = null)
    {
        if (bot.SelectedCharacter?.CharacterClass is not { } characterClass || bot.Inventory is not { } inventory)
        {
            return null;
        }

        var gold = bot.Money;
        var candidates = CandidatesFor(bot, characterClass)
            .Select(c => c with { Slots = SlotsFor(bot, c.Template.Definition!) })
            .Where(c => c.Slots.Length > 0)
            .ToList();
        var ranks = SlotRanks(bot, candidates);
        var weaponHand = MainHandOf(bot);
        var wielded = inventory.GetItem(weaponHand);
        var weaponRank = RankOf(candidates, weaponHand, wielded is null ? 0 : MobaShop.PriceOf(wielded)); // also when the weapon is already the best one

        if (weaponRank >= 1 && PotionPurchase(bot, gold, potionTier ?? MobaArena.Of(bot).Phase) is { } potions)
        {
            return potions;
        }

        if (ranks.Count == 0)
        {
            return null;
        }

        var lowest = ranks.Values.Min();
        if (weaponRank >= 1 && PetPurchase(bot, gold, lowest) is { } pet)
        {
            return pet;
        }

        foreach (var slot in weaponHand == InventoryConstants.RightHandSlot ? ElfSlotPriority : SlotPriority)
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

    /// <summary>
    /// The pets a bot buys, by shop tier: the damage pets a human would pick (the Dark Lord takes both its mount and its
    /// Raven, every other class one pet slot upgraded tier by tier). A tier-t pet is bought once all the gear is at tier t - 1.
    /// </summary>
    private static IEnumerable<(short Number, MobaShopVariant Variant, int Tier)> WantedPets(Player bot)
        => MobaPassives.FamilyOf(bot) == MobaFamily.DarkLord
            ? new[] { ((short)4, MobaShopVariant.Standard, 2), ((short)5, MobaShopVariant.Standard, 3) }
            : new[] { ((short)1, MobaShopVariant.Standard, 1), ((short)3, MobaShopVariant.Standard, 2), ((short)37, MobaShopVariant.Aggressive, 3) };

    /// <summary>The next pet the bot wants and can afford, or <see langword="null"/>.</summary>
    private static Purchase? PetPurchase(Player bot, long gold, int lowestGearRank)
    {
        if (bot.SelectedCharacter?.CharacterClass is not { } characterClass || bot.Inventory is not { } inventory)
        {
            return null;
        }

        var family = MobaPassives.FamilyOf(bot);
        foreach (var (number, variant, tier) in WantedPets(bot))
        {
            if (lowestGearRank < tier - 1
                || MobaShopCatalog.Entries.FirstOrDefault(e => e.Category == MobaShopCategory.Pets && e.Number == number && e.Variant == variant && (e.Families is null || e.Families.Contains(family))) is not { } entry
                || bot.GameContext.Configuration.Items.FirstOrDefault(d => d.Group == entry.Group && d.Number == entry.Number) is not { } definition
                || definition.QualifiedCharacters.Count > 0 && !definition.QualifiedCharacters.Contains(characterClass)
                || BestSlotFor(bot, definition) is not { } slot)
            {
                continue;
            }

            var template = MobaShop.CreateItem(definition, entry);
            var price = MobaShop.PriceOf(template);
            if (MobaPets.IsInventoryPet(definition))
            {
                if (bot is MobaBotPlayer plan && !plan.Wants(definition))
                {
                    continue; // measurement of the pets (duels): this side spends that gold on gear
                }

                // The Horse and the Raven are not worn: bought into the bag and switched on from there, like the player's right click.
                if (!inventory.Items.Any(i => i.Definition == definition) && gold >= price)
                {
                    return new Purchase(template, byte.MaxValue, price, null, 0);
                }

                continue;
            }

            var current = inventory.GetItem(slot);
            if (current is not null && MobaShop.PriceOf(current) >= price)
            {
                continue; // already owns this pet (or a better one)
            }

            var resale = current is null ? 0 : ResaleOf(bot, current);
            if (gold >= price - resale)
            {
                return new Purchase(template, slot, price, current, resale);
            }
        }

        return null;
    }

    /// <summary>A pack of the best affordable potions the bot is missing (healing, then mana).</summary>
    private static Purchase? PotionPurchase(Player bot, long gold, MobaShopTier tier)
    {
        foreach (var numbers in new[] { new short[] { 1, 2, 3 }, new short[] { 4, 5, 6 } })
        {
            if (FindPotion(bot, numbers) is not null)
            {
                continue;
            }

            var number = numbers[Math.Min((int)tier, numbers.Length - 1)];
            var definition = bot.GameContext.Configuration.Items.FirstOrDefault(d => d.Group == 14 && d.Number == number);
            var entry = new MobaShopEntry(MobaShopCategory.Consumables, null, 14, number, tier, MobaShopCatalog.PotionLevelOf(tier), MobaShopCatalog.BotPotionPurchase);
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

    private static async ValueTask<Item?> BuyAsync(Player bot, Purchase plan)
    {
        if (bot.Inventory is not { } inventory)
        {
            return null;
        }

        var cost = plan.Price - plan.Resale;
        if (bot.Money < cost)
        {
            return null;
        }

        // Its OWN copy, like a player buying at the vendor: the template of the plan is cached per class, and handing the same object to every bot of the class
        // mixed their gear up (found 2026-11-25 with 60 duels buying at once).
        var item = bot.PersistenceContext.CreateNew<Item>();
        item.AssignValues(plan.Template);

        if (plan.Slot == byte.MaxValue)
        {
            var bagSlot = inventory.CheckInvSpace(item);
            if (bagSlot is null || !bot.TryRemoveMoney((int)plan.Price))
            {
                return null;
            }

            await inventory.AddItemAsync((byte)bagSlot, item).ConfigureAwait(false);
            bot.Logger.LogInformation("[MOBA-BOT-ECON] \"{Name}\" bought {Item} x{Qty} for {Price}.", bot.SelectedCharacter?.Name, item.Definition?.Name, item.Durability, plan.Price);
            if (item.Definition is { } bought && MobaPets.IsInventoryPet(bought))
            {
                await MobaPets.ToggleFromInventoryAsync(bot, item.ItemSlot).ConfigureAwait(false);
            }

            return item;
        }

        if (plan.Current is not null)
        {
            await SellAsync(bot, plan.Current).ConfigureAwait(false);
        }

        if (!bot.TryRemoveMoney((int)plan.Price))
        {
            return null;
        }

        if (!await inventory.AddItemAsync(plan.Slot, item).ConfigureAwait(false))
        {
            bot.Logger.LogWarning("[MOBA-BOT-ECON] \"{Name}\" could not equip {Item} into slot {Slot}.", bot.SelectedCharacter?.Name, item.Definition?.Name, plan.Slot);
            bot.TryAddMoney((int)plan.Price);
            return null;
        }

        bot.Logger.LogInformation("[MOBA-BOT-ECON] \"{Name}\" bought {Item} for {Price} into slot {Slot} (resold {Old} for {Resale}); gold left {Gold}.", bot.SelectedCharacter?.Name, plan.Template.Definition?.Name, plan.Price, plan.Slot, plan.Current?.Definition?.Name ?? "-", plan.Resale, bot.Money);
        return item;
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

    /// <summary>Describes, for the drop report, whether the item would be an upgrade for the champion and what it wears in that slot.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="item">The dropped item.</param>
    /// <returns>(upgrade, worn item name, worn item price).</returns>
    internal static (bool Upgrade, string Current, long CurrentPrice) DescribeUpgrade(Player champion, Item item)
    {
        if (champion.SelectedCharacter?.CharacterClass is not { } characterClass || item.Definition is not { } definition)
        {
            return (false, "-", 0);
        }

        var upgrade = IsUpgrade(champion, item, characterClass);
        var slot = BestSlotFor(champion, definition);
        var worn = slot is { } s ? champion.Inventory?.GetItem(s) : null;
        return (upgrade, worn?.Definition?.Name ?? "-", worn is null ? 0 : MobaShop.PriceOf(worn));
    }

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

    /// <summary>The equipment slot a definition would go to: of the slots the champion uses it in (see <see cref="SlotsFor"/>), the one holding the cheapest piece.</summary>
    private static byte? BestSlotFor(Player bot, ItemDefinition definition)
    {
        var slots = SlotsFor(bot, definition);
        if (slots.Length == 0)
        {
            return null;
        }

        return slots.OrderBy(s => bot.Inventory?.GetItem(s) is { } worn ? MobaShop.PriceOf(worn) : 0).First();
    }

    /// <summary>Whether the champion fights with a weapon in each hand: the Rage Fighter always, the Blade Knight and the Magic Gladiator when they chose it over the shield.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns><see langword="true"/> with two weapons.</returns>
    public static bool DualWields(Player champion) => MobaPassives.FamilyOf(champion) switch
    {
        MobaFamily.RageFighter => true,
        MobaFamily.Knight or MobaFamily.MagicGladiator => champion is MobaBotPlayer bot ? bot.DualWield : WearsTwoWeapons(champion),
        _ => false,
    };

    private static bool WearsTwoWeapons(Player champion)
        => new[] { InventoryConstants.LeftHandSlot, InventoryConstants.RightHandSlot }
            .All(slot => champion.Inventory?.GetItem(slot)?.Definition is { } d && CatalogWeapons.Value.Contains((d.Group, d.Number)));

    private static byte MainHandOf(Player champion)
        => MobaPassives.FamilyOf(champion) == MobaFamily.Elf ? InventoryConstants.RightHandSlot : InventoryConstants.LeftHandSlot;

    /// <summary>
    /// The slots a bot uses an item in. Hands (2026-11-25): ONE weapon (the bow for the Elf; the Elf bot takes no crossbow nor buys arrows) and the other hand only
    /// for the shield / book (the Horse and the Raven of the Dark Lord work from the bag); two weapons only with <see cref="DualWields"/>, which then takes no shield. Before this, every purchase
    /// went to the hand with the cheapest piece: an old staff replaced the shield and the Elf bought her bow after the whole set.
    /// </summary>
    private static byte[] SlotsFor(Player bot, ItemDefinition definition)
    {
        if (definition.ItemSlot?.ItemSlots is not { Count: > 0 } listed)
        {
            return Array.Empty<byte>();
        }

        var slots = listed.Select(s => (byte)s).ToArray();
        var key = (definition.Group, definition.Number);
        if (CatalogWeapons.Value.Contains(key))
        {
            if (definition.IsAmmunition)
            {
                return Array.Empty<byte>();
            }

            return DualWields(bot)
                ? slots.Where(s => s == InventoryConstants.LeftHandSlot || s == InventoryConstants.RightHandSlot).ToArray()
                : slots.Where(s => s == MainHandOf(bot)).ToArray();
        }

        if (CatalogOffhands.Value.Contains(key))
        {
            return DualWields(bot) ? Array.Empty<byte>() : slots.Where(s => s == InventoryConstants.RightHandSlot).ToArray();
        }

        return slots;
    }

    private static HashSet<(byte Group, short Number)> CatalogItemsOf(MobaShopCategory category)
        => MobaShopCatalog.Entries.Where(e => e.Category == category).Select(e => (e.Group, e.Number)).ToHashSet();

    private static IReadOnlyList<Candidate> CandidatesFor(Player bot, CharacterClass characterClass)
        => CandidatesByClass.GetOrAdd(characterClass.Number, _ =>
        {
            var list = new List<Candidate>();
            foreach (var category in GearCategories)
            {
                // Tier by tier (2026-11-25): asked all at once, the sets did not fit on the grid and the bots never bought most tier 3 pieces.
                foreach (var tier in Enum.GetValues<MobaShopTier>())
                {
                    int overflow;
                    foreach (var template in MobaShop.BuildCategoryItems(bot.GameContext.Configuration, characterClass, category, tier, out overflow))
                    {
                        if (template.Definition?.ItemSlot?.ItemSlots is { Count: > 0 } slots)
                        {
                            list.Add(new Candidate(template, slots.Select(s => (byte)s).ToArray(), MobaShop.PriceOf(template)));
                        }
                    }

                    if (overflow > 0)
                    {
                        bot.Logger.LogWarning("[MOBA-BOT-ECON] {Overflow} item(s) of {Category} {Tier} of class {Class} didn't fit on the grid: the bots can't buy them.", overflow, category, tier, characterClass.Name);
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

        public Dictionary<ILocateable, int> LootAttempts { get; } = new();
    }
}
