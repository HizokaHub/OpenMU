// <copyright file="MobaF10Ledger.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The CHANNEL LEDGER of F10 (2026-11-23). Design rule: every element of a build (stat, tree node, piece trait, buff, pet) must give some percent to the
/// damage of the hit (<see cref="LedgerChannel.Offense"/>), some percent against the hit (<see cref="LedgerChannel.Life"/> or
/// <see cref="LedgerChannel.Durability"/>), or sit behind a time gate that scales one of them (mana, crowd control, position). This class
/// (1) converts the channels the first translation of F10 did not see into pool terms (<see cref="ExtraOf"/>), each one a percent <c>g</c> of the hit or <c>h</c> of
/// the effective life turned into pool units with <see cref="GainOffense"/> / <see cref="GainLife"/> so that <c>exp(K * pool)</c> grows by exactly that percent;
/// and (2) declares where every trait and tree kind is counted, so <see cref="Orphans"/> (<c>/mobaf10 audit</c>) lists any element that is in the game
/// but nowhere in the model.
/// <para>
/// Only the DEVIATION from the reference build is counted (the reference has no piece traits), so a build without items stays neutral and the class offsets
/// (<see cref="MobaF10.Psi"/>) keep their meaning. The weights below are first estimates taken from the diagnosis of 2026-11-20, to be checked by ablation
/// (+10 % of an element must move the win rate 1.5-6 %).
/// </para>
/// </summary>
public static class MobaF10Ledger
{
    /// <summary>Gets or sets a value indicating whether the ledger channels enter the pools (<c>/mobaf10 canales on|off</c>), to measure what they move.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the fight horizon T in seconds (<c>/mobaf10 horizonte v</c>): mana, regeneration and leech depend on how long the fight lasts
    /// (a duel ~10 s, a skirmish ~20 s, a siege ~40 s). The model is calibrated for duels.
    /// </summary>
    public static double Horizon { get; set; } = 10;

    /// <summary>Where a trait enters the model.</summary>
    public enum LedgerChannel
    {
        /// <summary>A percent of the damage of the hit (pool <c>O</c>).</summary>
        Offense,

        /// <summary>A percent of effective life: life, sustain, shield (pool <c>life</c>).</summary>
        Life,

        /// <summary>A percent against the hit (pool <c>durability</c>).</summary>
        Durability,

        /// <summary>A time gate (mana, crowd control, position) that scales a channel; counted by a rule of the game, not by the pools.</summary>
        Gate,

        /// <summary>Outside combat (gold, recovery between fights): deliberately not a combat channel.</summary>
        OutsideCombat,

        /// <summary>A real mechanic of the game that every build gets by the same rule at every hit or step (hit rate, movement), so it needs no pool term.</summary>
        Live,

        /// <summary>Declared but still without a weight: the audit reports it as pending.</summary>
        Pending,
    }

    /// <summary>The extra terms of a champion in pool units.</summary>
    /// <param name="Offense">Extra offense pool.</param>
    /// <param name="Life">Extra life pool.</param>
    /// <param name="Durability">Extra durability pool.</param>
    public readonly record struct Extra(double Offense, double Life, double Durability);

    /// <summary>Pool units of a percent gain of the damage: <c>exp(KO * x) = 1 + g</c>.</summary>
    /// <param name="g">The gain as a fraction (0.10 = +10 %).</param>
    /// <returns>The pool units.</returns>
    public static double GainOffense(double g) => g <= -1 ? -10 : Math.Log(1.0 + g) / 0.5;

    /// <summary>Pool units of a percent gain of the effective life: <c>exp(KD * x) = 1 + h</c>.</summary>
    /// <param name="h">The gain as a fraction (0.10 = +10 %).</param>
    /// <returns>The pool units.</returns>
    public static double GainLife(double h) => h <= -1 ? -10 : Math.Log(1.0 + h) / 0.5;

    /// <summary>How much of the damage of a family needs mana (1 = casts all the time, 0 = auto attacks).</summary>
    /// <param name="family">The family.</param>
    /// <returns>The dependence.</returns>
    public static double ManaDependence(MobaFamily family) => family switch
    {
        MobaFamily.Wizard or MobaFamily.Summoner => 1.0,
        MobaFamily.Elf or MobaFamily.DarkLord => 0.7,
        MobaFamily.MagicGladiator => 0.6,
        MobaFamily.RageFighter or MobaFamily.Knight => 0.5,
        _ => 0.5,
    };

    /// <summary>Share of the fight a caster spends without mana for the horizon: 0 up to 5 s, 33 % at 17 s (measured 2026-11-20), flat after.</summary>
    /// <param name="horizon">The horizon in seconds.</param>
    /// <returns>The dry share, 0..0.33.</returns>
    public static double DrySharePerHorizon(double horizon) => Math.Clamp((horizon - 5.0) / 12.0, 0.0, 1.0) * 0.33;

    /// <summary>Gets the extra terms the ledger adds to the pools of a champion.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="family">Its family.</param>
    /// <returns>The terms in pool units.</returns>
    public static Extra ExtraOf(Player champion, MobaFamily family)
    {
        if (!Enabled)
        {
            return default;
        }

        var horizon = Math.Clamp(Horizon, 1.0, 120.0);
        var level = Math.Clamp(champion.MobaLevel <= 0 ? 1 : champion.MobaLevel, 1, MobaLevels.MaxLevel);
        var attributes = champion.Attributes;

        // (1) MANA: the pool, the regeneration, the master tree (maximum mana, usage reduction) move the share of the fight spent dry.
        // A tree-less class counts the reference tree (no deviation), like PoolsOf.
        var refTree = 0.3 * ((level - 1) / 29.0);
        var treeMana = (MobaMasterTree.Fraction(champion, MobaTreeKind.Mana) ?? refTree) - refTree;
        var manaPool = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.ManaPool) + (TreeManaPoolAtFull * treeMana);
        var manaRegen = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.ManaRegen);
        var usage = Math.Clamp(attributes is null ? 0.0 : attributes[Stats.ManaUsageReduction], 0.0, 0.6);
        var usageAsRegen = usage / (1.0 - usage);   // a cost cut of u gives the same endurance as u/(1-u) more regeneration
        var manaGain = ManaDependence(family) * DrySharePerHorizon(horizon) * ((0.6 * manaPool) + (0.8 * (manaRegen + usageAsRegen)));

        // (2) COOLDOWN is already in DamageFactor (CooldownRatio); crit in CritRatio; spell power in the base pool.

        // (3) SD (a third of the life): the weapon pierces it, the wings skip it, the armour makes it absorb more. The victim's shield is a quarter of its pool.
        const double SdShare = 0.25;
        var sdOffense = (MobaItemTraits.SdPierceOf(champion) * 0.9 * SdShare) + (MobaItemTraits.SdBypassOf(champion) * SdShare);
        var sdLife = MobaItemTraits.SdGuardOf(champion) * SdShare;

        // (4) SUSTAIN over the horizon: regeneration (fraction of the life per second) and the heal power over the life buffs the champion carries.
        var regen = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.HealthRegen) * horizon;
        var healPower = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.HealPower);
        var healBuffs = 0.1 * ((MobaBuffPower.RankOn(champion, 26) / 5.0) + (MobaBuffPower.RankOn(champion, 234) / 5.0));
        var sustain = regen + (healPower * healBuffs);

        // (5) ANTI-HEAL of the weapon: cuts the sustain the rival would have (the reference sustain of a rival is ~10 % of its life).
        var antiHeal = MobaItemTraits.AntiHealOf(champion) * 0.10;

        // (6) PETS: the damage of the utility pets (Imp, Demon, Skeleton) was a parallel channel that F10 switches off, and their +75 life is pinned out.
        // Their damage taken reduction (Angel, Spirit, Panda, Unicorn) and speed stay live through DamageReceiveDecrement and the hit rate / movement.
        var petDamage = 0.0;
        var petLife = 0.0;
        var refLife = MobaProgression.HealthAt(level) * MobaF10.HealthScaleAt(level);
        foreach (var number in MobaPets.ActiveUtilityNumbers(champion))
        {
            if (PetDamageGain.TryGetValue(number, out var g))
            {
                petDamage += g;
            }

            if (PetLifeFlat.TryGetValue(number, out var flat))
            {
                petLife += flat / Math.Max(1.0, refLife);
            }
        }

        // FENRIR (mount of the pet slot): the black one adds damage and the gold one a little of damage and life through attributes F10 switches off or pins; the blue one
        // (less damage taken) stays live through DamageReceiveDecrement and every colour gives speed (live, see MovementSpeed below).
        if (champion.Inventory?.GetItem(InventoryConstants.PetSlot) is { Definition: { Group: 13, Number: 37 } } fenrir)
        {
            var colours = fenrir.ItemOptions.Select(o => o.ItemOption?.OptionType).ToList();
            if (colours.Contains(ItemOptionTypes.BlackFenrir))
            {
                petDamage += FenrirBlackDamage;
            }

            if (colours.Contains(ItemOptionTypes.GoldFenrir))
            {
                petDamage += FenrirGoldDamage;
                petLife += FenrirGoldLife;
            }
        }

        // The Dark Lord's pets are part of its kit but the reference (the bots) switches none on: what they add is a share of the total damage.
        if (family == MobaFamily.DarkLord)
        {
            var (earthshake, raven) = MobaPets.DarkLordPetShare(level, 3);
            petDamage += MobaPets.IsHorseActive(champion) ? earthshake / (1.0 - earthshake) : 0.0;
            petDamage += MobaPets.IsRavenActive(champion) ? raven / (1.0 - raven) : 0.0;
        }

        // (7) AG of the accessories (Soul Master family): more jumps of the Teleport, which is repositioning, counted as life (a part of the escapes).
        var agGain = 0.0;
        if (MobaBlink.UsesGauge(champion))
        {
            agGain = AgGateWeight * (MobaBlink.RegenPerSecond(champion) / MobaBlink.RegenPerSecondFor(Array.Empty<int>()) - 1.0);
        }

        // (8) MOVEMENT (mount of the Dark Lord, wings): position gate, a part of the fights is won or lost by kiting. Counted as life.
        var speedGain = 0.0;
        if (attributes is not null)
        {
            speedGain = MoveSpeedWeight * Math.Max(0.0, (attributes[Stats.MovementSpeed] / BaseMoveSpeed) - 1.0);
        }

        var offense = GainOffense(sdOffense + manaGain + antiHeal + petDamage);
        var life = GainLife(sdLife + sustain + petLife + agGain + speedGain);
        return new Extra(offense, life, 0.0);
    }

    /// <summary>
    /// The hit-rate ratio of a champion over the one of its weapon (1 = none): the attack speed above the speed of the weapon, from pieces, pets, auras and the
    /// masteries (1 % more hits per point). It is a LIVE channel: it multiplies every hit by the same rule for everyone, so it needs no pool term; the bots (which hit
    /// on a timer) use it to shorten their interval so that they hit like a player whose client follows the stat.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The ratio, 1..1.7.</returns>
    public static double HitRateRatio(Player champion)
    {
        if (champion.Attributes is not { } a)
        {
            return 1.0;
        }

        var extra = Math.Clamp(a[Stats.AttackSpeedAny] - a[Stats.AttackSpeedByWeapon], 0f, MobaItemCaps.MaxAttackSpeed);
        return 1.0 + (extra / 100.0);
    }

    /// <summary>
    /// What a champion's damage taken is worth in durability pool units: the pool lowers the damage taken (and the mitigation) by <c>exp(-0.465 * x)</c> at once
    /// (0.5 of the pool goes to the factor on the damage taken, 0.5 to the mitigation with the magic share).
    /// </summary>
    /// <param name="reduction">The wanted reduction of the damage taken (0.05 = 5 % less).</param>
    /// <returns>The pool units.</returns>
    public static double GainDurability(double reduction) => reduction <= 0 ? 0 : -Math.Log(1.0 - Math.Min(reduction, 0.95)) / DurabilityExponent;

    /// <summary>Exponent of the total damage taken per unit of the durability pool: <c>KD * (phi * (1 + mu) / 2 + (1 - phi))</c> with phi 0.5 and mu 0.72.</summary>
    public const double DurabilityExponent = 0.5 * ((0.5 * 1.72 / 2.0) + 0.5);

    /// <summary>Maximum mana a full mana tree gives, as the fraction a mana-pool trait of the pieces gives (a trait of 0.20 is a fifth more mana).</summary>
    private const double TreeManaPoolAtFull = 0.20;

    /// <summary>Share of the life a full AG regeneration (+100 %) of the Soul Master accessories is worth in escapes (first estimate, to be checked by ablation).</summary>
    private const double AgGateWeight = 0.15;

    /// <summary>Share of the life a +100 % of movement speed would be worth in kiting (first estimate; the mounts give +10 %).</summary>
    private const double MoveSpeedWeight = 0.30;

    /// <summary>The movement speed of a champion with the wings (native unit).</summary>
    private const double BaseMoveSpeed = 15.0;

    /// <summary>Damage of the black Fenrir (the shop says 10 %).</summary>
    private const double FenrirBlackDamage = 0.10;

    /// <summary>Damage of the gold Fenrir (first estimate: half of the black one, it also gives life and mana).</summary>
    private const double FenrirGoldDamage = 0.05;

    /// <summary>Life of the gold Fenrir as a fraction of the life of the level (first estimate).</summary>
    private const double FenrirGoldLife = 0.05;

    private static readonly Dictionary<short, double> PetDamageGain = new()
    {
        [1] = 0.05,    // Imp
        [64] = 0.08,   // Demon
        [123] = 0.04,  // Skeleton
    };

    private static readonly Dictionary<short, double> PetLifeFlat = new()
    {
        [0] = 75,      // Guardian Angel
        [65] = 75,     // Spirit of Guardian
    };

    /// <summary>One declared element of the ledger.</summary>
    /// <param name="Name">The element.</param>
    /// <param name="Channel">Where it is counted.</param>
    /// <param name="Where">Where in the code.</param>
    public readonly record struct Entry(string Name, LedgerChannel Channel, string Where);

    private static readonly Entry[] Declared =
    {
        new("trait:HealthPool", LedgerChannel.Life, "MobaF10.PoolsOf (2.0 x trait)"),
        new("trait:Durability", LedgerChannel.Durability, "MobaF10.PoolsOf (3.0 x trait)"),
        new("trait:ManaPool", LedgerChannel.Gate, "MobaF10Ledger.ExtraOf (mana gate, dry share of the horizon)"),
        new("trait:ManaRegen", LedgerChannel.Gate, "MobaF10Ledger.ExtraOf (mana gate, dry share of the horizon)"),
        new("trait:SpellPower", LedgerChannel.Offense, "MobaF10.PoolsOf (3.0 x trait)"),
        new("trait:HealPower", LedgerChannel.Life, "MobaF10Ledger.ExtraOf (over the life buffs 26 and 234)"),
        new("trait:CritChance", LedgerChannel.Offense, "MobaF10.CritRatio (expected damage of the crit chance)"),
        new("trait:HealthRegen", LedgerChannel.Life, "MobaF10Ledger.ExtraOf (x horizon)"),
        new("trait:Cooldown", LedgerChannel.Offense, "MobaF10.CooldownRatio"),
        new("item:AntiHeal", LedgerChannel.Offense, "MobaF10Ledger.ExtraOf (10 % of the rival sustain)"),
        new("item:CcResist", LedgerChannel.Gate, "MobaCc.ShapeHardCc (shortens every hard control)"),
        new("item:CooldownReduction", LedgerChannel.Offense, "MobaF10.CooldownRatio"),
        new("item:AssistGold", LedgerChannel.OutsideCombat, "economy"),
        new("item:PassiveGold", LedgerChannel.OutsideCombat, "economy"),
        new("item:SdPierce", LedgerChannel.Offense, "MobaF10Ledger.ExtraOf"),
        new("item:SdBypass", LedgerChannel.Offense, "MobaF10Ledger.ExtraOf"),
        new("item:SdGuard", LedgerChannel.Life, "MobaF10Ledger.ExtraOf"),
        new("item:GaugeRegen", LedgerChannel.Gate, "MobaF10Ledger.ExtraOf (AG regeneration of the accessories = more Teleports, counted as life for the Soul Master family)"),
        new("tree:Offense", LedgerChannel.Offense, "MobaF10.PoolsOf"),
        new("tree:Health", LedgerChannel.Life, "MobaF10.PoolsOf"),
        new("tree:Mana", LedgerChannel.Gate, "MobaF10Ledger.ExtraOf (mana gate: tree fraction over the reference tree)"),
        new("tree:Defense", LedgerChannel.Durability, "MobaF10.PoolsOf"),
        new("tree:Crit", LedgerChannel.Offense, "MobaF10.CritRatio (FinalCritChanceOf includes the tree)"),
        new("tree:Recovery", LedgerChannel.OutsideCombat, "native values are out of combat"),
        new("tree:Utility", LedgerChannel.Gate, "mana usage reduction in the mana gate (ManaUsageReduction); attack speed of the masteries in the live hit rate"),
        new("tree:Strengthener", LedgerChannel.Offense, "MobaMasterTree.StrengthenerBonusOf (damage of the skill)"),
        new("tree:Useless", LedgerChannel.OutsideCombat, "meaningless in the MOBA"),
        new("tree:Shield", LedgerChannel.Life, "Aegis barrier (MobaCastEffects), not the pool"),
        new("tree:Durability", LedgerChannel.Durability, "MobaF10.PoolsOf"),
        new("tree:KillRecovery", LedgerChannel.OutsideCombat, "off until MOBA values"),
        new("tree:Unlock", LedgerChannel.Offense, "gives an active skill (Rush): counted by the kit"),
        new("tree:Other", LedgerChannel.OutsideCombat, "not available"),
        new("stat:attackSpeed", LedgerChannel.Live, "HitRateRatio: pieces, pets, aura and masteries shorten the hit interval (bots: MobaBotPlayer speedFactor; players: the client follows the stat)"),
        new("stat:moveSpeed", LedgerChannel.Life, "MobaF10Ledger.ExtraOf (position gate: speed above the 15 of the wings, counted as life)"),
        new("rule:flatDefense400", LedgerChannel.Durability, "AttackableExtensions: under F10 the flat defense of the level is 0 (it only erased small hits); the mitigation is the percent of the pools"),
        new("rule:penetration", LedgerChannel.Offense, "MobaDefense.PenetrationBySkill (kit, absorbed by the class offset)"),
        new("rule:leech", LedgerChannel.Life, "MobaVampAndSpecial (class intrinsic, absorbed by the class offset)"),
        new("pet:utility", LedgerChannel.Offense, "MobaF10Ledger.ExtraOf (Imp, Demon, Skeleton damage; Angel, Spirit life; damage taken stays live; Demon speed in the hit rate)"),
        new("pet:fenrir", LedgerChannel.Offense, "MobaF10Ledger.ExtraOf (black +10 % damage, gold +5 % damage and life; blue less damage taken stays live; speed live)"),
        new("pet:darkLordPets", LedgerChannel.Offense, "MobaF10Ledger.ExtraOf (Earthshake and Raven shares of DarkLordPetShare, the reference has none)"),
        new("aura:command banner", LedgerChannel.Live, "adds attack speed to the allies in range: counted by HitRateRatio like any attack speed"),
        new("buff:ledger", LedgerChannel.Offense, "MobaF10.PoolsOf (buff ledger 2026-11-20)"),
    };

    /// <summary>Gets the declared ledger.</summary>
    public static IReadOnlyList<Entry> Ledger => Declared;

    /// <summary>
    /// The audit: every <see cref="MobaArchetypeTrait"/> and every <see cref="MobaTreeKind"/> of the game must be declared in the ledger; the ones that are not are
    /// "orphans" (an element that exists in the game but is in no channel of the model).
    /// </summary>
    /// <returns>The orphans, and the pending entries (declared but without a weight).</returns>
    public static (IReadOnlyList<string> Orphans, IReadOnlyList<string> Pending) Audit()
    {
        var names = Declared.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var orphans = new List<string>();
        foreach (var trait in Enum.GetValues<MobaArchetypeTrait>())
        {
            if (!names.Contains($"trait:{trait}"))
            {
                orphans.Add($"trait:{trait}");
            }
        }

        foreach (var kind in Enum.GetValues<MobaTreeKind>())
        {
            if (!names.Contains($"tree:{kind}"))
            {
                orphans.Add($"tree:{kind}");
            }
        }

        var pending = Declared.Where(e => e.Channel == LedgerChannel.Pending).Select(e => $"{e.Name} -> {e.Where}").ToList();
        return (orphans, pending);
    }
}
