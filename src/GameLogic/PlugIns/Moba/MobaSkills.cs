// <copyright file="MobaSkills.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Skill rules for the MOBA mode: level cap per skill, and the helper to spend a
/// champion skill point. Damage scaling per skill level and per-match cooldowns hang
/// off this next (balance pass / cooldown step).
/// </summary>
public static class MobaSkills
{
    /// <summary>Maximum level a single skill can be raised to with champion skill points.</summary>
    public const int SkillLevelCap = 5;

    /// <summary>
    /// Spends one champion skill point to raise the given learned skill by a level.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <param name="skillNumber">The skill number to level up.</param>
    /// <returns>The result of the attempt.</returns>
    public static SkillUpResult TryLevelUp(Player champion, short skillNumber)
    {
        if (!champion.IsMobaClone || champion.SelectedCharacter is not { } character)
        {
            return SkillUpResult.NotInMatch;
        }

        if (champion.MobaSkillPoints <= 0)
        {
            return SkillUpResult.NoPoints;
        }

        var entry = character.LearnedSkills.FirstOrDefault(s => s.Skill?.Number == skillNumber);
        if (entry?.Skill is null)
        {
            return SkillUpResult.SkillNotLearned;
        }

        if (entry.Level >= SkillLevelCap)
        {
            return SkillUpResult.AtCap;
        }

        entry.Level++;
        champion.MobaSkillPoints--;
        return SkillUpResult.Ok;
    }

    /// <summary>Outcome of <see cref="TryLevelUp"/>.</summary>
    public enum SkillUpResult
    {
        /// <summary>The skill was raised by a level.</summary>
        Ok,

        /// <summary>The session is not a MOBA clone.</summary>
        NotInMatch,

        /// <summary>No unspent champion skill points.</summary>
        NoPoints,

        /// <summary>The champion has not learned that skill.</summary>
        SkillNotLearned,

        /// <summary>The skill is already at <see cref="SkillLevelCap"/>.</summary>
        AtCap,
    }
}
