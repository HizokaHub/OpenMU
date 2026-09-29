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
    // --- Critical strike (from invested AGI, blended with item Luck) ---
    private const double CritChanceAtMaxAgi = 0.60;
    private const double CritAgiHalfPoint = 14_000;

    /// <summary>The extra damage multiplier on a critical hit.</summary>
    public const double CritMultiplier = 1.75;

    /// <summary>Weight of the AGI term ("A") in <see cref="FinalCritChanceOf"/> against the item Luck term ("B").</summary>
    private const double CritStatWeight = 0.60;

    /// <summary>Hard cap on the final (stat + item) crit chance, so stacked Luck can't run away.</summary>
    private const double MaxCritChance = 0.60;

    /// <summary>Hard cap on the "excellent hit" (x1.2 damage) roll for MOBA champions - see <see cref="AttackableExtensions"/>.</summary>
    public const double MaxExcellentHitChance = 0.50;

    /// <summary>
    /// Critical-strike chance (0..1) for a champion from invested AGI alone. This is the
    /// stat-build term ("A") blended in <see cref="FinalCritChanceOf"/> - use that one for
    /// the actual combat roll.
    /// </summary>
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

    /// <summary>
    /// The champion's final crit chance: the AGI-only term ("A", <see cref="CritChanceOf"/>)
    /// blended with the item Luck term ("B", the aggregated <see cref="Stats.CriticalDamageChance"/>
    /// from equipped Luck options), weighted <see cref="CritStatWeight"/> stats /
    /// <c>1 - CritStatWeight</c> items, capped at <see cref="MaxCritChance"/>.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The final crit chance, 0..<see cref="MaxCritChance"/>.</returns>
    public static double FinalCritChanceOf(Player champion)
    {
        var fromStats = CritChanceOf(champion);
        var fromItems = champion.Attributes?[Stats.CriticalDamageChance] ?? 0;
        return Math.Clamp((CritStatWeight * fromStats) + ((1.0 - CritStatWeight) * fromItems), 0.0, MaxCritChance);
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
