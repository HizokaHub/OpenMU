// <copyright file="MobaMasterTree.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The Master Skill Tree inside a MOBA match. The champion earns <see cref="PointsPerLevel"/> master points per
/// champion level (about 145 over 29 level-ups, a sixth of a full tree) and spends them in the native tree window,
/// but only on the nodes that make sense in the MOBA combat model:
/// <list type="bullet">
/// <item>Passive nodes whose effect is damage ("offense" nodes): they do nothing natively (MOBA damage comes from
/// <see cref="MobaSkillDamage"/>), they feed the tree term of the damage blend - <see cref="OffenseFraction"/>.</item>
/// <item>Maximum Health and Maximum Mana (bounded by the MOBA level curve caps).</item>
/// </list>
/// Every other node (skill strengtheners that replace a skill, resistances, durability, recoveries, ...) is refused:
/// it would either replace a skill the MOBA tables know by number or do nothing. The tree's rank requirements are
/// replaced by <see cref="MeetsRank"/>, because the nodes of the lower ranks are mostly refused ones.
/// </summary>
public static class MobaMasterTree
{
    /// <summary>Master points granted per champion level.</summary>
    public const int PointsPerLevel = 5;

    /// <summary>Most points in offense nodes that are ever needed for the whole tree term of the damage blend.</summary>
    public const int OffensePointsForMax = 100;

    /// <summary>Share of a class's offense-node capacity that gives the whole tree term (classes with few offense nodes need fewer points).</summary>
    public const double OffenseCapacityShare = 0.8;

    /// <summary>Points in the same root per rank above 2 needed to learn a node (rank 1 and 2 are free).</summary>
    public const int PointsPerRank = 10;

    private static readonly ConcurrentDictionary<short, bool> OffenseCache = new();

    private static readonly ConcurrentDictionary<short, int> OffenseCapCache = new();

    /// <summary>Whether the MOBA lets champions spend points on the master skill.</summary>
    /// <param name="skill">The master skill.</param>
    /// <returns><c>true</c> for offense nodes, maximum health and maximum mana.</returns>
    public static bool IsAllowed(Skill skill)
    {
        var definition = skill.MasterDefinition;
        if (definition is null || definition.ReplacedSkill is not null || definition.TargetAttribute is null)
        {
            return false;
        }

        return IsOffense(skill) || definition.TargetAttribute == Stats.MaximumHealth || definition.TargetAttribute == Stats.MaximumMana;
    }

    /// <summary>Whether the master skill is a passive whose effect is damage.</summary>
    /// <param name="skill">The master skill.</param>
    /// <returns><c>true</c> for offense nodes.</returns>
    public static bool IsOffense(Skill skill)
    {
        if (skill.MasterDefinition is not { ReplacedSkill: null, TargetAttribute: { } target })
        {
            return false;
        }

        return OffenseCache.GetOrAdd(skill.Number, _ => (target.Designation?.ToString() ?? string.Empty).Contains("Damage", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gets the points the champion has in offense nodes.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The points.</returns>
    public static int OffensePoints(Player champion)
    {
        var points = 0;
        foreach (var entry in champion.SelectedCharacter?.LearnedSkills ?? Enumerable.Empty<DataModel.Entities.SkillEntry>())
        {
            if (entry.Skill is { MasterDefinition: not null } skill && IsOffense(skill))
            {
                points += entry.Level;
            }
        }

        return points;
    }

    /// <summary>The points in offense nodes that give the champion's class the whole tree term: <c>min(<see cref="OffensePointsForMax"/>, 80 % of the class's offense capacity)</c>.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The points.</returns>
    public static int OffenseCapOf(Player champion)
    {
        if (champion.SelectedCharacter?.CharacterClass is not { } characterClass)
        {
            return OffensePointsForMax;
        }

        return OffenseCapCache.GetOrAdd(characterClass.Number, _ => OffenseCapFor(champion.GameContext.Configuration.Skills, characterClass));
    }

    /// <summary>Computes the offense cap of a class from the skill list.</summary>
    /// <param name="skills">All skills.</param>
    /// <param name="characterClass">The class.</param>
    /// <returns>The points.</returns>
    public static int OffenseCapFor(IEnumerable<Skill> skills, CharacterClass characterClass)
    {
        var capacity = skills
            .Where(s => s.MasterDefinition is not null && IsOffense(s) && s.QualifiedCharacters.Contains(characterClass))
            .Sum(s => (int)s.MasterDefinition!.MaximumLevel);
        return Math.Max(1, Math.Min(OffensePointsForMax, (int)(capacity * OffenseCapacityShare)));
    }

    /// <summary>The tree term of the damage blend: offense points over <see cref="OffenseCapOf"/>.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..1.</returns>
    public static double OffenseFraction(Player champion)
        => Math.Clamp(OffensePoints(champion) / (double)OffenseCapOf(champion), 0.0, 1.0);

    /// <summary>Points the champion needs in the node's root to learn it: (rank - 2) * <see cref="PointsPerRank"/>, at least 0.</summary>
    /// <param name="skill">The node.</param>
    /// <returns>The points.</returns>
    public static int PointsNeededInRoot(Skill skill)
        => Math.Max(0, (skill.MasterDefinition?.Rank ?? 0) - 2) * PointsPerRank;

    /// <summary>The MOBA rank rule: a node of rank r needs <see cref="PointsNeededInRoot"/> points in the same root.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="skill">The node.</param>
    /// <returns><c>true</c> if the champion may learn it.</returns>
    public static bool MeetsRank(Player champion, Skill skill)
    {
        var definition = skill.MasterDefinition;
        if (definition is null)
        {
            return false;
        }

        var needed = PointsNeededInRoot(skill);
        if (needed == 0)
        {
            return true;
        }

        var inRoot = (champion.SelectedCharacter?.LearnedSkills ?? Enumerable.Empty<DataModel.Entities.SkillEntry>())
            .Where(e => e.Skill?.MasterDefinition?.Root?.Id == definition.Root?.Id)
            .Sum(e => (int)e.Level);
        return inRoot >= needed;
    }

    /// <summary>The nodes of the champion's class, in the order a player (or a bot) would fill them: offense first, by rank, then health, then mana.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The allowed nodes.</returns>
    public static IEnumerable<Skill> NodesInFillOrder(Player champion)
    {
        var characterClass = champion.SelectedCharacter?.CharacterClass;
        return champion.GameContext.Configuration.Skills
            .Where(s => s.MasterDefinition is not null && IsAllowed(s) && characterClass is not null && s.QualifiedCharacters.Contains(characterClass))
            .OrderBy(s => IsOffense(s) ? 0 : (s.MasterDefinition!.TargetAttribute == Stats.MaximumHealth ? 1 : 2))
            .ThenBy(s => s.MasterDefinition!.Rank)
            .ThenBy(s => s.Number);
    }
}
