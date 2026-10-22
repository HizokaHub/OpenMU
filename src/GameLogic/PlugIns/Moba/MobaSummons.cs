// <copyright file="MobaSummons.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The creature of the Elf (the 5th skill of her kit). The seven creatures are only appearances: they all have the same
/// strength, which grows with the rank of the skill (1-5) and with the champion level. A champion has one at a time; summoning
/// again replaces it. While the control mode is on (key X) the owner commands it like a second character: it walks anywhere on
/// the map and fights what is around it, and attacks the target of an order; with the mode off it follows the owner and fights
/// next to it. It dies like any minion and its kills count for the owner.
/// </summary>
public static class MobaSummons
{
    /// <summary>Order kind: walk to a tile.</summary>
    public const byte OrderMove = 1;

    /// <summary>Order kind: attack a target.</summary>
    public const byte OrderAttack = 2;

    // Same combat basis as a lane minion (see MobaWaveSpawner), scaled with the rank.
    private const float BaseMinDamage = 60f;
    private const float BaseMaxDamage = 85f;
    private const float BaseDefense = 20f;
    private const float AttackRate = 150f;
    private const float DefenseRate = 30f;

    /// <summary>Life of the creature as a share of the champion life at its level: 38 % at rank 1 up to 70 % at rank 5.</summary>
    /// <param name="level">The champion level.</param>
    /// <param name="rank">The rank of the skill (1-5).</param>
    /// <returns>The maximum life.</returns>
    public static float HealthAt(int level, int rank)
        => MobaProgression.HealthAt(level) * (0.30f + (0.08f * Math.Clamp(rank, 1, MobaSkills.SkillLevelCap)));

    /// <summary>The factor on the damage of a lane minion: 1.0 at rank 1 up to 1.8 at rank 5.</summary>
    /// <param name="level">The champion level.</param>
    /// <param name="rank">The rank of the skill (1-5).</param>
    /// <returns>The multiplier.</returns>
    public static float DamageMultiplierAt(int level, int rank)
        => MobaWaveSpawner.CreepDamageMultiplierAt(level) * (0.8f + (0.2f * Math.Clamp(rank, 1, MobaSkills.SkillLevelCap)));

    /// <summary>Gets the rank (0-5) of the summon skill the champion carries; 0 if it has none or it is inactive.</summary>
    /// <param name="owner">The champion.</param>
    /// <returns>The rank.</returns>
    public static int RankOf(Player owner)
        => owner.SelectedCharacter?.LearnedSkills.FirstOrDefault(e => e.Skill is { } s && MobaLoadouts.IsCreatureSummon((short)s.Number))?.Level ?? 0;

    /// <summary>Gets the summon of the champion, if it is alive.</summary>
    /// <param name="owner">The champion.</param>
    /// <returns>The monster and its brain, or <see langword="null"/>.</returns>
    public static (Monster Monster, MobaSummonIntelligence Brain)? Of(Player owner)
        => owner.Summon is ({ IsAlive: true } monster, MobaSummonIntelligence brain) ? (monster, brain) : null;

    /// <summary>Whether the skill is the creature summon of the Elf kit.</summary>
    /// <param name="skill">The skill.</param>
    /// <returns><see langword="true"/> if casting it summons the creature.</returns>
    public static bool IsSummonSkill(Skill skill) => MobaLoadouts.IsCreatureSummon((short)skill.Number);

    /// <summary>Called by the summon plumbing before the monster is initialized: its life comes from the rank and the level.</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="area">The spawn area of the monster.</param>
    public static void Prepare(Player owner, MonsterSpawnArea area)
    {
        area.MaximumHealthOverride = (int)HealthAt(owner.MobaLevel, Math.Max(RankOf(owner), 1));
    }

    /// <summary>Called after the monster was initialized: its team, and the combat stats of its rank.</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="monster">The summoned monster.</param>
    public static void Equip(Player owner, Monster monster)
    {
        MobaTeams.Set(monster, MobaTeams.GetTeam(owner));
        ApplyStats(owner, monster, healToFull: true);
    }

    /// <summary>Updates the stats of the living summon (the champion leveled up or ranked the skill).</summary>
    /// <param name="owner">The champion.</param>
    public static void Refresh(Player owner)
    {
        if (Of(owner) is { } summon)
        {
            ApplyStats(owner, summon.Monster, healToFull: false);
        }
    }

    /// <summary>Turns the control mode on or off.</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="on">The new mode.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SetControlAsync(Player owner, bool on)
    {
        if (!owner.IsMobaClone)
        {
            return;
        }

        if (on && Of(owner) is null)
        {
            await owner.ShowBlueMessageAsync("[MOBA] No tienes una invocación viva.").ConfigureAwait(false);
            owner.MobaSummonControl = false;
            return;
        }

        owner.MobaSummonControl = on;
        if (!on)
        {
            Of(owner)?.Brain.ClearOrders();
        }

        await owner.ShowBlueMessageAsync(on
            ? "[MOBA] Controlas a tu invocación: clic derecho en el suelo o en el minimapa la manda, en un enemigo la manda a atacarlo. X para soltarla."
            : "[MOBA] La invocación te sigue otra vez.").ConfigureAwait(false);
    }

    /// <summary>Applies an order of the owner to the summon.</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="kind"><see cref="OrderMove"/> or <see cref="OrderAttack"/>.</param>
    /// <param name="x">The tile X of a move order.</param>
    /// <param name="y">The tile Y of a move order.</param>
    /// <param name="targetKey">The object id of the target of an attack order.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OrderAsync(Player owner, byte kind, byte x, byte y, ushort targetKey)
    {
        if (!owner.IsMobaClone || !owner.MobaSummonControl || Of(owner) is not { } summon || owner.CurrentMap is not { } map)
        {
            return;
        }

        if (kind == OrderMove)
        {
            if (map.Terrain.WalkMap[x, y])
            {
                await summon.Brain.OrderMoveAsync(new Point(x, y)).ConfigureAwait(false);
            }

            return;
        }

        if (kind == OrderAttack
            && map.GetObject(targetKey) is IAttackable target
            && target.IsAlive
            && MobaTeams.AreEnemies(summon.Monster, target)
            && MobaVision.CanTarget(owner, target))
        {
            await summon.Brain.OrderAttackAsync(target).ConfigureAwait(false);
        }
    }

    /// <summary>Called when the summon is gone (it died, was replaced or the match ended): the control mode ends with it.</summary>
    /// <param name="owner">The champion.</param>
    public static void OnSummonGone(Player owner)
    {
        if (owner.IsMobaClone)
        {
            owner.MobaSummonControl = false;
        }
    }

    /// <summary>The champion which owns the attacker when it is a summon, or the attacker itself.</summary>
    /// <param name="attacker">Who dealt the damage (a champion, a minion, a summon).</param>
    /// <returns>The champion to credit, or <see langword="null"/> when it is not one.</returns>
    public static Player? CreditedChampion(object? attacker)
        => attacker switch
        {
            Player player => player,
            Monster { SummonedBy: { } owner } => owner,
            _ => null,
        };

    private static void ApplyStats(Player owner, Monster monster, bool healToFull)
    {
        var level = owner.MobaLevel;
        var rank = Math.Max(RankOf(owner), 1);
        var damageMul = DamageMultiplierAt(level, rank);
        var health = HealthAt(level, rank);
        var before = monster.Attributes[Stats.MaximumHealth];

        SetAbsolute(monster, Stats.MaximumHealth, health);
        SetAbsolute(monster, Stats.MinimumPhysBaseDmg, BaseMinDamage * damageMul);
        SetAbsolute(monster, Stats.MaximumPhysBaseDmg, BaseMaxDamage * damageMul);
        SetAbsolute(monster, Stats.DefenseBase, BaseDefense);
        SetAbsolute(monster, Stats.AttackRatePvm, AttackRate);
        SetAbsolute(monster, Stats.DefenseRatePvm, DefenseRate);

        // A summon which grows keeps its wounds (the new life is added), a fresh one starts full.
        monster.Health = healToFull ? (int)health : Math.Clamp(monster.Health + (int)(health - before), 1, (int)health);
    }

    private static void SetAbsolute(Monster monster, AttributeDefinition stat, float value)
    {
        var current = monster.Attributes[stat];
        monster.Attributes.AddElement(new SimpleElement(value - current, AggregateType.AddRaw), stat);
    }
}
