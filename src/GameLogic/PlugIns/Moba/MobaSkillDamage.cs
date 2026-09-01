// <copyright file="MobaSkillDamage.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// MOBA skill damage model (LoL-style, hybrid A + B).
/// <para>
/// <b>A - flat table:</b> every skill has a base (rank 1) + a per-rank increment. This is
/// the bulk of the damage and is completely independent of the champion's stats, so two
/// classes casting the same skill at the same rank with 0 invested points hit identically.
/// </para>
/// <para>
/// <b>B - per-class stat scaling:</b> a small term <c>primaryStat * skillStatRatio</c> is
/// added on top. The primary stat is the class's own (STR for Knight/RF, ENE for the
/// casters, AGI for the Elf, CMD for the Dark Lord), so builds differ - a maxed
/// glass-cannon hits noticeably harder - without any class spiking from an off-stat.
/// </para>
/// <para>First-pass numbers - tuned with the bot harness.</para>
/// </summary>
public static class MobaSkillDamage
{
    private const int DefaultBase = 70;

    private const int DefaultPerRank = 18;

    private const double DefaultStatRatio = 0.04;

    /// <summary>Flat base + spread for a champion's basic attack.</summary>
    private const int BasicAttackDamage = 45;

    private const double BasicAttackStatRatio = 0.02;

    /// <summary>Spread applied around the mid value to get a min/max.</summary>
    private const double Spread = 0.10;

    /// <summary>(base at rank 1, increment per rank, stat ratio) by Persistence skill number.</summary>
    private static readonly Dictionary<short, (int Base, int PerRank, double StatRatio)> Table = new()
    {
        // --- pokes / low: low stat ratio ---
        [17] = (45, 10, 0.02),   // Energy Ball
        [11] = (55, 12, 0.03),   // Power Wave
        [60] = (50, 12, 0.03),   // Force
        [24] = (38, 9, 0.02),    // Triple Shot (3 hits)
        [19] = (60, 14, 0.03),   // Falling Slash
        [23] = (65, 15, 0.03),   // Slash
        [20] = (70, 16, 0.03),   // Lunge

        // --- mid ---
        [4] = (80, 20, 0.05),    // Fire Ball
        [3] = (85, 21, 0.05),    // Lightning
        [1] = (70, 16, 0.04),    // Poison (also a DoT)
        [2] = (95, 24, 0.05),    // Meteorite
        [7] = (85, 20, 0.045),   // Ice
        [9] = (95, 24, 0.05),    // Evil Spirit
        [22] = (90, 22, 0.045),  // Cyclone
        [41] = (85, 20, 0.04),   // Twisting Slash
        [21] = (100, 25, 0.05),  // Uppercut
        [39] = (100, 24, 0.05),  // Ice Storm
        [52] = (95, 24, 0.05),   // Penetration
        [55] = (90, 22, 0.045),  // Fire Slash
        [56] = (85, 20, 0.04),   // Power Slash
        [61] = (100, 26, 0.05),  // Fire Burst
        [66] = (95, 24, 0.05),   // Force Wave
        [65] = (110, 28, 0.055), // Electric Spike
        [214] = (80, 20, 0.04),  // Drain Life
        [215] = (85, 20, 0.04),  // Chain Lightning
        [216] = (90, 22, 0.045), // Lightning Orb
        [235] = (110, 26, 0.05), // Multi-Shot

        // --- heavy hitters: bigger stat ratio (reward investment) ---
        [42] = (115, 28, 0.06),  // Rageful Blow
        [43] = (125, 30, 0.065), // Death Stab
        [232] = (135, 32, 0.07), // Strike of Destruction
        [46] = (140, 34, 0.07),  // Starfall
        [51] = (135, 32, 0.065), // Ice Arrow
        [260] = (120, 30, 0.06), // Killing Blow
        [261] = (110, 28, 0.055),// Beast Uppercut
        [263] = (135, 33, 0.07), // Dark Side
        [264] = (100, 24, 0.05), // Dragon Roar
        [265] = (100, 24, 0.05), // Dragon Slasher
        [270] = (110, 28, 0.055),// Phoenix Shot
    };

    /// <summary>Gets the min/max MOBA damage for a champion's skill at a rank (1..5).</summary>
    /// <param name="champion">The casting champion (for the per-class stat term).</param>
    /// <param name="skillNumber">Persistence skill number.</param>
    /// <param name="rank">The champion skill rank (0 is treated as 1).</param>
    /// <param name="min">Output minimum.</param>
    /// <param name="max">Output maximum.</param>
    public static void GetSkillBaseDamage(Player champion, short skillNumber, int rank, out int min, out int max)
    {
        var (baseDamage, perRank, statRatio) = Table.TryGetValue(skillNumber, out var entry)
            ? entry
            : (DefaultBase, DefaultPerRank, DefaultStatRatio);

        var r = Math.Clamp(rank, 1, 5);
        var flat = baseDamage + (perRank * (r - 1));
        Spread2(flat + StatTerm(champion, statRatio), out min, out max);
    }

    /// <summary>Gets the min/max MOBA damage for a champion's basic attack.</summary>
    /// <param name="champion">The attacking champion.</param>
    /// <param name="min">Output minimum.</param>
    /// <param name="max">Output maximum.</param>
    public static void GetBasicAttackDamage(Player champion, out int min, out int max)
    {
        Spread2(BasicAttackDamage + StatTerm(champion, BasicAttackStatRatio), out min, out max);
    }

    private static double StatTerm(Player champion, double ratio)
    {
        if (champion.Attributes is not { } attributes)
        {
            return 0;
        }

        var stat = MobaPassives.FamilyOf(champion) switch
        {
            MobaFamily.Knight or MobaFamily.RageFighter => Stats.TotalStrength,
            MobaFamily.Elf => Stats.TotalAgility,
            MobaFamily.DarkLord => Stats.TotalLeadership,
            _ => Stats.TotalEnergy, // Wizard, MagicGladiator, Summoner
        };

        return attributes[stat] * ratio;
    }

    private static void Spread2(double mid, out int min, out int max)
    {
        min = (int)(mid * (1.0 - Spread));
        max = (int)(mid * (1.0 + Spread));
    }
}
