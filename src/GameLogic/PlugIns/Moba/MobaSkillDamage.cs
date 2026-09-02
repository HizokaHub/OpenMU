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
/// <b>B - per-race stat scaling:</b> on top of the flat value, a skill hits up to
/// <c>MaxStatBonus</c> harder (a fraction of its flat damage) as the caster's PRIMARY stat
/// goes from the flat clone baseline to fully maxed (<see cref="MobaStatEconomy.MaxPerStat"/>
/// invested). The primary stat is the class's own - STR for Knight/RF, ENE for the casters,
/// AGI for the Elf, CMD for the Dark Lord - so an off-stat never spikes a class, and the
/// scaling is expressed as "how much this skill rewards a maxed build" rather than a raw
/// coefficient that would explode at a 30k stat scale.
/// </para>
/// <para>
/// <c>finalMid = flat * (1 + MaxStatBonus * investedFraction)</c>, then a +/-10% spread.
/// </para>
/// <para>First-pass numbers - tuned with the bot / dummy harness.</para>
/// </summary>
public static class MobaSkillDamage
{
    private const int DefaultBase = 70;

    private const int DefaultPerRank = 18;

    /// <summary>Default "extra fraction of flat damage at a fully-maxed primary stat".</summary>
    private const double DefaultMaxStatBonus = 0.50;

    /// <summary>
    /// Global tuning knobs. The table below holds RELATIVE weights; these scale the whole
    /// model at once. <see cref="FlatMultiplier"/> scales the stat-independent base (raise
    /// to make skills hit harder vs the 2500 HP pool); <see cref="StatBonusMultiplier"/>
    /// scales how much a maxed primary stat rewards a build (raise to make stat investment
    /// feel decisive).
    /// </summary>
    private const double FlatMultiplier = 1.5;

    private const double StatBonusMultiplier = 3.0;

    /// <summary>Flat base + spread for a champion's basic attack.</summary>
    private const int BasicAttackDamage = 45;

    private const double BasicAttackMaxStatBonus = 0.60;

    /// <summary>Spread applied around the mid value to get a min/max.</summary>
    private const double Spread = 0.10;

    /// <summary>(base at rank 1, increment per rank, extra fraction of flat at a maxed primary stat) by Persistence skill number.</summary>
    private static readonly Dictionary<short, (int Base, int PerRank, double MaxStatBonus)> Table = new()
    {
        // --- pokes / low: light scaling ---
        [17] = (45, 10, 0.35),   // Energy Ball
        [11] = (55, 12, 0.40),   // Power Wave
        [60] = (50, 12, 0.40),   // Force
        [66] = (60, 14, 0.40),   // Force Wave
        [24] = (38, 9, 0.35),    // Triple Shot (per volley)
        [19] = (60, 14, 0.40),   // Falling Slash
        [23] = (65, 15, 0.40),   // Slash
        [20] = (70, 16, 0.40),   // Lunge
        [45] = (55, 13, 0.35),   // Lance
        [8] = (60, 14, 0.40),    // Twister
        [38] = (65, 15, 0.40),   // Decay (also DoT)

        // --- mid ---
        [4] = (80, 20, 0.55),    // Fire Ball
        [3] = (85, 21, 0.55),    // Lightning
        [1] = (70, 16, 0.45),    // Poison (also a DoT)
        [7] = (85, 20, 0.50),    // Ice
        [5] = (85, 21, 0.55),    // Flame
        [9] = (95, 24, 0.55),    // Evil Spirit
        [10] = (95, 24, 0.55),   // Hellfire
        [12] = (90, 22, 0.50),   // Aqua Beam
        [14] = (95, 24, 0.55),   // Inferno
        [22] = (90, 22, 0.50),   // Cyclone
        [41] = (85, 20, 0.45),   // Twisting Slash
        [21] = (100, 25, 0.55),  // Uppercut
        [44] = (95, 23, 0.50),   // Crescent Moon Slash
        [47] = (95, 23, 0.50),   // Impale
        [49] = (95, 23, 0.50),   // Fire Breath
        [55] = (90, 22, 0.50),   // Fire Slash
        [56] = (85, 20, 0.45),   // Power Slash
        [57] = (95, 23, 0.50),   // Spiral Slash
        [61] = (100, 26, 0.55),  // Fire Burst
        [62] = (100, 25, 0.55),  // Earthshake
        [74] = (95, 23, 0.50),   // Fire Blast
        [78] = (100, 25, 0.55),  // Fire Scream
        [214] = (80, 20, 0.45),  // Drain Life
        [215] = (85, 20, 0.45),  // Chain Lightning
        [223] = (95, 24, 0.55),  // Explosion
        [224] = (95, 24, 0.55),  // Requiem
        [225] = (85, 20, 0.45),  // Pollution (also DoT)
        [230] = (95, 24, 0.55),  // Lightning Shock
        [235] = (110, 26, 0.55), // Multi-Shot
        [262] = (100, 25, 0.55), // Chain Drive
        [264] = (100, 24, 0.55), // Dragon Roar
        [265] = (100, 24, 0.55), // Dragon Slasher
        [269] = (90, 22, 0.50),  // Charge

        // --- mid-heavy ---
        [2] = (95, 24, 0.60),    // Meteorite
        [13] = (100, 25, 0.60),  // Cometfall
        [39] = (100, 24, 0.60),  // Ice Storm
        [52] = (95, 24, 0.55),   // Penetration
        [65] = (110, 28, 0.65),  // Electric Spike
        [236] = (105, 26, 0.60), // Flame Strike
        [237] = (110, 28, 0.65), // Gigantic Storm
        [238] = (110, 28, 0.65), // Chaotic Diseier
        [261] = (110, 28, 0.60), // Beast Uppercut
        [270] = (110, 28, 0.60), // Phoenix Shot

        // --- heavy hitters: reward investment most ---
        [40] = (120, 30, 0.80),  // Nova (channelled burst)
        [42] = (115, 28, 0.75),  // Rageful Blow
        [43] = (125, 30, 0.80),  // Death Stab
        [46] = (140, 34, 0.85),  // Starfall
        [51] = (135, 32, 0.80),  // Ice Arrow
        [232] = (135, 32, 0.85), // Strike of Destruction
        [260] = (120, 30, 0.75), // Killing Blow
        [263] = (135, 33, 0.85), // Dark Side
    };

    /// <summary>Gets the min/max MOBA damage for a champion's skill at a rank (1..5).</summary>
    /// <param name="champion">The casting champion (for the per-race stat term).</param>
    /// <param name="skillNumber">Persistence skill number.</param>
    /// <param name="rank">The champion skill rank (0 is treated as 1).</param>
    /// <param name="min">Output minimum.</param>
    /// <param name="max">Output maximum.</param>
    public static void GetSkillBaseDamage(Player champion, short skillNumber, int rank, out int min, out int max)
    {
        var (baseDamage, perRank, maxStatBonus) = Table.TryGetValue(skillNumber, out var entry)
            ? entry
            : (DefaultBase, DefaultPerRank, DefaultMaxStatBonus);

        var r = Math.Clamp(rank, 1, 5);
        var flat = (baseDamage + (perRank * (r - 1))) * FlatMultiplier;
        var bonus = maxStatBonus * StatBonusMultiplier * InvestedFraction(champion);
        Spread2(flat * (1.0 + bonus), out min, out max);
    }

    /// <summary>Gets the min/max MOBA damage for a champion's basic attack.</summary>
    /// <param name="champion">The attacking champion.</param>
    /// <param name="min">Output minimum.</param>
    /// <param name="max">Output maximum.</param>
    public static void GetBasicAttackDamage(Player champion, out int min, out int max)
    {
        var flat = BasicAttackDamage * FlatMultiplier;
        var bonus = BasicAttackMaxStatBonus * StatBonusMultiplier * InvestedFraction(champion);
        Spread2(flat * (1.0 + bonus), out min, out max);
    }

    /// <summary>
    /// How far the champion's PRIMARY stat is from baseline to fully maxed, as 0..1.
    /// Only points invested past the flat clone baseline count, so a fresh clone scales 0.
    /// </summary>
    private static double InvestedFraction(Player champion)
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

        double invested = attributes[stat] - MobaCloneFactory.BaselineStatValue;
        return Math.Clamp(invested / MobaStatEconomy.MaxPerStat, 0.0, 1.0);
    }

    private static void Spread2(double mid, out int min, out int max)
    {
        min = (int)(mid * (1.0 - Spread));
        max = (int)(mid * (1.0 + Spread));
    }
}
