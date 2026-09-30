// <copyright file="MobaItemTraits.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// The MOBA-only options of shop pieces (they have no native MU attribute), read straight off the
/// equipped items like <see cref="MobaItemPower"/>: the tier of the piece decides the size.
/// <list type="bullet">
/// <item><description>Weapon: <b>anti-heal</b> - hits cut the healing the target receives (10 / 20 / 30 %) for a few seconds.</description></item>
/// <item><description>Armor and pants: <b>crowd-control resistance</b> (5 / 10 / 15 % each, so up to 30 %).</description></item>
/// <item><description>Gloves and boots: <b>cooldown reduction</b> (6 / 12 / 20 % each, so up to 40 %; with a T3 shield 65 %).</description></item>
/// <item><description>Off-hand (shield / book): up to four options by tier - T1 CC resistance 10 %; T2 CC 15 % and
/// cooldown reduction 15 %; T3 all four (CC 20 %, cooldown 25 %, assist gold 10 %, passive gold 10 %). Shields
/// dropped by creeps carry 1 (70 %) or 2 (30 %) random ones of the four at the size of their tier.</description></item>
/// <item><description>Rings: <b>gold per assist</b> (5 / 10 / 15 % each); pendant: <b>passive gold</b> (5 / 10 / 15 %).</description></item>
/// </list>
/// </summary>
public static class MobaItemTraits
{
    /// <summary>Seconds a hit of an anti-heal weapon keeps the target's healing reduced.</summary>
    public const double AntiHealSeconds = 3;

    private static readonly double[] AntiHealByTier = { 0, 0.10, 0.20, 0.30 };
    private static readonly double[] CcResistByTier = { 0, 0.05, 0.10, 0.15 };
    private static readonly double[] CooldownByTier = { 0, 0.06, 0.12, 0.20 };
    private static readonly double[] GoldByTier = { 0, 0.05, 0.10, 0.15 };

    /// <summary>Highest total CC resistance (armor + pants + shield).</summary>
    public const double MaxCcResist = 0.50;

    /// <summary>Highest total cooldown reduction (gloves + boots + shield).</summary>
    public const double MaxCooldownReduction = 0.65;

    private const byte KindCc = 2;
    private const byte KindCooldown = 3;
    private const byte KindAssistGold = 4;
    private const byte KindPassiveGold = 5;

    private static readonly byte[] ShieldKinds = { KindCc, KindCooldown, KindAssistGold, KindPassiveGold };

    // Size of each shield option by tier (index 1..3). The cooldown of T1 and the gold of T1 / T2 only appear on drops.
    private static readonly double[] ShieldCcByTier = { 0, 0.10, 0.15, 0.20 };
    private static readonly double[] ShieldCooldownByTier = { 0, 0.05, 0.15, 0.25 };
    private static readonly double[] ShieldGoldByTier = { 0, 0.05, 0.08, 0.10 };

    private static readonly ConditionalWeakTable<Player, AntiHealState> AntiHeals = new();

    private static readonly ConditionalWeakTable<Item, ShieldOptionSet> RolledShields = new();

    private static readonly object ShieldLock = new();

    private static readonly Dictionary<(ushort Type, byte Level, byte OptionLevel, bool Luck, byte Excellent), (byte Kind, byte Percent)[]> ShieldRegistry = new();

    private static GameConfiguration? _shieldsFor;

    private static IReadOnlyList<Views.Moba.MobaItemTrait>? _tooltipTraits;

    /// <summary>
    /// Gets the trait of every catalog item (client type, kind, percent), sent to the client for the tooltips.
    /// Kinds: 1 anti-heal, 2 CC resistance, 3 cooldown reduction, 4 assist gold, 5 passive gold.
    /// </summary>
    public static IReadOnlyList<Views.Moba.MobaItemTrait> TooltipTraits => _tooltipTraits ??= BuildTooltipTraits();

    /// <summary>The fraction of healing a weapon hit removes.</summary>
    /// <param name="champion">The attacking champion.</param>
    /// <returns>0..0.30.</returns>
    public static double AntiHealOf(Player champion)
        => AntiHealByTier[Math.Max(
            MobaItemPower.TierIn(champion, InventoryConstants.RightHandSlot, MobaShopCategory.Weapons),
            MobaItemPower.TierIn(champion, InventoryConstants.LeftHandSlot, MobaShopCategory.Weapons))];

    /// <summary>The crowd-control duration reduction of the armor and pants.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.30.</returns>
    public static double CcResistOf(Player champion)
        => Math.Min(
            MaxCcResist,
            CcResistByTier[MobaItemPower.TierIn(champion, InventoryConstants.ArmorSlot, MobaShopCategory.Sets)]
            + CcResistByTier[MobaItemPower.TierIn(champion, InventoryConstants.PantsSlot, MobaShopCategory.Sets)]
            + ShieldOptionOf(champion, KindCc));

    /// <summary>The skill cooldown reduction of the gloves and boots.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.65.</returns>
    public static double CooldownReductionOf(Player champion)
        => Math.Min(
            MaxCooldownReduction,
            CooldownByTier[MobaItemPower.TierIn(champion, InventoryConstants.GlovesSlot, MobaShopCategory.Sets)]
            + CooldownByTier[MobaItemPower.TierIn(champion, InventoryConstants.BootsSlot, MobaShopCategory.Sets)]
            + ShieldOptionOf(champion, KindCooldown));

    /// <summary>The extra gold from assists (rings).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.30.</returns>
    public static double AssistGoldBonusOf(Player champion)
        => GoldByTier[MobaItemPower.TierIn(champion, InventoryConstants.Ring1Slot, MobaShopCategory.Accessories)]
           + GoldByTier[MobaItemPower.TierIn(champion, InventoryConstants.Ring2Slot, MobaShopCategory.Accessories)]
           + ShieldOptionOf(champion, KindAssistGold);

    /// <summary>The extra passive gold (pendant).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..0.15.</returns>
    public static double PassiveGoldBonusOf(Player champion)
        => GoldByTier[MobaItemPower.TierIn(champion, InventoryConstants.PendantSlot, MobaShopCategory.Accessories)]
           + ShieldOptionOf(champion, KindPassiveGold);

    /// <summary>Gets the shield options of every known shield / book (shop ones and rolled drops), for the client tooltips.</summary>
    public static IReadOnlyCollection<Views.Moba.MobaShieldOption> ShieldOptionTable
    {
        get
        {
            lock (ShieldLock)
            {
                return ShieldRegistry
                    .Select(e => new Views.Moba.MobaShieldOption(e.Key.Type, e.Key.Level, e.Key.OptionLevel, e.Key.Luck, e.Key.Excellent, e.Value))
                    .ToList();
            }
        }
    }

    /// <summary>Registers the options the shop shields / books are sold with (by tier), once per configuration.</summary>
    /// <param name="configuration">The game configuration.</param>
    public static void EnsureShopShieldOptions(GameConfiguration configuration)
    {
        lock (ShieldLock)
        {
            if (ReferenceEquals(_shieldsFor, configuration))
            {
                return;
            }

            foreach (var entry in MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Offhand))
            {
                if (configuration.Items.FirstOrDefault(d => d.Group == entry.Group && d.Number == entry.Number) is { } definition)
                {
                    var item = MobaShop.CreateItem(definition, entry);
                    ShieldRegistry.TryAdd(KeyOf(item), DefaultShieldOptions((int)entry.Tier));
                }
            }

            _shieldsFor = configuration;
        }
    }

    /// <summary>
    /// Rolls the options of a shield / book dropped by a creep: 1 option (70 %) or 2 (30 %) of the four, at the
    /// size of the item tier. Items are identified by the client by their properties, so if another shield
    /// with the same properties already has options, this one takes the same ones (the tooltips stay right).
    /// </summary>
    /// <param name="item">The dropped item.</param>
    /// <param name="tier">The tier of the item.</param>
    /// <returns><c>true</c> if a new entry was added to the <see cref="ShieldOptionTable"/> (clients must be told).</returns>
    public static bool AssignDropOptions(Item item, MobaShopTier tier)
    {
        var key = KeyOf(item);
        lock (ShieldLock)
        {
            if (ShieldRegistry.TryGetValue(key, out var existing))
            {
                RolledShields.AddOrUpdate(item, new ShieldOptionSet(existing));
                return false;
            }

            var count = Rand.NextRandomBool(0.7) ? 1 : 2;
            var kinds = ShieldKinds.OrderBy(_ => Rand.NextDouble()).Take(count).OrderBy(k => k).ToArray();
            var options = kinds.Select(k => (k, SizeOf(k, (int)tier))).ToArray();
            ShieldRegistry[key] = options;
            RolledShields.AddOrUpdate(item, new ShieldOptionSet(options));
            return true;
        }
    }

    private static (ushort Type, byte Level, byte OptionLevel, bool Luck, byte Excellent) KeyOf(Item item)
        => ((ushort)(((item.Definition?.Group ?? 0) * 512) + (item.Definition?.Number ?? 0)),
            item.Level,
            (byte)MobaShop.OptionLevelOf(item),
            MobaShop.HasLuck(item),
            (byte)MobaShop.ExcellentCountOf(item));

    private static byte SizeOf(byte kind, int tier)
        => (byte)Math.Round(100 * kind switch
        {
            KindCc => ShieldCcByTier[tier],
            KindCooldown => ShieldCooldownByTier[tier],
            _ => ShieldGoldByTier[tier],
        });

    private static (byte Kind, byte Percent)[] DefaultShieldOptions(int tier)
        => ShieldKinds.Take(tier switch { 1 => 1, 2 => 2, _ => 4 }).Select(k => (k, SizeOf(k, tier))).ToArray();

    /// <summary>The size (fraction) of one shield option of the champion's equipped off-hand item.</summary>
    private static double ShieldOptionOf(Player champion, byte kind)
    {
        var item = champion.Inventory?.Items.FirstOrDefault(i => i.ItemSlot == InventoryConstants.LeftHandSlot);
        if (item?.Definition is not { } definition)
        {
            return 0;
        }

        var entry = MobaShopCatalog.Entries.FirstOrDefault(e => e.Category == MobaShopCategory.Offhand && e.Group == definition.Group && e.Number == definition.Number);
        if (entry is null)
        {
            return 0;
        }

        var options = RolledShields.TryGetValue(item, out var rolled) ? rolled.Options : DefaultShieldOptions((int)entry.Tier);
        return options.Where(o => o.Kind == kind).Sum(o => o.Percent / 100.0);
    }

    /// <summary>Marks the victim of an anti-heal hit.</summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <param name="victim">The victim.</param>
    public static void ApplyAntiHeal(Player attacker, Player victim)
    {
        var fraction = AntiHealOf(attacker);
        if (fraction <= 0)
        {
            return;
        }

        var state = AntiHeals.GetOrCreateValue(victim);
        state.Fraction = Math.Max(state.Fraction, fraction);
        state.UntilUtc = DateTime.UtcNow.AddSeconds(AntiHealSeconds);
    }

    /// <summary>Scales an amount of healing the champion is about to receive by its active anti-heal.</summary>
    /// <param name="target">The healed champion (anything but a MOBA champion is returned unchanged).</param>
    /// <param name="amount">The healing amount.</param>
    /// <returns>The reduced healing.</returns>
    public static double ScaleHealing(IAttackable? target, double amount)
    {
        if (target is Player { IsMobaClone: true } champion
            && AntiHeals.TryGetValue(champion, out var state)
            && DateTime.UtcNow < state.UntilUtc)
        {
            return amount * (1 - state.Fraction);
        }

        return amount;
    }

    private static List<Views.Moba.MobaItemTrait> BuildTooltipTraits()
    {
        var traits = new Dictionary<ushort, Views.Moba.MobaItemTrait>();
        foreach (var entry in MobaShopCatalog.Entries)
        {
            var tier = (int)entry.Tier;
            (byte Kind, double[] Table)? trait = entry.Category switch
            {
                MobaShopCategory.Weapons when entry.Quantity == 1 => (1, AntiHealByTier),
                MobaShopCategory.Sets or MobaShopCategory.SetsSustain when entry.Group is 8 or 9 => (2, CcResistByTier),
                MobaShopCategory.Sets or MobaShopCategory.SetsSustain when entry.Group is 10 or 11 => (3, CooldownByTier),
                MobaShopCategory.Accessories => (entry.Number is 8 or 23 or 24 ? (byte)4 : (byte)5, GoldByTier),
                _ => null,
            };

            if (trait is { } t)
            {
                var type = (ushort)((entry.Group * 512) + entry.Number);
                traits.TryAdd(type, new Views.Moba.MobaItemTrait(type, t.Kind, (byte)Math.Round(t.Table[tier] * 100)));
            }
        }

        return traits.Values.ToList();
    }

    private sealed class ShieldOptionSet
    {
        public ShieldOptionSet((byte Kind, byte Percent)[] options) => this.Options = options;

        public (byte Kind, byte Percent)[] Options { get; }
    }

    private sealed class AntiHealState
    {
        public double Fraction;

        public DateTime UntilUtc;
    }
}
