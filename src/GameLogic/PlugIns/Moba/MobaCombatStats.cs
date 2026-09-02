// <copyright file="MobaCombatStats.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// LoL-style per-champion combat derived stats for the MOBA mode - critical strike,
/// life steal / spell vamp, class attack range, and the special-damage (true / %HP)
/// table. All first-pass and tunable; items feed the same knobs later.
/// </summary>
public static class MobaCombatStats
{
    // --- Critical strike (from invested AGI) ---
    private const double CritChanceAtMaxAgi = 0.60;
    private const double CritAgiHalfPoint = 14_000;

    /// <summary>The extra damage multiplier on a critical hit.</summary>
    public const double CritMultiplier = 1.75;

    /// <summary>Critical-strike chance (0..1) for a champion, scaling with invested AGI.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The crit chance.</returns>
    public static double CritChanceOf(Player champion)
    {
        if (champion.Attributes is not { } a)
        {
            return 0;
        }

        var investedAgi = Math.Max(0, a[Stats.TotalAgility] - MobaCloneFactory.BaselineStatValue);
        return CritChanceAtMaxAgi * (investedAgi / (investedAgi + CritAgiHalfPoint));
    }

    // --- Life steal / spell vamp ---

    /// <summary>Fraction of damage dealt that heals the attacker (skills heal at a third of this).</summary>
    /// <param name="champion">The attacking champion.</param>
    /// <param name="isSkill">Whether the hit is a skill (true) or a basic attack (false).</param>
    /// <returns>The heal fraction.</returns>
    public static double VampOf(Player champion, bool isSkill)
    {
        var family = MobaPassives.FamilyOf(champion);
        var basic = family switch
        {
            MobaFamily.Knight or MobaFamily.RageFighter => 0.14,  // bruisers sustain in melee
            MobaFamily.Elf => 0.12,
            MobaFamily.MagicGladiator or MobaFamily.DarkLord => 0.10,
            _ => 0.08, // pure casters: mostly spell vamp
        };

        return isSkill ? basic * 0.35 : basic;
    }

    // --- Attack range by class family (tiles) ---

    /// <summary>The champion's basic-attack range in tiles - ranged classes actually kite.</summary>
    /// <param name="family">The champion family.</param>
    /// <returns>The attack range in tiles.</returns>
    public static int AttackRangeOf(MobaFamily family) => family switch
    {
        MobaFamily.Elf => 6,
        MobaFamily.Wizard or MobaFamily.Summoner => 6,
        MobaFamily.MagicGladiator => 3,
        MobaFamily.DarkLord => 4,
        _ => 2, // Knight, RageFighter
    };

    // --- Special damage: true and % max/current HP (anti-tank identity) ---

    /// <summary>Special-damage rule for a skill: bonus true damage as a fraction of the target's max / current HP.</summary>
    /// <param name="skillNumber">Persistence skill number.</param>
    /// <returns>(maxHpFraction, currentHpFraction) - both 0 if the skill has no special component.</returns>
    public static (double MaxHp, double CurrentHp) SpecialDamageOf(short skillNumber) => skillNumber switch
    {
        43 => (0.06, 0.0),   // Death Stab - carves max HP
        232 => (0.05, 0.0),  // Strike of Destruction
        264 => (0.0, 0.09),  // Dragon Roar - % current HP
        265 => (0.0, 0.07),  // Dragon Slasher
        260 => (0.0, 0.05),  // Killing Blow
        9 => (0.04, 0.0),    // Evil Spirit
        _ => (0.0, 0.0),
    };
}
