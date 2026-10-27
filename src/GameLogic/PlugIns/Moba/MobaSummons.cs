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
    /// <param name="summonLevel">The level of the creature itself (1-10).</param>
    public static float HealthAt(int level, int rank, int summonLevel = 1)
        => MobaProgression.HealthAt(level) * (0.30f + (0.08f * Math.Clamp(rank, 1, MobaSkills.SkillLevelCap))) * (1f + (LevelHealthBonus * (Math.Clamp(summonLevel, 1, MaxSummonLevel) - 1)));

    /// <summary>The factor on the damage of a lane minion: 1.0 at rank 1 up to 1.8 at rank 5.</summary>
    /// <param name="level">The champion level.</param>
    /// <param name="rank">The rank of the skill (1-5).</param>
    /// <param name="summonLevel">The level of the creature itself (1-10).</param>
    /// <returns>The multiplier.</returns>
    public static float DamageMultiplierAt(int level, int rank, int summonLevel = 1)
        => MobaWaveSpawner.CreepDamageMultiplierAt(level) * (0.8f + (0.2f * Math.Clamp(rank, 1, MobaSkills.SkillLevelCap))) * (1f + (LevelDamageBonus * (Math.Clamp(summonLevel, 1, MaxSummonLevel) - 1)));

    /// <summary>Kill points of a lane minion.</summary>
    public const int PointsCreep = 1;

    /// <summary>Kill points of a jungle monster.</summary>
    public const int PointsJungle = 3;

    /// <summary>Kill points of an epic jungle monster.</summary>
    public const int PointsEpic = 8;

    /// <summary>Kill points of an enemy champion.</summary>
    public const int PointsChampion = 6;

    /// <summary>The highest level of the creature.</summary>
    public const int MaxSummonLevel = 10;

    /// <summary>Life gained per level of the creature (relative to level 1).</summary>
    public const float LevelHealthBonus = 0.15f;

    /// <summary>Damage gained per level of the creature (relative to level 1).</summary>
    public const float LevelDamageBonus = 0.12f;

    /// <summary>Defense gained per level of the creature.</summary>
    public const float LevelDefenseBonus = 6f;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, object> TestOwners = new();

    // Kill points needed in total to reach level 2, 3, ... 10.
    private static readonly int[] PointsToReach = { 4, 9, 15, 22, 30, 39, 49, 60, 72 };

    /// <summary>The level (1-10) a creature with these kill points has.</summary>
    /// <param name="points">The kill points.</param>
    /// <returns>The level.</returns>
    public static int LevelForPoints(int points)
    {
        var level = 1;
        foreach (var needed in PointsToReach)
        {
            if (points < needed)
            {
                break;
            }

            level++;
        }

        return level;
    }

    /// <summary>The progress (0-100) towards the next level of the creature; 100 at the highest level.</summary>
    /// <param name="owner">The champion.</param>
    /// <returns>The percentage.</returns>
    public static int ProgressPercent(Player owner)
    {
        var level = Math.Clamp(owner.MobaSummonLevel, 1, MaxSummonLevel);
        if (level >= MaxSummonLevel)
        {
            return 100;
        }

        var from = level == 1 ? 0 : PointsToReach[level - 2];
        var to = PointsToReach[level - 1];
        return Math.Clamp((owner.MobaSummonPoints - from) * 100 / Math.Max(1, to - from), 0, 99);
    }

    /// <summary>When the creature of the champion (or a creature of it) lands the last hit, it collects kill points.</summary>
    /// <param name="map">The map.</param>
    /// <param name="death">How the victim died.</param>
    /// <param name="points">The points the kill is worth.</param>
    /// <returns>A task.</returns>
    public static async ValueTask NoteKillAsync(GameMap map, DeathInformation death, int points)
    {
        if (map.GetObject(death.KillerId) is Monster { SummonedBy: { IsMobaClone: true } owner } killer
            && ReferenceEquals(Of(owner)?.Monster, killer))
        {
            await GrantPointsAsync(owner, points).ConfigureAwait(false);
        }
    }

    /// <summary>Adds kill points to the creature of the champion and levels it up when it earns it.</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="points">The points.</param>
    /// <returns>A task.</returns>
    public static async ValueTask GrantPointsAsync(Player owner, int points)
    {
        if (points <= 0 || owner.MobaSummonLevel >= MaxSummonLevel)
        {
            return;
        }

        owner.MobaSummonPoints += IsTesting(owner) ? points * 3 : points;
        var level = LevelForPoints(owner.MobaSummonPoints);
        if (level <= owner.MobaSummonLevel)
        {
            return;
        }

        owner.MobaSummonLevel = level;
        Refresh(owner);
        await owner.ShowBlueMessageAsync($"[MOBA] Tu invocación sube al nivel {level}{(level >= MaxSummonLevel ? " (máximo)" : string.Empty)}.").ConfigureAwait(false);
    }

    /// <summary>Gets the rank (0-5) of the summon skill the champion carries; 0 if it has none or it is inactive.</summary>
    /// <param name="owner">The champion.</param>
    /// <returns>The rank.</returns>
    public static int RankOf(Player owner)
        => owner.SelectedCharacter?.LearnedSkills.FirstOrDefault(e => e.Skill is { } s && MobaLoadouts.IsCreatureSummon((short)s.Number))?.Level ?? 0;

    /// <summary>Gets the appearance (the skill number 30-36 of the creature) the champion picked.</summary>
    /// <param name="owner">The champion.</param>
    /// <returns>The skill number; the goblin when it has none.</returns>
    public static short AppearanceOf(Player owner)
        => (short)(owner.SelectedCharacter?.LearnedSkills.FirstOrDefault(e => e.Skill is { } s && MobaLoadouts.IsCreatureSummon((short)s.Number))?.Skill?.Number ?? 30);

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
        area.MaximumHealthOverride = (int)HealthAt(owner.MobaLevel, Math.Max(RankOf(owner), 1), owner.MobaSummonLevel);
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

    /// <summary>Control mode: the champion commands the summon.</summary>
    public const byte ModeControl = 1;

    /// <summary>Release: the summon stays where it is, holding its post (it finishes the order it has).</summary>
    public const byte ModeStay = 0;

    /// <summary>Call back: the summon follows the champion again.</summary>
    public const byte ModeFollow = 2;

    /// <summary>Changes what the summon does: commanded (X), released where it stands (X again) or following the champion (Z).</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="mode"><see cref="ModeControl"/>, <see cref="ModeStay"/> or <see cref="ModeFollow"/>.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SetControlAsync(Player owner, byte mode)
    {
        if (!owner.IsMobaClone)
        {
            return;
        }

        if (Of(owner) is not { } summon)
        {
            owner.MobaSummonControl = false;
            owner.MobaSummonStay = false;
            await owner.ShowBlueMessageAsync("[MOBA] No tienes una invocación viva.").ConfigureAwait(false);
            return;
        }

        string message;
        switch (mode)
        {
            case ModeControl:
                owner.MobaSummonControl = true;
                owner.MobaSummonStay = false;
                message = "[MOBA] Controlas a tu invocación: clic derecho en el suelo o en el minimapa la manda, en un enemigo la manda a atacarlo. X para soltarla, Z para llamarla.";
                break;
            case ModeFollow:
                owner.MobaSummonControl = false;
                owner.MobaSummonStay = false;
                summon.Brain.ClearOrders();
                message = "[MOBA] La invocación vuelve contigo y te sigue.";
                break;
            default:
                owner.MobaSummonControl = false;
                owner.MobaSummonStay = true;
                summon.Brain.ClearOrders();
                await summon.Monster.StopWalkingAsync().ConfigureAwait(false);
                message = "[MOBA] La invocación se queda donde está defendiendo su puesto (ataca lo que tenga cerca, no a las torres). Z para llamarla.";
                break;
        }

        await owner.ShowBlueMessageAsync(message).ConfigureAwait(false);
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

    /// <summary>The owner casts a skill of the creature (key 1 or 2 of the control mode).</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="slot">The skill (0 or 1).</param>
    /// <param name="targetKey">The object id of the target under the cursor (0xFFFF for none).</param>
    /// <returns>A task.</returns>
    public static async ValueTask CastSkillAsync(Player owner, byte slot, ushort targetKey)
    {
        if (!owner.IsMobaClone || !owner.MobaSummonControl || Of(owner) is not { } summon || owner.CurrentMap is not { } map)
        {
            return;
        }

        IAttackable? target = null;
        if (targetKey != ushort.MaxValue
            && map.GetObject(targetKey) is IAttackable candidate
            && candidate.IsAlive
            && MobaTeams.AreEnemies(summon.Monster, candidate)
            && MobaVision.CanTarget(owner, candidate))
        {
            target = candidate;
        }

        if (await summon.Brain.OrderSkillAsync(slot, target).ConfigureAwait(false) is { } message)
        {
            await owner.ShowBlueMessageAsync(message).ConfigureAwait(false);
        }
    }

    /// <summary>The cooldown left of the skills of the creature of the champion, in tenths of a second (for the HUD).</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="slot">The skill (0 or 1).</param>
    /// <returns>The tenths of a second (0 when ready or when there is no such skill).</returns>
    public static byte CooldownTenths(Player owner, int slot)
    {
        if (Of(owner) is not { } summon || slot >= MobaSummonSkills.SkillsOf(AppearanceOf(owner)).Count)
        {
            return 0;
        }

        var skill = MobaSummonSkills.SkillsOf(AppearanceOf(owner))[slot];
        return (byte)Math.Clamp((int)Math.Ceiling(summon.Brain.RemainingSeconds(skill.Key) * 10), 0, 255);
    }

    /// <summary>Whether the champion tests its creature (invulnerable, hits harder and earns triple points): dev command <c>/mobainvoca fuerte</c>.</summary>
    /// <param name="owner">The champion.</param>
    /// <returns><see langword="true"/> in test mode.</returns>
    public static bool IsTesting(Player owner) => TestOwners.TryGetValue(owner, out _);

    /// <summary>Switches the test mode of the creature of the champion on or off.</summary>
    /// <param name="owner">The champion.</param>
    /// <returns>The new state.</returns>
    public static bool ToggleTesting(Player owner)
    {
        if (IsTesting(owner))
        {
            TestOwners.Remove(owner);
        }
        else
        {
            TestOwners.Add(owner, new object());
        }

        Refresh(owner);
        return IsTesting(owner);
    }

    /// <summary>Sets the level of the creature at once (dev command <c>/mobainvoca nivel N</c>).</summary>
    /// <param name="owner">The champion.</param>
    /// <param name="level">The level (1-10).</param>
    public static void SetLevel(Player owner, int level)
    {
        level = Math.Clamp(level, 1, MaxSummonLevel);
        owner.MobaSummonLevel = level;
        owner.MobaSummonPoints = level == 1 ? 0 : PointsToReach[level - 2];
        Refresh(owner);
    }

    /// <summary>Called when the summon is gone (it died, was replaced or the match ended): the control mode ends with it.</summary>
    /// <param name="owner">The champion.</param>
    public static void OnSummonGone(Player owner)
    {
        if (owner.IsMobaClone)
        {
            owner.MobaSummonControl = false;
            owner.MobaSummonStay = false;
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
        var summonLevel = Math.Clamp(owner.MobaSummonLevel, 1, MaxSummonLevel);
        var damageMul = DamageMultiplierAt(level, rank, summonLevel) * (IsTesting(owner) ? 4f : 1f);
        var health = HealthAt(level, rank, summonLevel);
        var before = monster.Attributes[Stats.MaximumHealth];

        SetAbsolute(monster, Stats.MaximumHealth, health);
        SetAbsolute(monster, Stats.MinimumPhysBaseDmg, BaseMinDamage * damageMul);
        SetAbsolute(monster, Stats.MaximumPhysBaseDmg, BaseMaxDamage * damageMul);
        SetAbsolute(monster, Stats.DefenseBase, BaseDefense + (LevelDefenseBonus * (summonLevel - 1)));
        SetAbsolute(monster, Stats.AttackRatePvm, AttackRate);
        SetAbsolute(monster, Stats.DamageReceiveDecrement, IsTesting(owner) ? 0f : 1f);
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
