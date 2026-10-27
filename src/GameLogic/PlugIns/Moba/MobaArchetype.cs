// <copyright file="MobaArchetype.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// What the pieces of a set are good at depends on who wears them and on which set it is (2026-10-25, decided with the user).
/// <para>
/// <b>Who wears it:</b> a Blade Knight's armour is about life, regeneration and durability; a Soul Master's about mana and spell power; an
/// Elf's about healing and critical hits; a Dark Lord mixes both. So a fight between an Elf and a Blade Knight, or between a Blade Knight and a
/// Soul Master, feels different: the bruiser outlasts, the mage hits hard but runs dry.
/// </para>
/// <para>
/// <b>Which set it is:</b> where a class is offered two sets of the same tier, the first one is the <see cref="MobaPieceProfile.Recovery"/> set
/// (life or mana, and recovery) and the second one the <see cref="MobaPieceProfile.Attack"/> set (skill power and critical hits), so a
/// player is not forced to follow the style of its class; a class with a single set in a tier gets the <see cref="MobaPieceProfile.Balanced"/>
/// one (half of each). The weapons of the third tier work the same way (the first weapon balanced, the second one the attack weapon); the
/// three mixes of a weapon of the first two tiers differ by their excellent options (<c>MobaShop.ExcellentPickPriority</c>).
/// </para>
/// <para>
/// The size of a bonus grows with the tier (a third of the full value per tier). The champion adds up what it wears (<see cref="TotalOf"/>);
/// the combat reads the totals: <see cref="TickAsync"/> keeps the life and mana pools and the life regeneration, and the other systems ask for
/// the durability, mana regeneration, skill power, healing power and critical chance.
/// </para>
/// </summary>
public static class MobaArchetype
{
    /// <summary>The first group of the set pieces (helm); armor, pants, gloves and boots follow.</summary>
    public const byte FirstPieceGroup = 7;

    /// <summary>The last group of the set pieces (boots).</summary>
    public const byte LastPieceGroup = 11;

    /// <summary>The slot key of the weapon in the piece tables (set pieces use their item group, 7 to 11).</summary>
    public const byte WeaponKey = 200;

    /// <summary>How many tiers a shop piece has (the size of a bonus is the tier over this).</summary>
    private const double TierCount = 3.0;

    /// <summary>A balanced piece gives this share of each of the other two profiles.</summary>
    private const double BalancedShare = 0.5;

    private static readonly TimeSpan CacheLife = TimeSpan.FromMilliseconds(250);

    private static readonly byte[] PieceSlots =
    {
        InventoryConstants.HelmSlot, InventoryConstants.ArmorSlot, InventoryConstants.PantsSlot, InventoryConstants.GlovesSlot, InventoryConstants.BootsSlot,
    };

    private static readonly Piece[] BruiserRecovery =
    {
        new(7, MobaArchetypeTrait.HealthPool, 0.12),
        new(8, MobaArchetypeTrait.Durability, 0.05),
        new(9, MobaArchetypeTrait.Durability, 0.05),
        new(10, MobaArchetypeTrait.HealthRegen, 0.007),
        new(11, MobaArchetypeTrait.HealthRegen, 0.007),
        new(WeaponKey, MobaArchetypeTrait.HealthPool, 0.05),
        new(WeaponKey, MobaArchetypeTrait.HealthRegen, 0.004),
    };

    private static readonly Piece[] CasterRecovery =
    {
        new(7, MobaArchetypeTrait.ManaPool, 0.20),
        new(8, MobaArchetypeTrait.ManaRegen, 0.12),
        new(9, MobaArchetypeTrait.ManaRegen, 0.12),
        new(10, MobaArchetypeTrait.HealthRegen, 0.005),
        new(11, MobaArchetypeTrait.HealthPool, 0.05),
        new(WeaponKey, MobaArchetypeTrait.ManaPool, 0.10),
        new(WeaponKey, MobaArchetypeTrait.ManaRegen, 0.08),
    };

    private static readonly Piece[] ElfRecovery =
    {
        new(7, MobaArchetypeTrait.HealPower, 0.10),
        new(8, MobaArchetypeTrait.HealPower, 0.10),
        new(9, MobaArchetypeTrait.HealthPool, 0.08),
        new(10, MobaArchetypeTrait.HealthRegen, 0.005),
        new(11, MobaArchetypeTrait.ManaRegen, 0.10),
        new(WeaponKey, MobaArchetypeTrait.HealPower, 0.06),
        new(WeaponKey, MobaArchetypeTrait.ManaRegen, 0.06),
    };

    private static readonly Piece[] DarkLordRecovery =
    {
        new(7, MobaArchetypeTrait.HealthPool, 0.08),
        new(8, MobaArchetypeTrait.Durability, 0.06),
        new(9, MobaArchetypeTrait.ManaRegen, 0.12),
        new(10, MobaArchetypeTrait.HealthRegen, 0.006),
        new(11, MobaArchetypeTrait.HealthRegen, 0.006),
        new(WeaponKey, MobaArchetypeTrait.HealthPool, 0.05),
        new(WeaponKey, MobaArchetypeTrait.ManaRegen, 0.06),
    };

    private static readonly Piece[] GladiatorRecovery =
    {
        new(8, MobaArchetypeTrait.ManaPool, 0.10),
        new(9, MobaArchetypeTrait.Durability, 0.06),
        new(10, MobaArchetypeTrait.HealthRegen, 0.006),
        new(11, MobaArchetypeTrait.ManaRegen, 0.10),
        new(WeaponKey, MobaArchetypeTrait.HealthPool, 0.05),
        new(WeaponKey, MobaArchetypeTrait.ManaRegen, 0.06),
    };

    // The attack sets are the same for every class: skill power and critical hits, with a little of the pool of the class.
    private static readonly Piece[] BruiserAttack = AttackSet(MobaArchetypeTrait.HealthPool, 0.04);

    private static readonly Piece[] CasterAttack = AttackSet(MobaArchetypeTrait.ManaPool, 0.08);

    private static readonly Piece[] ElfAttack = AttackSet(MobaArchetypeTrait.HealthPool, 0.04);

    private static readonly Piece[] DarkLordAttack = AttackSet(MobaArchetypeTrait.ManaRegen, 0.08);

    private static readonly Piece[] GladiatorAttack = AttackSet(MobaArchetypeTrait.ManaPool, 0.05, includeHelm: false);

    private static readonly Dictionary<MobaFamily, (Piece[] Recovery, Piece[] Attack)> Tables = new()
    {
        [MobaFamily.Knight] = (BruiserRecovery, BruiserAttack),
        [MobaFamily.RageFighter] = (BruiserRecovery, BruiserAttack),
        [MobaFamily.Wizard] = (CasterRecovery, CasterAttack),
        [MobaFamily.Summoner] = (CasterRecovery, CasterAttack),
        [MobaFamily.RuneWizard] = (CasterRecovery, CasterAttack),
        [MobaFamily.Elf] = (ElfRecovery, ElfAttack),
        [MobaFamily.DarkLord] = (DarkLordRecovery, DarkLordAttack),
        [MobaFamily.MagicGladiator] = (GladiatorRecovery, GladiatorAttack),
    };

    private static readonly ConditionalWeakTable<Player, Snapshot> Snapshots = new();

    private static readonly ConditionalWeakTable<Player, PoolElements> Pools = new();

    private static readonly Lazy<Catalog> CatalogInfo = new(() => new Catalog());

    private static DateTime _lastTickUtc = DateTime.MinValue;

    /// <summary>
    /// The bonuses a piece gives to a family.
    /// </summary>
    /// <param name="family">The family of the class that wears it.</param>
    /// <param name="key">The item group of a set piece (7 helm, 8 armor, 9 pants, 10 gloves, 11 boots) or <see cref="WeaponKey"/>.</param>
    /// <param name="tier">The tier of the piece, 1 to 3.</param>
    /// <param name="profile">The profile of the set the piece belongs to.</param>
    /// <returns>The traits with their sizes as fractions (0.05 = 5 %); empty if the slot gives none.</returns>
    public static IReadOnlyList<(MobaArchetypeTrait Trait, double Value)> PieceBonusesOf(MobaFamily family, byte key, int tier, MobaPieceProfile profile)
    {
        var result = new List<(MobaArchetypeTrait, double)>(2);
        if (tier <= 0 || !Tables.TryGetValue(family, out var tables))
        {
            return result;
        }

        var scale = Math.Min(tier, TierCount) / TierCount;
        var share = profile == MobaPieceProfile.Balanced ? scale * BalancedShare : scale;
        if (profile != MobaPieceProfile.Attack)
        {
            Add(result, tables.Recovery, key, share);
        }

        if (profile != MobaPieceProfile.Recovery)
        {
            Add(result, tables.Attack, key, share);
        }

        return result;
    }

    /// <summary>The profile of a set piece, by its item (the sets are told apart by their item number inside a family and a tier).</summary>
    /// <param name="family">The family of the class.</param>
    /// <param name="number">The item number.</param>
    /// <returns>The profile, or <see cref="MobaPieceProfile.Balanced"/> for an item the catalog does not know.</returns>
    public static MobaPieceProfile SetProfileOf(MobaFamily family, short number)
        => CatalogInfo.Value.SetProfiles.TryGetValue((family, number), out var profile) ? profile : MobaPieceProfile.Balanced;

    /// <summary>The profile of a weapon, by its item (the third tier offers two weapons: the second one is the attack weapon).</summary>
    /// <param name="family">The family of the class.</param>
    /// <param name="group">The item group.</param>
    /// <param name="number">The item number.</param>
    /// <returns>The profile.</returns>
    public static MobaPieceProfile WeaponProfileOf(MobaFamily family, byte group, short number)
        => CatalogInfo.Value.AttackWeapons.Contains((family, group, number)) ? MobaPieceProfile.Attack : MobaPieceProfile.Balanced;

    /// <summary>Gets the total of one trait over the pieces the champion wears.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="trait">The trait.</param>
    /// <returns>The total as a fraction (0.10 = 10 %).</returns>
    public static double TotalOf(Player champion, MobaArchetypeTrait trait)
    {
        if (!champion.IsMobaClone)
        {
            return 0;
        }

        var snapshot = Snapshots.GetOrCreateValue(champion);
        var now = DateTime.UtcNow;
        if (now - snapshot.AtUtc > CacheLife)
        {
            Refresh(champion, snapshot, now);
        }

        return snapshot.Totals[(int)trait];
    }

    /// <summary>Extra damage reduction of the pieces (0 to about 0.10).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The fraction of the damage taken which is cut.</returns>
    public static double DurabilityOf(Player champion) => TotalOf(champion, MobaArchetypeTrait.Durability);

    /// <summary>Extra mana regeneration of the pieces, as a fraction of the base regeneration.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The fraction.</returns>
    public static double ManaRegenOf(Player champion) => TotalOf(champion, MobaArchetypeTrait.ManaRegen);

    /// <summary>The factor on the damage of the skills (1 = no bonus).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The factor.</returns>
    public static double SpellPowerFactorOf(Player champion) => 1.0 + TotalOf(champion, MobaArchetypeTrait.SpellPower);

    /// <summary>The factor on the healing and the shields the champion gives (1 = no bonus).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The factor.</returns>
    public static double HealPowerFactorOf(Player champion) => 1.0 + TotalOf(champion, MobaArchetypeTrait.HealPower);

    /// <summary>Extra chance of a critical hit given by the pieces.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The chance as a fraction.</returns>
    public static double CritChanceOf(Player champion) => TotalOf(champion, MobaArchetypeTrait.CritChance);

    /// <summary>
    /// Once a second: grows or shrinks the life and mana pools by what the pieces give and regenerates life.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        var now = DateTime.UtcNow;
        var seconds = _lastTickUtc == DateTime.MinValue ? 1.0 : Math.Clamp((now - _lastTickUtc).TotalSeconds, 0.05, 2.5);
        _lastTickUtc = now;

        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var champion in players.Where(p => p.IsMobaClone && p.Attributes is not null))
        {
            var attributes = champion.Attributes!;
            var pools = Pools.GetOrCreateValue(champion);
            SyncPool(attributes, Stats.MaximumHealth, Stats.CurrentHealth, TotalOf(champion, MobaArchetypeTrait.HealthPool), ref pools.Health);
            SyncPool(attributes, Stats.MaximumMana, Stats.CurrentMana, TotalOf(champion, MobaArchetypeTrait.ManaPool), ref pools.Mana);
            RegenerateHealth(champion, attributes, seconds);
        }
    }

    /// <summary>Removes the pool bonuses of a champion (before its level scaling pins the pools to the new level).</summary>
    /// <param name="champion">The champion.</param>
    public static void Reset(Player champion)
    {
        if (champion.Attributes is not { } attributes || !Pools.TryGetValue(champion, out var pools))
        {
            return;
        }

        RemoveElement(attributes, Stats.MaximumHealth, ref pools.Health);
        RemoveElement(attributes, Stats.MaximumMana, ref pools.Mana);
    }

    private static Piece[] AttackSet(MobaArchetypeTrait helm, double helmFull, bool includeHelm = true)
    {
        var pieces = new List<Piece>
        {
            new(8, MobaArchetypeTrait.SpellPower, 0.05),
            new(9, MobaArchetypeTrait.SpellPower, 0.05),
            new(10, MobaArchetypeTrait.CritChance, 0.03),
            new(11, MobaArchetypeTrait.CritChance, 0.03),
            new(WeaponKey, MobaArchetypeTrait.SpellPower, 0.06),
            new(WeaponKey, MobaArchetypeTrait.CritChance, 0.03),
        };

        if (includeHelm)
        {
            pieces.Insert(0, new Piece(7, helm, helmFull));
        }

        return pieces.ToArray();
    }

    private static void Add(List<(MobaArchetypeTrait, double)> result, Piece[] pieces, byte key, double scale)
    {
        foreach (var piece in pieces)
        {
            if (piece.Key == key)
            {
                result.Add((piece.Trait, piece.Full * scale));
            }
        }
    }

    private static void Refresh(Player champion, Snapshot snapshot, DateTime now)
    {
        Array.Clear(snapshot.Totals);
        snapshot.AtUtc = now;
        var family = MobaPassives.FamilyOf(champion);
        for (var i = 0; i < PieceSlots.Length; i++)
        {
            var item = champion.Inventory?.Items.FirstOrDefault(it => it.ItemSlot == PieceSlots[i]);
            var tier = MobaItemPower.TierIn(champion, PieceSlots[i], MobaShopCategory.Sets);
            if (item?.Definition is { } definition && tier > 0)
            {
                AddTotals(snapshot, PieceBonusesOf(family, (byte)(FirstPieceGroup + i), tier, SetProfileOf(family, (short)definition.Number)));
            }
        }

        var weapon = champion.Inventory?.Items.FirstOrDefault(it => it.ItemSlot == InventoryConstants.RightHandSlot);
        var weaponTier = MobaItemPower.TierIn(champion, InventoryConstants.RightHandSlot, MobaShopCategory.Weapons);
        if (weapon?.Definition is { } weaponDefinition && weaponTier > 0)
        {
            AddTotals(snapshot, PieceBonusesOf(family, WeaponKey, weaponTier, WeaponProfileOf(family, weaponDefinition.Group, (short)weaponDefinition.Number)));
        }
    }

    private static void AddTotals(Snapshot snapshot, IReadOnlyList<(MobaArchetypeTrait Trait, double Value)> bonuses)
    {
        foreach (var (trait, value) in bonuses)
        {
            snapshot.Totals[(int)trait] += value;
        }
    }

    private static void RegenerateHealth(Player champion, IAttributeSystem attributes, double seconds)
    {
        var fraction = TotalOf(champion, MobaArchetypeTrait.HealthRegen);
        if (fraction <= 0 || !champion.IsAlive)
        {
            return;
        }

        var maximum = attributes[Stats.MaximumHealth];
        var healed = MobaItemTraits.ScaleHealing(champion, maximum * fraction * seconds);
        attributes[Stats.CurrentHealth] = (float)Math.Min(maximum, attributes[Stats.CurrentHealth] + healed);
    }

    private static void SyncPool(IAttributeSystem attributes, AttributeDefinition maximum, AttributeDefinition current, double fraction, ref SimpleElement? element)
    {
        const float MinimumChange = 1f;
        var applied = element?.Value ?? 0f;
        var wanted = (float)((attributes[maximum] - applied) * fraction);
        if (Math.Abs(wanted - applied) < MinimumChange)
        {
            return;
        }

        RemoveElement(attributes, maximum, ref element);
        if (wanted > 0)
        {
            element = new SimpleElement(wanted, AggregateType.AddRaw);
            attributes.AddElement(element, maximum);
        }

        // A bigger pool arrives full of what it grew by (like buying a life item); a smaller one just caps the current value.
        attributes[current] = Math.Min(attributes[maximum], attributes[current] + Math.Max(0f, wanted - applied));
    }

    private static void RemoveElement(IAttributeSystem attributes, AttributeDefinition maximum, ref SimpleElement? element)
    {
        if (element is not null)
        {
            attributes.RemoveElement(element, maximum);
            element = null;
        }
    }

    private readonly record struct Piece(byte Key, MobaArchetypeTrait Trait, double Full);

    private sealed class Snapshot
    {
        public DateTime AtUtc = DateTime.MinValue;

        public double[] Totals = new double[Enum.GetValues<MobaArchetypeTrait>().Max(t => (int)t) + 1];
    }

    private sealed class PoolElements
    {
        public SimpleElement? Health;

        public SimpleElement? Mana;
    }

    /// <summary>What the catalog says about the sets and the weapons: which set is the second of its tier and which weapon is the attack one.</summary>
    private sealed class Catalog
    {
        public Catalog()
        {
            var alternates = MobaShopCatalog.Entries
                .Where(e => e.Category == MobaShopCategory.Sets && e.Variant is MobaShopVariant.Bulwark or MobaShopVariant.Utility && e.Families is { Length: > 0 })
                .Select(e => (Family: e.Families![0], e.Tier, e.Number))
                .Distinct()
                .ToList();

            foreach (var entry in MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Sets && e.Families is { Length: > 0 }))
            {
                var family = entry.Families![0];
                var isAlternate = alternates.Any(a => a.Family == family && a.Number == entry.Number);
                var hasAlternate = alternates.Any(a => a.Family == family && a.Tier == entry.Tier);
                this.SetProfiles[(family, entry.Number)] = isAlternate ? MobaPieceProfile.Attack : hasAlternate ? MobaPieceProfile.Recovery : MobaPieceProfile.Balanced;
            }

            foreach (var group in MobaShopCatalog.Entries
                         .Where(e => e.Category == MobaShopCategory.Weapons && e.Families is { Length: > 0 })
                         .Select(e => (Family: e.Families![0], e.Tier, e.Group, e.Number))
                         .Distinct()
                         .GroupBy(w => (w.Family, w.Tier)))
            {
                foreach (var weapon in group.Skip(1))
                {
                    this.AttackWeapons.Add((weapon.Family, weapon.Group, weapon.Number));
                }
            }
        }

        public Dictionary<(MobaFamily Family, short Number), MobaPieceProfile> SetProfiles { get; } = new();

        public HashSet<(MobaFamily Family, byte Group, short Number)> AttackWeapons { get; } = new();
    }
}

/// <summary>
/// The bonuses the pieces of a set give by archetype (see <see cref="MobaArchetype"/>). The numbers are the kinds of the tooltip packet
/// (1-5 are the older tier traits of <see cref="MobaItemTraits"/>).
/// </summary>
public enum MobaArchetypeTrait : byte
{
    /// <summary>More maximum life.</summary>
    HealthPool = 6,

    /// <summary>Less damage taken.</summary>
    Durability = 7,

    /// <summary>More maximum mana.</summary>
    ManaPool = 8,

    /// <summary>More mana regeneration.</summary>
    ManaRegen = 9,

    /// <summary>More damage of the skills.</summary>
    SpellPower = 10,

    /// <summary>More healing and shields given.</summary>
    HealPower = 11,

    /// <summary>More chance of a critical hit.</summary>
    CritChance = 12,

    /// <summary>Life regenerated every second, as a fraction of the maximum life.</summary>
    HealthRegen = 13,
}

/// <summary>
/// The leaning of a set or weapon (see <see cref="MobaArchetype"/>): where a class is offered two sets of a tier, the first one recovers
/// and the second one attacks.
/// </summary>
public enum MobaPieceProfile
{
    /// <summary>Half of the recovery bonuses and half of the attack bonuses (the only set of a tier, the first weapon).</summary>
    Balanced,

    /// <summary>The bonuses of the style of the class: life or mana, and recovery.</summary>
    Recovery,

    /// <summary>Skill power and critical hits.</summary>
    Attack,
}
