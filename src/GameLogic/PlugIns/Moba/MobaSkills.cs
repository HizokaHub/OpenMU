// <copyright file="MobaSkills.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Skill rules for the MOBA mode (LoL style): the skills of the kit start inactive (rank 0) and a champion point
/// activates or raises them up to <see cref="SkillLevelCap"/>. The attack skills get one point per champion level up
/// to <see cref="AttackPointsMaxLevel"/> (4 skills x 5 ranks = 20 points, the leftovers do not exist); the class
/// buffs have their own points, granted at the levels of <see cref="BuffPointLevels"/>.
/// </summary>
public static class MobaSkills
{
    /// <summary>Maximum level a single skill can be raised to with champion skill points.</summary>
    public const int SkillLevelCap = 5;

    /// <summary>The last champion level which grants a point for the attack skills.</summary>
    public const int AttackPointsMaxLevel = 20;

    /// <summary>The champion levels which grant points for the class buffs.</summary>
    public static readonly int[] BuffPointLevels = { 5, 10, 15, 20, 25, 30 };

    /// <summary>Whether the skill is one of the class buffs (they use the buff points).</summary>
    /// <param name="skillNumber">The skill number.</param>
    /// <returns><see langword="true"/> for a buff.</returns>
    public static bool IsBuff(short skillNumber) => MobaLoadouts.IsBuff(skillNumber);

    /// <summary>
    /// The points granted for reaching a champion level: one for the attack skills up to level 20, and the points of the
    /// class buffs at the milestone levels (2 when the class has 1-2 buffs, 3 for 3-4, 4 for 5).
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <param name="level">The champion level reached.</param>
    /// <returns>The attack skill points and the buff points.</returns>
    public static (int Skill, int Buff) PointsForReaching(Player champion, int level)
        => PointsForReaching(champion.SelectedCharacter?.CharacterClass?.Number ?? 0, level);

    /// <summary>The points granted for reaching a champion level, by class number.</summary>
    /// <param name="classNumber">The character class number.</param>
    /// <param name="level">The champion level reached.</param>
    /// <returns>The attack skill points and the buff points.</returns>
    public static (int Skill, int Buff) PointsForReaching(byte classNumber, int level)
    {
        // The Elf has a 5th skill (the summon) to rank up: 5 x 5 = 25 points, up to level 25.
        var lastLevel = AttackPointsMaxLevel + (MobaLoadouts.SummonsFor(classNumber).Count > 0 ? SkillLevelCap : 0);
        var skill = level <= lastLevel ? 1 : 0;
        var buff = Array.IndexOf(BuffPointLevels, level) >= 0 ? BuffPointsPerMilestone(classNumber) : 0;
        return (skill, buff);
    }

    /// <summary>The buff points a champion gets at every milestone level, by how many buffs its class has.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The points.</returns>
    public static int BuffPointsPerMilestone(Player champion)
        => BuffPointsPerMilestone(champion.SelectedCharacter?.CharacterClass?.Number ?? 0);

    /// <summary>The buff points a champion of the class gets at every milestone level.</summary>
    /// <param name="classNumber">The character class number.</param>
    /// <returns>The points.</returns>
    public static int BuffPointsPerMilestone(byte classNumber)
    {
        var count = MobaLoadouts.BuffsFor(classNumber).Count;
        return count switch
        {
            0 => 0,
            <= 2 => 2,
            <= 4 => 3,
            _ => 4,
        };
    }

    /// <summary>Whether the skill is part of the champion's kit but has no rank yet, so it cannot be cast.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="entry">The skill entry the champion wants to cast.</param>
    /// <returns><see langword="true"/> if the skill is inactive.</returns>
    public static bool IsInactive(Player champion, SkillEntry entry)
        => entry.Level <= 0
           && entry.Skill is { } skill
           && champion.SelectedCharacter?.CharacterClass is { } characterClass
           && MobaLoadouts.InKit(characterClass.Number, (short)skill.Number);

    /// <summary>
    /// Spends one champion point to activate or raise the given learned skill by a level (buffs spend buff points).
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

        var entry = character.LearnedSkills.FirstOrDefault(s => s.Skill?.Number == skillNumber);
        if (entry?.Skill is null)
        {
            return SkillUpResult.SkillNotLearned;
        }

        var isBuff = IsBuff(skillNumber);
        if ((isBuff ? champion.MobaBuffPoints : champion.MobaSkillPoints) <= 0)
        {
            return isBuff ? SkillUpResult.NoBuffPoints : SkillUpResult.NoPoints;
        }

        if (entry.Level >= SkillLevelCap)
        {
            return SkillUpResult.AtCap;
        }

        entry.Level++;
        if (MobaLoadouts.IsCreatureSummon(skillNumber))
        {
            MobaSummons.Refresh(champion);
        }

        if (isBuff)
        {
            champion.MobaBuffPoints--;
        }
        else
        {
            champion.MobaSkillPoints--;
        }

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

        /// <summary>No unspent points for the class buffs.</summary>
        NoBuffPoints,

        /// <summary>The champion has not learned that skill.</summary>
        SkillNotLearned,

        /// <summary>The skill is already at <see cref="SkillLevelCap"/>.</summary>
        AtCap,
    }
}
