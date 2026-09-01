// <copyright file="MobaCooldowns.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Per-match skill cooldowns for the MOBA mode. Season 6 skills carry no cooldown of
/// their own, so a champion's abilities would otherwise be spammable. Each ability has a
/// base cooldown that shrinks as the skill is levelled up (1..5). The numbers here are a
/// first pass - the balance pass tunes the per-skill table and the reduction curve.
/// </summary>
public static class MobaCooldowns
{
    /// <summary>Cooldown used for an ability with no explicit entry in <see cref="BaseCooldownSeconds"/>.</summary>
    private const double DefaultBaseSeconds = 6.0;

    /// <summary>Fraction of the base cooldown removed per skill level above 1 (level 5 => -32%).</summary>
    private const double ReductionPerLevel = 0.08;

    /// <summary>A skill never drops below this fraction of its base cooldown.</summary>
    private const double MinFraction = 0.5;

    /// <summary>
    /// Per-skill base cooldown (seconds), keyed by Persistence skill number. Only the
    /// abilities that need to differ from <see cref="DefaultBaseSeconds"/> are listed;
    /// everything else a champion can cast uses the default.
    /// </summary>
    private static readonly Dictionary<short, double> BaseCooldownSeconds = new()
    {
        // Wizard
        [17] = 1.5,   // Energy Ball (spammable poke)
        [4] = 5.0,    // Fire Ball
        [3] = 6.0,    // Lightning
        [11] = 4.0,   // Power Wave
        [9] = 7.0,    // Evil Spirit
        [7] = 8.0,    // Ice

        // Blade Knight
        [19] = 2.0,   // Falling Slash
        [20] = 2.5,   // Lunge
        [22] = 7.0,   // Cyclone
        [23] = 3.0,   // Slash
        [41] = 6.0,   // Twisting Slash
        [21] = 4.0,   // Uppercut

        // Elf
        [24] = 2.0,   // Triple Shot
        [26] = 9.0,   // Heal
        [28] = 12.0,  // Greater Damage (buff)
        [27] = 12.0,  // Greater Defense (buff)
        [52] = 6.0,   // Penetration
        [46] = 8.0,   // Starfall

        // Dark Lord
        [60] = 3.0,   // Force
        [74] = 7.0,   // Fire Blast
        [62] = 9.0,   // Earthshake

        // Summoner
        [214] = 8.0,  // Drain Life
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

        var baseSeconds = BaseCooldownSeconds.TryGetValue((short)skill.Number, out var configured)
            ? configured
            : DefaultBaseSeconds;

        // Skill level runs 1..5 once at least one point is spent; treat 0 as 1.
        var level = Math.Clamp(learned.Level, 1, MobaSkills.SkillLevelCap);
        var factor = Math.Max(MinFraction, 1.0 - (ReductionPerLevel * (level - 1)));

        return TimeSpan.FromSeconds(baseSeconds * factor);
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
