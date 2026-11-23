// <copyright file="MobaF10Ledger.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

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

        // (1) MANA: the pool and the regeneration move the share of the fight spent dry (only what depends on mana).
        var manaPool = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.ManaPool);
        var manaRegen = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.ManaRegen);
        var manaGain = ManaDependence(family) * DrySharePerHorizon(horizon) * ((0.6 * manaPool) + (0.8 * manaRegen));

        // (2) COOLDOWN is already in DamageFactor (CooldownRatio); crit in CritRatio; spell power in the base pool.

        // (3) SD (a third of the life): the weapon pierces it, the wings skip it, the armour makes it absorb more. The victim's shield is a quarter of its pool.
        const double SdShare = 0.25;
        var sdOffense = (MobaItemTraits.SdPierceOf(champion) * 0.9 * SdShare) + (MobaItemTraits.SdBypassOf(champion) * SdShare);
        var sdLife = MobaItemTraits.SdGuardOf(champion) * SdShare;

        // (4) SUSTAIN over the horizon: regeneration (fraction of the life per second), leech (a part of the damage dealt: ~ the life of the rival
        // by the end of a won fight, half on average) and the heal power over the life buff the champion carries.
        var regen = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.HealthRegen) * horizon;
        var healPower = MobaArchetype.TotalOf(champion, MobaArchetypeTrait.HealPower);
        var healBuffs = 0.1 * ((MobaBuffPower.RankOn(champion, 26) / 5.0) + (MobaBuffPower.RankOn(champion, 234) / 5.0));
        var sustain = regen + (healPower * healBuffs);

        // anti-heal of the weapon: cuts the sustain the rival would have (the reference sustain of a rival is ~10 % of its life).
        var antiHeal = MobaItemTraits.AntiHealOf(champion) * 0.10;

        var offense = GainOffense(sdOffense + manaGain + antiHeal);
        var life = GainLife(sdLife + sustain);
        return new Extra(offense, life, 0.0);
    }

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
        new("item:GaugeRegen", LedgerChannel.Pending, "AG regeneration of the accessories (pays the Teleport of the Soul Master): needs the AG gate"),
        new("tree:Offense", LedgerChannel.Offense, "MobaF10.PoolsOf"),
        new("tree:Health", LedgerChannel.Life, "MobaF10.PoolsOf"),
        new("tree:Mana", LedgerChannel.Pending, "tree nodes of maximum mana: not yet in the mana gate"),
        new("tree:Defense", LedgerChannel.Durability, "MobaF10.PoolsOf"),
        new("tree:Crit", LedgerChannel.Offense, "MobaF10.CritRatio (FinalCritChanceOf includes the tree)"),
        new("tree:Recovery", LedgerChannel.OutsideCombat, "native values are out of combat"),
        new("tree:Utility", LedgerChannel.Pending, "mana usage reduction and attack speed of the masteries: needs the speed channel"),
        new("tree:Strengthener", LedgerChannel.Offense, "MobaMasterTree.StrengthenerBonusOf (damage of the skill)"),
        new("tree:Useless", LedgerChannel.OutsideCombat, "meaningless in the MOBA"),
        new("tree:Shield", LedgerChannel.Life, "Aegis barrier (MobaCastEffects), not the pool"),
        new("tree:Durability", LedgerChannel.Durability, "MobaF10.PoolsOf"),
        new("tree:KillRecovery", LedgerChannel.OutsideCombat, "off until MOBA values"),
        new("tree:Unlock", LedgerChannel.Offense, "gives an active skill (Rush): counted by the kit"),
        new("tree:Other", LedgerChannel.OutsideCombat, "not available"),
        new("stat:attackSpeed", LedgerChannel.Pending, "attack speed (stats, pets, auras): hits per second; needs the reference speed per class"),
        new("stat:moveSpeed", LedgerChannel.Pending, "movement speed: position gate"),
        new("rule:flatDefense400", LedgerChannel.Pending, "flat defense of 400 per hit before the factor: still a flat subtraction"),
        new("rule:penetration", LedgerChannel.Offense, "MobaDefense.PenetrationBySkill (kit, absorbed by the class offset)"),
        new("rule:leech", LedgerChannel.Life, "MobaVampAndSpecial (class intrinsic, absorbed by the class offset)"),
        new("pet:pets", LedgerChannel.Pending, "pets from the inventory: attack speed and stats; the Dark Raven/Horse are the kit of the Dark Lord"),
        new("aura:command banner", LedgerChannel.Pending, "attack speed of the allies in range: team channel"),
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
