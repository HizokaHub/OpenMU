// <copyright file="MobaCooldowns.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Per-match skill cooldowns for the MOBA mode. Season 6 skills carry no cooldown of
/// their own, so a champion's abilities would otherwise be spammable.
/// <para>
/// Like League of Legends, every skill rank (1..5) has its own authored cooldown rather
/// than a uniform percentage: pokes stay almost flat, burst / AoE / hard-CC and the
/// sustain buffs drop more as they are ranked up. These are first-pass numbers meant to
/// be edited during the balance pass.
/// </para>
/// </summary>
public static class MobaCooldowns
{
    /// <summary>Cooldown per rank (seconds, index 0 = rank 1) for a skill with no explicit entry.</summary>
    private static readonly double[] DefaultPerRank = { 6.5, 6.0, 5.5, 5.0, 4.5 };

    /// <summary>
    /// Per-skill cooldown by rank, keyed by Persistence skill number. Only the abilities a
    /// champion can actually be given (see <see cref="MobaLoadouts"/>) are listed; anything
    /// else falls back to <see cref="DefaultPerRank"/>.
    /// </summary>
    private static readonly Dictionary<short, double[]> PerRankSeconds = new()
    {
        // --- Pokes / primary spam abilities: near-flat, tiny drop ---
        [17] = new[] { 2.0, 1.9, 1.8, 1.7, 1.5 },   // Energy Ball
        [11] = new[] { 4.0, 3.75, 3.5, 3.25, 3.0 }, // Power Wave
        [19] = new[] { 2.5, 2.3, 2.1, 1.9, 1.7 },   // Falling Slash
        [23] = new[] { 3.0, 2.75, 2.5, 2.25, 2.0 }, // Slash
        [24] = new[] { 2.0, 1.9, 1.8, 1.7, 1.6 },   // Triple Shot
        [60] = new[] { 2.5, 2.4, 2.3, 2.2, 2.0 },   // Force

        // --- Gap-closer ---
        [20] = new[] { 5.0, 4.5, 4.0, 3.5, 3.0 },   // Lunge

        // --- Mid-cost nukes ---
        [4] = new[] { 6.0, 5.5, 5.0, 4.5, 4.0 },    // Fire Ball
        [3] = new[] { 7.0, 6.5, 6.0, 5.5, 5.0 },    // Lightning
        [21] = new[] { 8.0, 7.5, 7.0, 6.5, 6.0 },   // Uppercut (knock-up)
        [52] = new[] { 6.0, 5.5, 5.0, 4.5, 4.0 },   // Penetration
        [74] = new[] { 8.0, 7.25, 6.5, 5.75, 5.0 }, // Fire Blast

        // --- AoE / hard CC / heavy hitters: bigger payoff for ranking ---
        [22] = new[] { 7.0, 6.5, 6.0, 5.5, 5.0 },   // Cyclone
        [41] = new[] { 6.0, 5.5, 5.0, 4.5, 4.0 },   // Twisting Slash (spin AoE)
        [7] = new[] { 9.0, 8.25, 7.5, 6.75, 6.0 },  // Ice (slow AoE)
        [9] = new[] { 8.0, 7.5, 7.0, 6.5, 6.0 },    // Evil Spirit
        [46] = new[] { 9.0, 8.0, 7.0, 6.0, 5.0 },   // Starfall (big burst)
        [62] = new[] { 10.0, 9.0, 8.0, 7.0, 6.0 },  // Earthshake
        [214] = new[] { 8.0, 7.5, 7.0, 6.5, 6.0 },  // Drain Life

        // --- Sustain / buffs: long, and shrink noticeably as ranked ---
        [26] = new[] { 10.0, 9.0, 8.0, 7.0, 6.0 },   // Heal
        [28] = new[] { 14.0, 13.0, 12.0, 11.0, 10.0 }, // Greater Damage
        [27] = new[] { 14.0, 13.0, 12.0, 11.0, 10.0 }, // Greater Defense
    };

    /// <summary>
    /// Gets the cooldown to apply after a champion successfully casts the given skill.
    /// Returns <see cref="TimeSpan.Zero"/> when no per-match cooldown should be tracked
    /// (not a MOBA clone, or the skill is not one of the champion's learned abilities -
    /// e.g. a plain weapon attack).
    /// </summary>
    /// <param name="champion">The casting player.</param>
    /// <param name="skillEntry">The skill entry that was cast.</param>
    /// <returns>The cooldown duration, or <see cref="TimeSpan.Zero"/> for none.</returns>
    public static TimeSpan GetCooldown(Player champion, SkillEntry? skillEntry)
    {
        if (!champion.IsMobaClone
            || skillEntry?.Skill is not { } skill
            || champion.SelectedCharacter is not { } character)
        {
            return TimeSpan.Zero;
        }

        var learned = character.LearnedSkills.FirstOrDefault(s => s.Skill?.Number == skill.Number);
        if (learned is null)
        {
            return TimeSpan.Zero;
        }

        var perRank = PerRankSeconds.TryGetValue((short)skill.Number, out var table) ? table : DefaultPerRank;

        // Skill level runs 1..5 once at least one point is spent; treat 0 as rank 1.
        var rankIndex = Math.Clamp(learned.Level, 1, MobaSkills.SkillLevelCap) - 1;
        return TimeSpan.FromSeconds(perRank[rankIndex]);
    }

    /// <summary>
    /// Checks whether the given skill is currently on cooldown for the champion.
    /// </summary>
    /// <param name="champion">The player.</param>
    /// <param name="skillNumber">The skill number.</param>
    /// <param name="now">The current UTC time.</param>
    /// <returns><c>true</c> if the skill cannot be cast yet.</returns>
    public static bool IsOnCooldown(Player champion, short skillNumber, DateTime now)
    {
        return champion.MobaSkillCooldowns.TryGetValue(skillNumber, out var readyAt) && readyAt > now;
    }
}
