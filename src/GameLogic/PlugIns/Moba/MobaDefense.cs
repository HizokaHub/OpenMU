// <copyright file="MobaDefense.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// MOBA mitigation: a champion's invested VIT (and later items) buys a PERCENT damage
/// reduction with diminishing returns, and a handful of skills carry armour penetration.
/// Layered on top of <see cref="MobaSkillDamage"/> in <c>CalculateDamageAsync</c>.
/// </summary>
public static class MobaDefense
{
    /// <summary>VIT at which mitigation reaches half of <see cref="MaxMitigation"/> (diminishing-returns constant).</summary>
    private const double VitHalfPoint = 12_000;

    /// <summary>Hard cap on the VIT-derived percent mitigation.</summary>
    private const double MaxMitigation = 0.70;

    /// <summary>Per-skill armour penetration (fraction of the target's mitigation ignored), by Persistence skill number.</summary>
    private static readonly Dictionary<short, double> PenetrationBySkill = new()
    {
        [52] = 0.55,  // Penetration (the whole point of the skill)
        [43] = 0.40,  // Death Stab
        [232] = 0.35, // Strike of Destruction
        [65] = 0.30,  // Electric Spike
        [263] = 0.35, // Dark Side
        [270] = 0.30, // Phoenix Shot
        [42] = 0.20,  // Rageful Blow
    };

    /// <summary>
    /// The fraction of incoming damage a champion mitigates from invested VIT, 0..<see cref="MaxMitigation"/>.
    /// </summary>
    /// <param name="defender">The defending champion.</param>
    /// <returns>The mitigation fraction.</returns>
    public static double MitigationOf(Player defender)
    {
        if (defender.Attributes is not { } a)
        {
            return 0;
        }

        var investedVit = Math.Max(0, a[Stats.TotalVitality] - MobaCloneFactory.BaselineStatValue);
        return MaxMitigation * (investedVit / (investedVit + VitHalfPoint));
    }

    /// <summary>
    /// Applies MOBA mitigation to a raw damage value: reduces it by the defender's VIT
    /// mitigation, minus the casting skill's armour penetration.
    /// </summary>
    /// <param name="rawDamage">The pre-mitigation damage.</param>
    /// <param name="defender">The defending champion.</param>
    /// <param name="skillNumber">The skill number, or 0 for a basic attack.</param>
    /// <returns>The post-mitigation damage (at least 1 if the input was positive).</returns>
    public static int Apply(int rawDamage, Player defender, short skillNumber)
    {
        if (rawDamage <= 0)
        {
            return rawDamage;
        }

        var mitigation = MitigationOf(defender);
        if (skillNumber != 0 && PenetrationBySkill.TryGetValue(skillNumber, out var pen))
        {
            mitigation *= 1.0 - pen;
        }

        return Math.Max(1, (int)(rawDamage * (1.0 - mitigation)));
    }
}
