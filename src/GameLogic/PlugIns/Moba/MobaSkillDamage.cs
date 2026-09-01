// <copyright file="MobaSkillDamage.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// MOBA skill damage model (LoL-style). Each skill has a flat base + a per-rank
/// increment; this REPLACES the Season 6 weapon/stat-derived base damage for a champion
/// clone's skill cast (see the hook in <c>AttackableExtensions.CalculateDamageAsync</c>).
/// <para>
/// Purpose: make damage independent of the clone's raw stats, so every class casting the
/// same skill at the same rank deals the same number. Per-class stat scaling ("the race
/// formula") is layered back on top later, deliberately and small.
/// </para>
/// <para>First-pass numbers - the whole point of the bot harness is to tune this table.</para>
/// </summary>
public static class MobaSkillDamage
{
    private const int DefaultBase = 70;

    private const int DefaultPerRank = 18;

    /// <summary>Flat damage for a champion's basic attack (also stat-independent for now).</summary>
    private const int BasicAttackDamage = 45;

    /// <summary>Spread applied around the mid value to get a min/max.</summary>
    private const double Spread = 0.10;

    /// <summary>(base at rank 1, increment per rank) by Persistence skill number.</summary>
    private static readonly Dictionary<short, (int Base, int PerRank)> Table = new()
    {
        // --- pokes / low ---
        [17] = (45, 10),   // Energy Ball
        [11] = (55, 12),   // Power Wave
        [60] = (50, 12),   // Force
        [24] = (38, 9),    // Triple Shot (3 hits)
        [19] = (60, 14),   // Falling Slash
        [23] = (65, 15),   // Slash
        [20] = (70, 16),   // Lunge

        // --- mid ---
        [4] = (80, 20),    // Fire Ball
        [3] = (85, 21),    // Lightning
        [1] = (70, 16),    // Poison (also a DoT)
        [2] = (95, 24),    // Meteorite
        [7] = (85, 20),    // Ice
        [9] = (95, 24),    // Evil Spirit
        [22] = (90, 22),   // Cyclone
        [41] = (85, 20),   // Twisting Slash
        [21] = (100, 25),  // Uppercut
        [39] = (100, 24),  // Ice Storm
        [52] = (95, 24),   // Penetration
        [55] = (90, 22),   // Fire Slash
        [56] = (85, 20),   // Power Slash
        [61] = (100, 26),  // Fire Burst
        [66] = (95, 24),   // Force Wave
        [65] = (110, 28),  // Electric Spike
        [214] = (80, 20),  // Drain Life
        [215] = (85, 20),  // Chain Lightning
        [216] = (90, 22),  // Lightning Orb
        [235] = (110, 26), // Multi-Shot

        // --- heavy hitters ---
        [42] = (115, 28),  // Rageful Blow
        [43] = (125, 30),  // Death Stab
        [232] = (135, 32), // Strike of Destruction
        [46] = (140, 34),  // Starfall
        [51] = (135, 32),  // Ice Arrow
        [260] = (120, 30), // Killing Blow
        [261] = (110, 28), // Beast Uppercut
        [263] = (135, 33), // Dark Side
        [264] = (100, 24), // Dragon Roar
        [265] = (100, 24), // Dragon Slasher
        [270] = (110, 28), // Phoenix Shot
    };

    /// <summary>Gets the min/max MOBA base damage for a skill at a rank (1..5).</summary>
    /// <param name="skillNumber">Persistence skill number.</param>
    /// <param name="rank">The champion skill rank (0 is treated as 1).</param>
    /// <param name="min">Output minimum.</param>
    /// <param name="max">Output maximum.</param>
    public static void GetSkillBaseDamage(short skillNumber, int rank, out int min, out int max)
    {
        var (baseDamage, perRank) = Table.TryGetValue(skillNumber, out var entry)
            ? entry
            : (DefaultBase, DefaultPerRank);

        var r = Math.Clamp(rank, 1, 5);
        var mid = baseDamage + (perRank * (r - 1));
        min = (int)(mid * (1.0 - Spread));
        max = (int)(mid * (1.0 + Spread));
    }

    /// <summary>Gets the min/max MOBA base damage for a champion's basic attack.</summary>
    /// <param name="min">Output minimum.</param>
    /// <param name="max">Output maximum.</param>
    public static void GetBasicAttackDamage(out int min, out int max)
    {
        min = (int)(BasicAttackDamage * (1.0 - Spread));
        max = (int)(BasicAttackDamage * (1.0 + Spread));
    }
}
