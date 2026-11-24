// <copyright file="MobaBuffPower.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Attributes;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The buffs whose vanilla effect does not reach the MOBA damage model get a real effect here (2026-10-25): the champion damage comes
/// from <see cref="MobaSkillDamage"/> (not from the S6 weapon / stat formula), so <b>Expansion of Wizardry</b> (minimum wizardry damage)
/// and <b>Increase Critical Damage</b> (the crit bonus of the S6 crit roll, which champions do not use) did nothing.
/// <para>
/// A buff is worth the <b>rank of the skill of its caster</b> at the moment it was cast (a teammate who receives a shared buff has not
/// got the skill), so <see cref="NoteApplied"/> remembers it on the receiver and <see cref="RankOn"/> only answers while the magic
/// effect is still on the receiver.
/// </para>
/// </summary>
public static class MobaBuffPower
{
    /// <summary>Expansion of Wizardry: the skill number.</summary>
    public const short ExpansionOfWizardry = 233;

    /// <summary>Increase Critical Damage (Lord Emperor): the skill number.</summary>
    public const short IncreaseCriticalDamage = 64;

    /// <summary>Increase Block (Rage Fighter): the skill number. It gives crowd-control resistance in a match (the vanilla block only counts against monsters).</summary>
    public const short IncreaseBlock = 268;

    /// <summary>Spell damage bonus of Expansion at rank 1; every rank adds <see cref="ExpansionPerRank"/>.</summary>
    private const double ExpansionBase = 0.08;

    private const double ExpansionPerRank = 0.02;

    /// <summary>Extra crit multiplier of Increase Critical Damage at rank 1 (the crit is 1.75x); every rank adds <see cref="CritPerRank"/>.</summary>
    private const double CritBase = 0.15;

    private const double CritPerRank = 0.05;

    /// <summary>Crowd-control resistance of Increase Block at rank 1; every rank adds <see cref="CcResistPerRank"/>.</summary>
    private const double CcResistBase = 0.10;

    private const double CcResistPerRank = 0.04;

    private static readonly ConditionalWeakTable<Player, ConcurrentDictionary<short, int>> Ranks = new();

    private static readonly ConcurrentDictionary<short, short> EffectNumbers = new();

    private static readonly ConditionalWeakTable<Player, Dictionary<short, SimpleElement>> LifeBoosts = new();

    /// <summary>
    /// Remembers the rank a buff was cast at (called after the buff's magic effect was applied).
    /// </summary>
    /// <param name="caster">The champion who cast it.</param>
    /// <param name="target">The one who received it.</param>
    /// <param name="entry">The caster's skill entry.</param>
    public static void NoteApplied(Player caster, IAttackable target, SkillEntry entry)
    {
        if (!caster.IsMobaClone || target is not Player receiver || entry.Skill is not { } skill)
        {
            return;
        }

        if (skill.MagicEffectDef is { } effect)
        {
            EffectNumbers[(short)skill.Number] = effect.Number;
        }

        Ranks.GetOrCreateValue(receiver)[(short)skill.Number] = Math.Clamp(entry.Level, 1, MobaSkills.SkillLevelCap);
        ApplyLifeBoost(receiver, (short)skill.Number);
    }

    /// <summary>
    /// Gets the rank of a buff that is on the receiver right now.
    /// </summary>
    /// <param name="receiver">The champion.</param>
    /// <param name="skillNumber">The skill number of the buff.</param>
    /// <returns>The rank (1..5), or 0 if the buff is not active.</returns>
    public static int RankOn(Player receiver, short skillNumber)
    {
        if (!Ranks.TryGetValue(receiver, out var ranks)
            || !ranks.TryGetValue(skillNumber, out var rank)
            || !EffectNumbers.TryGetValue(skillNumber, out var effectNumber))
        {
            return 0;
        }

        try
        {
            return receiver.MagicEffectList.ActiveEffects.ContainsKey(effectNumber) ? rank : 0;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>The factor Expansion of Wizardry puts on the damage of the skills of the champion (1 = not active).</summary>
    /// <param name="champion">The attacking champion.</param>
    /// <returns>The factor, 1.08 to 1.16 while the buff lasts.</returns>
    public static double SpellDamageFactorOf(Player champion) => SpellFactorForRank(RankOn(champion, ExpansionOfWizardry));

    /// <summary>The multiplier of a critical hit of the champion: the base plus what Increase Critical Damage adds.</summary>
    /// <param name="champion">The attacking champion.</param>
    /// <returns>The crit multiplier.</returns>
    public static double CritMultiplierOf(Player champion) => MobaCombatStats.CritMultiplier + (MobaF10.Enabled ? 0.0 : CritBonusForRank(RankOn(champion, IncreaseCriticalDamage)));

    // ---- The buff rework of 2026-11-25 (decided with the user): every buff is worth a % that grows with the rank of the skill of its caster, from rank 1 to rank 5. ----

    /// <summary>Soul Barrier: damage taken reduction, 15 % (rank 1) to 25 % (rank 5).</summary>
    public const short SoulBarrier = 16;

    /// <summary>Defense (Blade Knight, Magic Gladiator, Lord Emperor): the skill number.</summary>
    public const short DefenseSkill = 18;

    /// <summary>Greater Defense (High Elf): the skill number.</summary>
    public const short GreaterDefense = 27;

    /// <summary>Greater Damage (High Elf): the skill number.</summary>
    public const short GreaterDamage = 28;

    /// <summary>Swell Life (Blade Knight): the skill number.</summary>
    public const short SwellLife = 48;

    /// <summary>Infinity Arrow (High Elf): the skill number.</summary>
    public const short InfinityArrow = 77;

    /// <summary>Damage Reflection (Summoner): the skill number.</summary>
    public const short DamageReflection = 217;

    /// <summary>Berserker (Summoner): the skill number.</summary>
    public const short Berserker = 218;

    /// <summary>Heal (High Elf): the skill number.</summary>
    public const short HealSkill = 26;

    /// <summary>Ignore Defense (Rage Fighter): the skill number.</summary>
    public const short IgnoreDefense = 266;

    /// <summary>Increase Health (Rage Fighter): the skill number.</summary>
    public const short IncreaseHealth = 267;

    /// <summary>The arrow attacks Infinity Arrow strengthens.</summary>
    private static readonly HashSet<short> ArrowSkills = new() { 24, 51, 52, 235 };

    /// <summary>The maximum life Berserker costs (a share of the maximum life while it lasts).</summary>
    public const double BerserkerHealthCost = 0.25;

    private static double Scale(int rank, double atRank1, double atRank5) => atRank1 + ((atRank5 - atRank1) * (Math.Clamp(rank, 1, MobaSkills.SkillLevelCap) - 1) / 4.0);

    /// <summary>Soul Barrier: share of the damage taken it removes (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.15 to 0.25.</returns>
    public static double SoulBarrierCut(int rank) => rank <= 0 ? 0 : Scale(rank, 0.15, 0.25);

    /// <summary>Defense: share of the damage taken it removes (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.10 to 0.20.</returns>
    public static double DefenseCut(int rank) => rank <= 0 ? 0 : Scale(rank, 0.10, 0.20);

    /// <summary>Greater Defense: share of the damage taken it removes (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.08 to 0.16.</returns>
    public static double GreaterDefenseCut(int rank) => rank <= 0 ? 0 : Scale(rank, 0.08, 0.16);

    /// <summary>Greater Damage: extra damage (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.06 to 0.14.</returns>
    public static double GreaterDamageBonus(int rank) => rank <= 0 ? 0 : Scale(rank, 0.06, 0.14);

    /// <summary>Berserker: extra damage (0 when not active); it costs <see cref="BerserkerHealthCost"/> of the maximum life.</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.20 to 0.40.</returns>
    public static double BerserkerBonus(int rank) => rank <= 0 ? 0 : Scale(rank, 0.20, 0.40);

    /// <summary>Infinity Arrow: extra damage of the arrow attacks (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.08 to 0.16.</returns>
    public static double InfinityArrowBonus(int rank) => rank <= 0 ? 0 : Scale(rank, 0.08, 0.16);

    /// <summary>Ignore Defense: share of the mitigation of the target the hits skip (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.10 to 0.20 (lowered from 0.20 - 0.40 on 2026-11-25: against a tank at 60 % mitigation the old top end was +60 % damage).</returns>
    public static double IgnoreDefenseShare(int rank) => rank <= 0 ? 0 : Scale(rank, 0.10, 0.20);

    /// <summary>Damage Reflection: share of the damage taken that goes back to the attacker (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.15 to 0.30.</returns>
    public static double ReflectShare(int rank) => rank <= 0 ? 0 : Scale(rank, 0.15, 0.30);

    /// <summary>Swell Life and Increase Health: extra maximum life (0 when not active).</summary>
    /// <param name="rank">The rank on the receiver.</param>
    /// <returns>0.12 to 0.24.</returns>
    public static double LifeBoostShare(int rank) => rank <= 0 ? 0 : Scale(rank, 0.12, 0.24);

    /// <summary>Heal: share of the maximum life of the target it gives back at a rank.</summary>
    /// <param name="rank">The rank of the skill (1-5).</param>
    /// <returns>0.15 to 0.30.</returns>
    public static double HealShare(int rank) => Scale(Math.Max(1, rank), 0.15, 0.30);

    /// <summary>The factor the buffs on the champion put on the damage it takes (1 = none). Defense always counts; Soul Barrier and Greater Defense only while F10 is off (its ledger carries them).</summary>
    /// <param name="defender">The defending champion.</param>
    /// <returns>The factor.</returns>
    public static double DamageTakenFactorOf(Player defender)
    {
        var factor = 1.0 - DefenseCut(RankOn(defender, DefenseSkill));
        if (!MobaF10.Enabled)
        {
            factor *= (1.0 - SoulBarrierCut(RankOn(defender, SoulBarrier))) * (1.0 - GreaterDefenseCut(RankOn(defender, GreaterDefense)));
        }

        return factor;
    }

    /// <summary>The factor the buffs on the champion put on the damage of its skill (1 = none); under F10 its ledger carries them.</summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <param name="skillNumber">The skill number, 0 for a basic attack.</param>
    /// <returns>The factor.</returns>
    public static double DamageDealtFactorOf(Player attacker, short skillNumber)
    {
        if (MobaF10.Enabled)
        {
            return 1.0;
        }

        var factor = (1.0 + GreaterDamageBonus(RankOn(attacker, GreaterDamage))) * (1.0 + BerserkerBonus(RankOn(attacker, Berserker)));
        if (ArrowSkills.Contains(skillNumber))
        {
            factor *= 1.0 + InfinityArrowBonus(RankOn(attacker, InfinityArrow));
        }

        return factor;
    }

    /// <summary>The share of the damage taken that Damage Reflection sends back right now.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The share (0 when not active).</returns>
    public static double ReflectOf(Player champion) => ReflectShare(RankOn(champion, DamageReflection));

    /// <summary>The share of the mitigation of the target the hits of the champion skip right now (Ignore Defense).</summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <returns>The share (0 when not active or under F10).</returns>
    public static double IgnoredMitigationOf(Player attacker) => MobaF10.Enabled ? 0 : IgnoreDefenseShare(RankOn(attacker, IgnoreDefense));

    /// <summary>
    /// The life buffs (Swell Life, Increase Health) add the share to the maximum (and the current) life while their effect lasts; the removal is checked every second.
    /// </summary>
    /// <param name="receiver">The champion who got the buff.</param>
    /// <param name="skillNumber">The skill number of the buff.</param>
    public static void ApplyLifeBoost(Player receiver, short skillNumber)
    {
        if (MobaF10.Enabled || skillNumber is not (SwellLife or IncreaseHealth) || receiver.Attributes is not { } a || !EffectNumbers.TryGetValue(skillNumber, out var effectNumber))
        {
            return;
        }

        var holds = LifeBoosts.GetOrCreateValue(receiver);
        lock (holds)
        {
            if (holds.TryGetValue(skillNumber, out var previous))
            {
                a.RemoveElement(previous, Stats.MaximumHealth);
                holds.Remove(skillNumber);
            }

            var amount = (float)(a[Stats.MaximumHealth] * LifeBoostShare(RankOn(receiver, skillNumber)));
            if (amount <= 0)
            {
                return;
            }

            var element = new SimpleElement(amount, AggregateType.AddRaw);
            a.AddElement(element, Stats.MaximumHealth);
            a[Stats.CurrentHealth] = Math.Min(a[Stats.MaximumHealth], a[Stats.CurrentHealth] + amount);
            holds[skillNumber] = element;
            _ = ReleaseLifeBoostAsync(receiver, skillNumber, effectNumber, element);
        }
    }

    /// <summary>The extra maximum life the life buffs (Swell Life, Increase Health) grant right now, on top of the level curve: the +20 % cap of <see cref="MobaItemCaps"/> does not eat it.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The extra life, 0 if none.</returns>
    public static float TemporaryLifeOf(Player champion)
    {
        if (!LifeBoosts.TryGetValue(champion, out var holds))
        {
            return 0f;
        }

        lock (holds)
        {
            return holds.Values.Sum(e => e.Value);
        }
    }

    private static async Task ReleaseLifeBoostAsync(Player receiver, short skillNumber, short effectNumber, SimpleElement element)
    {
        var holds = LifeBoosts.GetOrCreateValue(receiver);
        while (true)
        {
            await Task.Delay(1000).ConfigureAwait(false);
            lock (holds)
            {
                if (!holds.TryGetValue(skillNumber, out var current) || !ReferenceEquals(current, element))
                {
                    return;   // replaced by a newer cast
                }

                bool active;
                try
                {
                    active = receiver.MagicEffectList.ActiveEffects.ContainsKey(effectNumber);
                }
                catch (InvalidOperationException)
                {
                    active = true;
                }

                if (active && receiver.IsAlive)
                {
                    continue;
                }

                if (receiver.Attributes is { } a)
                {
                    a.RemoveElement(element, Stats.MaximumHealth);
                    a[Stats.CurrentHealth] = Math.Min(a[Stats.CurrentHealth], a[Stats.MaximumHealth]);
                }

                holds.Remove(skillNumber);
                return;
            }
        }
    }

    /// <summary>The Recovery skill (the High Elf's): it gives back a share of the MAXIMUM shield of the target.</summary>
    public const short RecoverySkill = 234;

    /// <summary>
    /// The share of the maximum shield that Recovery gives back at a rank (2026-11-25): 50 % at rank 1 up to 80 % at rank 5. It used to give <c>level + energy / 4</c> (525 points, 2-3 % of
    /// the shield). A shield point saves exactly one life point (90 % of a hit goes to the shield and takes 90 % out of the damage it would do to life), so 80 % is about 27 % of the
    /// maximum life; with the cooldown of 14 s down to 9 s and the strong hitters taking the whole shield in 2-4 s, it is a panic button, not a way to stand still.
    /// </summary>
    /// <param name="rank">The rank of the skill (1-5).</param>
    /// <returns>The share (0.50 to 0.80).</returns>
    public static double RecoveryShareForRank(int rank) => 0.50 + (0.075 * (Math.Clamp(rank, 1, MobaSkills.SkillLevelCap) - 1));

    /// <summary>The spell damage factor of Expansion of Wizardry at a rank.</summary>
    /// <param name="rank">The rank of the buff, 0 when it is not active.</param>
    /// <returns>1 when not active, else 1.08 (rank 1) to 1.16 (rank 5).</returns>
    public static double SpellFactorForRank(int rank) => rank <= 0 ? 1.0 : 1.0 + ExpansionBase + (ExpansionPerRank * (Math.Min(rank, MobaSkills.SkillLevelCap) - 1));

    /// <summary>The extra crit multiplier of Increase Critical Damage at a rank.</summary>
    /// <param name="rank">The rank of the buff, 0 when it is not active.</param>
    /// <returns>0 when not active, else 0.15 (rank 1) to 0.35 (rank 5).</returns>
    public static double CritBonusForRank(int rank) => rank <= 0 ? 0.0 : CritBase + (CritPerRank * (Math.Min(rank, MobaSkills.SkillLevelCap) - 1));

    /// <summary>The crowd-control resistance Increase Block gives the champion right now.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0 when not active, else 0.10 (rank 1) to 0.26 (rank 5).</returns>
    public static double CcResistBonusOf(Player champion) => CcResistForRank(RankOn(champion, IncreaseBlock));

    /// <summary>The crowd-control resistance of Increase Block at a rank.</summary>
    /// <param name="rank">The rank of the buff, 0 when it is not active.</param>
    /// <returns>0 when not active, else 0.10 (rank 1) to 0.26 (rank 5).</returns>
    public static double CcResistForRank(int rank) => rank <= 0 ? 0.0 : CcResistBase + (CcResistPerRank * (Math.Min(rank, MobaSkills.SkillLevelCap) - 1));
}
