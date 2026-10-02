// <copyright file="MobaMasterTree.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>The MOBA role of a master tree node, decided from the attribute the node improves.</summary>
public enum MobaTreeKind
{
    /// <summary>No effect that makes sense in the MOBA model (mastery side effects, curses...). Not available yet.</summary>
    Other,

    /// <summary>Damage nodes: feed the tree term of the damage blend.</summary>
    Offense,

    /// <summary>Maximum health (bounded by the level curve caps).</summary>
    Health,

    /// <summary>Maximum mana (bounded by the level curve caps).</summary>
    Mana,

    /// <summary>Defense, defense rates, shields and resistances: feed the tree term of the mitigation blend.</summary>
    Defense,

    /// <summary>Critical / double-damage chances and attack rate: feed the tree term of the critical blend.</summary>
    Crit,

    /// <summary>Health / mana / SD recovery (out of combat; mana feeds <see cref="MobaMana"/>).</summary>
    Recovery,

    /// <summary>Mana usage reduction (capped 40 %) and weapon-mastery attack speed (capped at the item cap).</summary>
    Utility,

    /// <summary>"X Strengthener" nodes of a damaging skill: in the MOBA they do not replace the skill, they add damage to it (<see cref="MobaMasterTree.StrengthenerBonusOf"/>).</summary>
    Strengthener,

    /// <summary>Pet duration: meaningless in the MOBA. Not available.</summary>
    Useless,

    /// <summary>Maximum shield (SD): feeds the Aegis barrier (the shield pool itself is fixed by the level curve).</summary>
    Shield,

    /// <summary>Item durability nodes ("Durability Reduction"), reassigned to a damage reduction (up to <see cref="MobaMasterTree.DurabilityDamageReduction"/>).</summary>
    Durability,

    /// <summary>Recovery after killing a monster: the native values are divisors read as multipliers (a full heal per kill), so they stay off until given MOBA values.</summary>
    KillRecovery,
}

/// <summary>
/// The Master Skill Tree inside a MOBA match. The champion earns <see cref="PointsPerLevel"/> master points per
/// champion level (about 145 over 29 level-ups, a sixth of a full tree) and spends them in the native tree window,
/// but only on the node kinds (<see cref="MobaTreeKind"/>) that make sense in the MOBA combat model and are enabled in
/// <see cref="AllowedKinds"/>:
/// <list type="bullet">
/// <item>Offense, defense and critical nodes do nothing natively (MOBA combat comes from the MOBA tables), they feed the
/// tree term (20 %) of the damage, mitigation and critical blends - see <see cref="Fraction"/>.</item>
/// <item>Maximum Health and Maximum Mana (bounded by the MOBA level curve caps).</item>
/// </list>
/// Every other node is refused until its kind is integrated. The tree's rank requirements are replaced by
/// <see cref="MeetsRank"/>, because the nodes of the lower ranks are mostly refused ones.
/// </summary>
public static class MobaMasterTree
{
    /// <summary>Master points granted per champion level.</summary>
    public const int PointsPerLevel = 5;

    /// <summary>Most points in nodes of one kind that are ever needed for the whole tree term of a blend.</summary>
    public const int OffensePointsForMax = 100;

    /// <summary>Share of a class's capacity in a kind that gives the whole tree term (classes with few nodes of the kind need fewer points).</summary>
    public const double OffenseCapacityShare = 0.8;

    /// <summary>Points in the same root per rank above 2 needed to learn a node (rank 1 and 2 are free).</summary>
    public const int PointsPerRank = 10;

    /// <summary>Damage added to the base skill per point in one of its strengthener nodes (20 points = +30 %).</summary>
    public const double StrengthenerBonusPerPoint = 0.015;

    /// <summary>Most extra damage the strengtheners of one skill give together.</summary>
    public const double StrengthenerBonusMax = 0.45;

    /// <summary>Extra damage reduction of a fully filled <see cref="MobaTreeKind.Durability"/> term (multiplicative, after the mitigation cap).</summary>
    public const double DurabilityDamageReduction = 0.15;

    /// <summary>Extra Aegis barrier of a fully filled <see cref="MobaTreeKind.Shield"/> term: the tree is 20 % of the total, so +25 % of the tier barrier.</summary>
    public const double AegisTreeBonus = 0.25;

    /// <summary>Weight of the stats term in every blend (damage, mitigation, critical).</summary>
    public const double StatsWeight = 0.45;

    /// <summary>Weight of the tree term in every blend.</summary>
    public const double TreeWeight = 0.20;

    /// <summary>Weight of the items term in every blend.</summary>
    public const double ItemsWeight = 0.35;

    /// <summary>The node kinds champions may spend points on (the rest is refused, see the type description).</summary>
    public static readonly IReadOnlySet<MobaTreeKind> AllowedKinds = new HashSet<MobaTreeKind>
    {
        MobaTreeKind.Offense,
        MobaTreeKind.Health,
        MobaTreeKind.Mana,
        MobaTreeKind.Defense,
        MobaTreeKind.Crit,
        MobaTreeKind.Shield,
        MobaTreeKind.Durability,
        MobaTreeKind.Recovery,
        MobaTreeKind.Utility,
        MobaTreeKind.Strengthener,
        MobaTreeKind.KillRecovery,
    };

    /// <summary>The order a player (or a bot) fills the tree: (kind, share of that kind's cap to reach in this stage).</summary>
    public static readonly IReadOnlyList<(MobaTreeKind Kind, double CapShare)> FillStages = new (MobaTreeKind, double)[]
    {
        (MobaTreeKind.Offense, 0.5),
        (MobaTreeKind.Defense, 0.5),
        (MobaTreeKind.Crit, 1.0),
        (MobaTreeKind.Strengthener, 0.4),
        (MobaTreeKind.Shield, 0.5),
        (MobaTreeKind.Durability, 0.5),
        (MobaTreeKind.Utility, 0.5),
        (MobaTreeKind.Recovery, 0.5),
        (MobaTreeKind.KillRecovery, 0.5),
        (MobaTreeKind.Offense, 1.0),
        (MobaTreeKind.Defense, 1.0),
        (MobaTreeKind.Shield, 1.0),
        (MobaTreeKind.Durability, 1.0),
        (MobaTreeKind.Health, 1.0),
        (MobaTreeKind.Mana, 1.0),
    };

    private static readonly ConcurrentDictionary<short, MobaTreeKind> KindCache = new();

    private static readonly ConcurrentDictionary<(byte Class, MobaTreeKind Kind), int> CapCache = new();

    /// <summary>Whether the MOBA lets champions spend points on the master skill.</summary>
    /// <param name="skill">The master skill.</param>
    /// <returns><c>true</c> if the kind of the node is in <see cref="AllowedKinds"/>.</returns>
    public static bool IsAllowed(Skill skill) => AllowedKinds.Contains(KindOf(skill));

    /// <summary>Whether the master skill is a passive whose effect is damage.</summary>
    /// <param name="skill">The master skill.</param>
    /// <returns><c>true</c> for offense nodes.</returns>
    public static bool IsOffense(Skill skill) => KindOf(skill) == MobaTreeKind.Offense;

    /// <summary>Classifies a master skill node by what it improves.</summary>
    /// <param name="skill">The master skill.</param>
    /// <returns>The kind; <see cref="MobaTreeKind.Other"/> for non master skills.</returns>
    public static MobaTreeKind KindOf(Skill skill)
    {
        if (skill.MasterDefinition is not { } definition)
        {
            return MobaTreeKind.Other;
        }

        return KindCache.GetOrAdd(skill.Number, _ =>
        {
            var kind = Classify(definition.ReplacedSkill is not null, definition.TargetAttribute?.Designation?.ToString());

            // Only the strengtheners of a damaging skill have something to add to; the rest (buffs, summons...) stay off.
            return kind == MobaTreeKind.Strengthener && !(definition.ReplacedSkill is not null && MobaSkillDamage.HasDamageEntry((short)skill.GetBaseSkill().Number))
                ? MobaTreeKind.Other
                : kind;
        });
    }

    /// <summary>The extra damage the champion's strengthener nodes give to a skill (+1,5 % per point, at most +45 %).</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="skillNumber">The (base) skill number.</param>
    /// <returns>0..<see cref="StrengthenerBonusMax"/>.</returns>
    public static double StrengthenerBonusOf(Player champion, short skillNumber)
    {
        var points = 0;
        foreach (var entry in champion.SelectedCharacter?.LearnedSkills ?? Enumerable.Empty<DataModel.Entities.SkillEntry>())
        {
            if (entry.Skill is { MasterDefinition.ReplacedSkill: not null } skill && KindOf(skill) == MobaTreeKind.Strengthener && skill.GetBaseSkill().Number == skillNumber)
            {
                points += entry.Level;
            }
        }

        return Math.Min(StrengthenerBonusMax, points * StrengthenerBonusPerPoint);
    }

    /// <summary>Classifies a node from whether it replaces a skill and the designation of its target attribute.</summary>
    /// <param name="replacesSkill">Whether the node replaces a skill by another.</param>
    /// <param name="designation">The designation of the target attribute, or <c>null</c> for an active node without one.</param>
    /// <returns>The kind.</returns>
    public static MobaTreeKind Classify(bool replacesSkill, string? designation)
    {
        if (replacesSkill || designation is null)
        {
            return MobaTreeKind.Strengthener;
        }

        bool Has(string text) => designation.Contains(text, StringComparison.OrdinalIgnoreCase);

        if (designation.Equals("Maximum Health", StringComparison.OrdinalIgnoreCase))
        {
            return MobaTreeKind.Health;
        }

        if (designation.Equals("Maximum Mana", StringComparison.OrdinalIgnoreCase))
        {
            return MobaTreeKind.Mana;
        }

        if (Has("Item Duration"))
        {
            return MobaTreeKind.Durability;
        }

        if (Has("Pet Duration"))
        {
            return MobaTreeKind.Useless;
        }

        if (Has("after Monster kill"))
        {
            return MobaTreeKind.KillRecovery;
        }

        // The champion has no AG, so its recovery does nothing.
        if (Has("Ability Recovery"))
        {
            return MobaTreeKind.Other;
        }

        if (Has("recover"))
        {
            return MobaTreeKind.Recovery;
        }

        if (Has("Mana Usage") || Has("Attack Speed"))
        {
            return MobaTreeKind.Utility;
        }

        if (Has("Summoned Monster") || Has("Swell Life"))
        {
            return MobaTreeKind.Other;
        }

        if (Has("Critical Damage") || Has("Double Damage Chance") || Has("Raven critical") || Has("Raven exc") || Has("Attack Rate"))
        {
            return MobaTreeKind.Crit;
        }

        // Debuffs / reductions of a skill's own effects and chances of mastery side effects: skill-bound, not generic.
        if (Has("Decrement") || Has("Receive") || Has("Chance") || Has("Extra Projectiles") || Has("Bonus Healing") || Has("Berserker"))
        {
            return MobaTreeKind.Other;
        }

        // The shield pool is fixed by the level curve (a quarter of the durability); the nodes make the Aegis barrier bigger instead.
        if (Has("Maximum Shield"))
        {
            return MobaTreeKind.Shield;
        }

        if (Has("Defense") || Has("Resistance") || Has("Block") || Has("Total Vitality"))
        {
            return MobaTreeKind.Defense;
        }

        return Has("Damage") ? MobaTreeKind.Offense : MobaTreeKind.Other;
    }

    /// <summary>How much one point of the node counts toward its kind's term (attack rate does little without an accuracy roll).</summary>
    /// <param name="skill">The node.</param>
    /// <returns>The weight, 0..1.</returns>
    public static double PointWeight(Skill skill)
        => skill.MasterDefinition?.TargetAttribute?.Designation?.ToString()?.Contains("Attack Rate", StringComparison.OrdinalIgnoreCase) == true ? 0.5 : 1.0;

    /// <summary>Share of the maximum restored per last hit with 1 point in a "recover after monster kill" node.</summary>
    public const double KillRecoveryMin = 0.02;

    /// <summary>Share of the maximum restored per last hit with a fully filled (20 points) kill-recovery node.</summary>
    public const double KillRecoveryMax = 0.10;

    /// <summary>
    /// Whether a master node's native effect is one of the "recover after monster kill" multipliers. Their native values are
    /// divisors read as multipliers (a full heal per kill), so a MOBA clone never gets the native effect: see <see cref="ApplyKillRecovery"/>.
    /// </summary>
    /// <param name="target">The node's target attribute.</param>
    /// <returns><c>true</c> for the health / mana / shield kill-recovery multipliers.</returns>
    public static bool IsKillRecoveryTarget(AttributeDefinition? target)
        => target == Stats.HealthAfterMonsterKillMultiplier
           || target == Stats.ManaAfterMonsterKillMultiplier
           || target == Stats.ShieldAfterMonsterKillMultiplier;

    /// <summary>The MOBA kill-recovery fraction of a node level: 2 % at 1 point, rising linearly to 10 % at 20.</summary>
    /// <param name="level">The node level.</param>
    /// <returns>The fraction of the maximum, 0 for level 0.</returns>
    public static double KillRecoveryFraction(int level)
        => level <= 0 ? 0 : KillRecoveryMin + ((KillRecoveryMax - KillRecoveryMin) * (Math.Min(level, 20) - 1) / 19.0);

    /// <summary>
    /// Restores HP / mana / SD after the champion's last hit on a monster: 2 % (1 point) to 10 % (20 points) of the maximum
    /// of each resource, from its own node ("Monster Attack Life / Mana / SD Inc", "Recover HP / Mana / SD from Monster Kills").
    /// </summary>
    /// <param name="champion">The champion.</param>
    public static void ApplyKillRecovery(Player champion)
    {
        if (champion.Attributes is not { } a)
        {
            return;
        }

        foreach (var entry in champion.SelectedCharacter?.LearnedSkills ?? Enumerable.Empty<DataModel.Entities.SkillEntry>())
        {
            var target = entry.Skill?.MasterDefinition?.TargetAttribute;
            if (!IsKillRecoveryTarget(target))
            {
                continue;
            }

            var (current, max) = target == Stats.HealthAfterMonsterKillMultiplier
                ? (Stats.CurrentHealth, Stats.MaximumHealth)
                : target == Stats.ManaAfterMonsterKillMultiplier
                    ? (Stats.CurrentMana, Stats.MaximumMana)
                    : (Stats.CurrentShield, Stats.MaximumShield);
            a[current] = Math.Min(a[max], a[current] + (a[max] * (float)KillRecoveryFraction(entry.Level)));
        }
    }

    /// <summary>Gets the weighted points the champion has in nodes of a kind.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The points.</returns>
    public static double PointsIn(Player champion, MobaTreeKind kind)
    {
        var points = 0.0;
        foreach (var entry in champion.SelectedCharacter?.LearnedSkills ?? Enumerable.Empty<DataModel.Entities.SkillEntry>())
        {
            if (entry.Skill is { MasterDefinition: not null } skill && KindOf(skill) == kind)
            {
                points += entry.Level * PointWeight(skill);
            }
        }

        return points;
    }

    /// <summary>Gets the points the champion has in offense nodes.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The points.</returns>
    public static int OffensePoints(Player champion) => (int)PointsIn(champion, MobaTreeKind.Offense);

    /// <summary>The weighted points in a kind that give the champion's class the whole tree term: <c>min(<see cref="OffensePointsForMax"/>, 80 % of the class's capacity in the kind)</c>; 0 if the class has no such nodes.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The points.</returns>
    public static int CapOf(Player champion, MobaTreeKind kind)
    {
        if (champion.SelectedCharacter?.CharacterClass is not { } characterClass)
        {
            return OffensePointsForMax;
        }

        return CapCache.GetOrAdd((characterClass.Number, kind), _ => CapFor(champion.GameContext.Configuration.Skills, characterClass, kind));
    }

    /// <summary>The offense cap of the champion's class (at least 1).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The points.</returns>
    public static int OffenseCapOf(Player champion) => Math.Max(1, CapOf(champion, MobaTreeKind.Offense));

    /// <summary>Computes the cap of a class in a kind from the skill list (0 if the class has no such nodes).</summary>
    /// <param name="skills">All skills.</param>
    /// <param name="characterClass">The class.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The points.</returns>
    public static int CapFor(IEnumerable<Skill> skills, CharacterClass characterClass, MobaTreeKind kind)
    {
        var capacity = skills
            .Where(s => s.MasterDefinition is not null && KindOf(s) == kind && s.QualifiedCharacters.Contains(characterClass))
            .Sum(s => s.MasterDefinition!.MaximumLevel * PointWeight(s));
        return (int)Math.Min(OffensePointsForMax, capacity * OffenseCapacityShare);
    }

    /// <summary>The offense cap of a class from the skill list (at least 1).</summary>
    /// <param name="skills">All skills.</param>
    /// <param name="characterClass">The class.</param>
    /// <returns>The points.</returns>
    public static int OffenseCapFor(IEnumerable<Skill> skills, CharacterClass characterClass)
        => Math.Max(1, CapFor(skills, characterClass, MobaTreeKind.Offense));

    /// <summary>The tree term of a blend: points in the kind over <see cref="CapOf"/>, or <c>null</c> if the class has no nodes of the kind (the blend then drops the tree term and renormalizes).</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>0..1, or <c>null</c>.</returns>
    public static double? Fraction(Player champion, MobaTreeKind kind)
    {
        var cap = CapOf(champion, kind);
        return cap <= 0 ? null : Math.Clamp(PointsIn(champion, kind) / cap, 0.0, 1.0);
    }

    /// <summary>The tree term of the damage blend.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>0..1.</returns>
    public static double OffenseFraction(Player champion) => Fraction(champion, MobaTreeKind.Offense) ?? 0.0;

    /// <summary>
    /// The shared blend of the damage, mitigation and critical formulas (2026-10-01, set by the user):
    /// <c>0,45 × stats + 0,20 × tree + 0,35 × items</c>. A class without nodes of the kind has no tree term, so
    /// the stats and items weights are renormalized instead of the class losing a fifth of the result.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <param name="kind">The tree kind that feeds the tree term.</param>
    /// <param name="fromStats">The stats term, 0..1.</param>
    /// <param name="fromItems">The items term, 0..1.</param>
    /// <returns>The blended 0..1 fraction.</returns>
    public static double Blend(Player champion, MobaTreeKind kind, double fromStats, double fromItems)
        => Blend(Fraction(champion, kind), fromStats, fromItems);

    /// <summary>The blend of <see cref="Blend(Player, MobaTreeKind, double, double)"/> with the tree term given.</summary>
    /// <param name="fromTree">The tree term, or <c>null</c> if the class has none.</param>
    /// <param name="fromStats">The stats term, 0..1.</param>
    /// <param name="fromItems">The items term, 0..1.</param>
    /// <returns>The blended 0..1 fraction.</returns>
    public static double Blend(double? fromTree, double fromStats, double fromItems)
    {
        if (fromTree is { } tree)
        {
            return Math.Clamp((StatsWeight * fromStats) + (TreeWeight * tree) + (ItemsWeight * fromItems), 0.0, 1.0);
        }

        return Math.Clamp(((StatsWeight * fromStats) + (ItemsWeight * fromItems)) / (StatsWeight + ItemsWeight), 0.0, 1.0);
    }

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

    /// <summary>The next node a bot (or a player following the fill plan) would put a point in, or <c>null</c> if nothing is left.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The node.</returns>
    public static Skill? NextNodeToFill(Player champion)
    {
        if (champion.SelectedCharacter?.CharacterClass is not { } characterClass)
        {
            return null;
        }

        var learned = champion.SelectedCharacter.LearnedSkills;
        foreach (var (kind, share) in FillStages)
        {
            var isCapped = kind is not (MobaTreeKind.Health or MobaTreeKind.Mana);
            if (isCapped && PointsIn(champion, kind) >= CapOf(champion, kind) * share)
            {
                continue;
            }

            var node = champion.GameContext.Configuration.Skills
                .Where(s => s.MasterDefinition is not null && KindOf(s) == kind && IsAllowed(s) && s.QualifiedCharacters.Contains(characterClass)
                            && (kind != MobaTreeKind.Strengthener || learned.Any(l => l.Skill?.Number == s.GetBaseSkill().Number)))
                .OrderBy(s => s.MasterDefinition!.Rank)
                .ThenBy(s => s.Number)
                .FirstOrDefault(s => (learned.FirstOrDefault(l => l.Skill == s)?.Level ?? 0) < s.MasterDefinition!.MaximumLevel && MeetsRank(champion, s));
            if (node is not null)
            {
                return node;
            }
        }

        return null;
    }
}
