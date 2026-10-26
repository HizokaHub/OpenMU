// <copyright file="MobaBuffPower.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MUnique.OpenMU.DataModel.Entities;

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
    public static double CritMultiplierOf(Player champion) => MobaCombatStats.CritMultiplier + CritBonusForRank(RankOn(champion, IncreaseCriticalDamage));

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
