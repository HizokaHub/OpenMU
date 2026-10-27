// <copyright file="MobaSummonSkills.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The skills of the creatures the Elf summons (2026-10-25): each of the seven appearances has one or two. The first one is available from rank 1 of the
/// summon skill, the second one from rank <see cref="SecondSkillRank"/>. A skill is used instead of the basic attack when it is ready and its condition is
/// met; the damage is dealt through the normal attack of the creature with a damage factor, so it uses the same mitigation as any other hit and grows with
/// the rank and the level. The animations are the ones the client already has for the skill numbers used.
/// </summary>
public static class MobaSummonSkills
{
    /// <summary>The rank of the summon skill from which the creature uses its second skill.</summary>
    public const int SecondSkillRank = 3;

    // Appearances (the skill numbers of the Elf kit, see MobaLoadouts.ElfSummons).
    private const short Goblin = 30;
    private const short StoneGolem = 31;
    private const short Assassin = 32;
    private const short EliteYeti = 33;
    private const short DarkKnight = 34;
    private const short Bali = 35;
    private const short Soldier = 36;

    // Client skill numbers used for the animations.
    private const short FireBall = 4;
    private const short Ice = 7;
    private const short TripleShot = 24;
    private const short Cyclone = 22;
    private const short EarthShake = 62;

    /// <summary>The reach in tiles the creature wants to have to its target: ranged creatures stay back.</summary>
    /// <param name="appearance">The appearance (skill number 30-36).</param>
    /// <param name="baseReach">The reach of its basic attack.</param>
    /// <returns>The reach.</returns>
    public static int ReachOf(short appearance, int baseReach) => appearance switch
    {
        Bali => Math.Max(baseReach, 6),
        Soldier => Math.Max(baseReach, 5),
        Assassin => Math.Max(baseReach, 2),
        _ => baseReach,
    };

    /// <summary>The names of the skills of an appearance, for the messages and the logs.</summary>
    /// <param name="appearance">The appearance.</param>
    /// <returns>The names (one or two).</returns>
    public static string[] NamesOf(short appearance) => appearance switch
    {
        Goblin => new[] { "Rapidez" },
        StoneGolem => new[] { "Golpe sísmico", "Piel de piedra" },
        Assassin => new[] { "Puñalada", "Salto" },
        EliteYeti => new[] { "Aliento helado", "Piel de escarcha" },
        DarkKnight => new[] { "Tajo giratorio", "Grito de guerra" },
        Bali => new[] { "Bola de fuego" },
        Soldier => new[] { "Disparo múltiple" },
        _ => Array.Empty<string>(),
    };

    /// <summary>One skill of a creature, as the owner sees it in the control mode.</summary>
    /// <param name="Key">The name of its cooldown.</param>
    /// <param name="Name">Its name.</param>
    /// <param name="CooldownSeconds">Its cooldown.</param>
    /// <param name="MinRank">The rank of the summon skill from which the creature has it.</param>
    /// <param name="Self">Whether it needs no target (it affects the creature itself).</param>
    /// <param name="Range">The tiles to its target from which it is cast.</param>
    public readonly record struct SkillInfo(string Key, string Name, double CooldownSeconds, int MinRank, bool Self, int Range);

    /// <summary>The skills of an appearance (one or two): the first one is on key 1, the second one on key 2 of the control mode.</summary>
    /// <param name="appearance">The appearance (skill number 30-36).</param>
    /// <returns>The skills.</returns>
    public static IReadOnlyList<SkillInfo> SkillsOf(short appearance) => appearance switch
    {
        Goblin => new[] { new SkillInfo("rush", "Rapidez", 14, 1, true, 0) },
        StoneGolem => new[] { new SkillInfo("quake", "Golpe sísmico", 9, 1, false, 3), new SkillInfo("stoneskin", "Piel de piedra", 20, SecondSkillRank, true, 0) },
        Assassin => new[] { new SkillInfo("stab", "Puñalada", 7, 1, false, 3), new SkillInfo("leap", "Salto", 10, SecondSkillRank, false, 8) },
        EliteYeti => new[] { new SkillInfo("frostbreath", "Aliento helado", 10, 1, false, 4), new SkillInfo("frostskin", "Piel de escarcha", 18, SecondSkillRank, true, 0) },
        DarkKnight => new[] { new SkillInfo("spin", "Tajo giratorio", 7, 1, false, 3), new SkillInfo("warcry", "Grito de guerra", 16, SecondSkillRank, true, 0) },
        Bali => new[] { new SkillInfo("fireball", "Bola de fuego", 5, 1, false, 7) },
        Soldier => new[] { new SkillInfo("volley", "Disparo múltiple", 7, 1, false, 6) },
        _ => Array.Empty<SkillInfo>(),
    };

    /// <summary>
    /// Casts one of the skills on the order of the owner (the cooldown is checked and armed by the caller). Returns <see langword="false"/> when the skill needs
    /// a target and there is none.
    /// </summary>
    /// <param name="brain">The brain of the creature.</param>
    /// <param name="appearance">The appearance.</param>
    /// <param name="slot">The skill (0 or 1).</param>
    /// <param name="target">The target, if the skill needs one.</param>
    /// <returns><see langword="true"/> if the skill was cast.</returns>
    public static async ValueTask<bool> CastAsync(MobaSummonIntelligence brain, short appearance, int slot, IAttackable? target)
    {
        var skills = SkillsOf(appearance);
        if (slot < 0 || slot >= skills.Count || (!skills[slot].Self && target is null))
        {
            return false;
        }

        try
        {
            switch (skills[slot].Key)
            {
                case "rush":
                    Rush(brain);
                    break;
                case "quake":
                    await QuakeAsync(brain, target!).ConfigureAwait(false);
                    break;
                case "stoneskin":
                    StoneSkin(brain);
                    break;
                case "stab":
                    await StabAsync(brain, target!).ConfigureAwait(false);
                    break;
                case "leap":
                    await LeapAsync(brain, target!).ConfigureAwait(false);
                    break;
                case "frostbreath":
                    await FrostBreathAsync(brain, target!).ConfigureAwait(false);
                    break;
                case "frostskin":
                    Heal(brain.Monster, 0.12f);
                    break;
                case "spin":
                    await SpinAsync(brain, target!).ConfigureAwait(false);
                    break;
                case "warcry":
                    Empower(brain.Monster, 1.3f, 6, brain.Token);
                    break;
                case "fireball":
                    await FireballAsync(brain, target!).ConfigureAwait(false);
                    break;
                case "volley":
                    await VolleyAsync(brain, target!).ConfigureAwait(false);
                    break;
                default:
                    return false;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Tries to use a skill instead of the basic attack (called when the creature is about to attack, or is near enough to its target to use one).</summary>
    /// <param name="brain">The brain of the creature.</param>
    /// <param name="target">The target.</param>
    /// <param name="appearance">The appearance.</param>
    /// <param name="rank">The rank of the summon skill of the owner.</param>
    /// <returns><see langword="true"/> if a skill replaced the basic attack.</returns>
    public static async ValueTask<bool> TryUseAsync(MobaSummonIntelligence brain, IAttackable target, short appearance, int rank)
    {
        // The owner who commands the creature casts its skills himself (keys 1 and 2 of the control mode).
        if (brain.Controlled)
        {
            return false;
        }

        try
        {
            var monster = brain.Monster;
            var second = rank >= SecondSkillRank;
            var distance = monster.GetDistanceTo(target.Position);
            switch (appearance)
            {
                case Goblin:
                    if (brain.Ready("rush", 14))
                    {
                        Rush(brain);
                    }

                    return false;
                case StoneGolem:
                    if (second && HealthFraction(monster) < 0.65 && brain.Ready("stoneskin", 20))
                    {
                        StoneSkin(brain);
                        return false;
                    }

                    if (distance <= 3 && brain.Ready("quake", 9))
                    {
                        await QuakeAsync(brain, target).ConfigureAwait(false);
                        return true;
                    }

                    return false;
                case Assassin:
                    if (second && distance is > 3 and <= 8 && brain.Ready("leap", 10))
                    {
                        await LeapAsync(brain, target).ConfigureAwait(false);
                        return true;
                    }

                    if (distance <= 3 && brain.Ready("stab", 7))
                    {
                        await StabAsync(brain, target).ConfigureAwait(false);
                        return true;
                    }

                    return false;
                case EliteYeti:
                    if (second && HealthFraction(monster) < 0.5 && brain.Ready("frostskin", 18))
                    {
                        Heal(monster, 0.12f);
                        return false;
                    }

                    if (distance <= 4 && brain.Ready("frostbreath", 10))
                    {
                        await FrostBreathAsync(brain, target).ConfigureAwait(false);
                        return true;
                    }

                    return false;
                case DarkKnight:
                    if (second && brain.Ready("warcry", 16))
                    {
                        Empower(monster, 1.3f, 6, brain.Token);
                        return false;
                    }

                    if (distance <= 3 && brain.Ready("spin", 7))
                    {
                        await SpinAsync(brain, target).ConfigureAwait(false);
                        return true;
                    }

                    return false;
                case Bali:
                    if (distance <= 7 && brain.Ready("fireball", 5))
                    {
                        await FireballAsync(brain, target).ConfigureAwait(false);
                        return true;
                    }

                    return false;
                case Soldier:
                    if (distance <= 6 && brain.Ready("volley", 7))
                    {
                        await VolleyAsync(brain, target).ConfigureAwait(false);
                        return true;
                    }

                    return false;
                default:
                    return false;
            }
        }
        catch
        {
            // best effort: a failing skill falls back to the basic attack
            return false;
        }
    }

    private static void Rush(MobaSummonIntelligence brain) => brain.HasteUntilUtc = DateTime.UtcNow.AddSeconds(6);

    private static void StoneSkin(MobaSummonIntelligence brain) => Shield(brain.Monster, 0.5f, 5, brain.Token);

    private static async ValueTask QuakeAsync(MobaSummonIntelligence brain, IAttackable target)
    {
        await ShowSkillAsync(brain.Monster, target, EarthShake).ConfigureAwait(false);
        await AreaAsync(brain, brain.Monster.Position, 3, 1.0, TimeSpan.FromSeconds(0.8)).ConfigureAwait(false);
    }

    private static async ValueTask StabAsync(MobaSummonIntelligence brain, IAttackable target)
    {
        await ShowSkillAsync(brain.Monster, target, 43).ConfigureAwait(false);
        await HitAsync(brain.Monster, target, 2.0).ConfigureAwait(false);
        Bleed(brain.Monster, target, 0.25, 3, brain.Token);
    }

    private static async ValueTask LeapAsync(MobaSummonIntelligence brain, IAttackable target)
    {
        if (brain.Monster.CurrentMap is { } map)
        {
            await brain.Monster.MoveAsync(map.Terrain.GetRandomCoordinate(target.Position, 1)).ConfigureAwait(false);
        }
    }

    private static async ValueTask FrostBreathAsync(MobaSummonIntelligence brain, IAttackable target)
    {
        await ShowSkillAsync(brain.Monster, target, Ice).ConfigureAwait(false);
        await AreaAsync(brain, target.Position, 3, 1.1, TimeSpan.FromSeconds(0.6)).ConfigureAwait(false);
    }

    private static async ValueTask SpinAsync(MobaSummonIntelligence brain, IAttackable target)
    {
        await ShowSkillAsync(brain.Monster, target, Cyclone).ConfigureAwait(false);
        await AreaAsync(brain, brain.Monster.Position, 3, 1.2, null).ConfigureAwait(false);
    }

    private static async ValueTask FireballAsync(MobaSummonIntelligence brain, IAttackable target)
    {
        await ShowSkillAsync(brain.Monster, target, FireBall).ConfigureAwait(false);
        await HitAsync(brain.Monster, target, 1.4).ConfigureAwait(false);
        Bleed(brain.Monster, target, 0.2, 3, brain.Token);
    }

    private static async ValueTask VolleyAsync(MobaSummonIntelligence brain, IAttackable target)
    {
        await ShowSkillAsync(brain.Monster, target, TripleShot).ConfigureAwait(false);
        var victims = brain.EnemiesAround(target.Position, 4).OrderBy(v => v.GetDistanceTo(target.Position)).Take(3).ToList();
        if (!victims.Contains(target))
        {
            victims.Insert(0, target);
        }

        foreach (var victim in victims.Take(3))
        {
            await HitAsync(brain.Monster, victim, 0.9).ConfigureAwait(false);
        }
    }

    private static double HealthFraction(Monster monster)
    {
        var max = monster.Attributes[Stats.MaximumHealth];
        return max <= 0 ? 1.0 : monster.Attributes[Stats.CurrentHealth] / max;
    }

    private static void Heal(Monster monster, float fraction)
    {
        var max = monster.Attributes[Stats.MaximumHealth];
        monster.Attributes[Stats.CurrentHealth] = Math.Min(max, monster.Attributes[Stats.CurrentHealth] + (max * fraction));
    }

    private static ValueTask ShowSkillAsync(Monster monster, IAttackable target, short skill)
        => monster.ForEachWorldObserverAsync<IShowSkillAnimationPlugIn>(p => p.ShowSkillAnimationAsync(monster, target, skill, true), true);

    private static async ValueTask HitAsync(Monster monster, IAttackable victim, double factor)
    {
        if (victim.IsAlive)
        {
            await victim.AttackByAsync(monster, null, false, factor).ConfigureAwait(false);
        }
    }

    private static async ValueTask AreaAsync(MobaSummonIntelligence brain, Point center, int radius, double factor, TimeSpan? stun)
    {
        foreach (var victim in brain.EnemiesAround(center, radius).ToList())
        {
            await HitAsync(brain.Monster, victim, factor).ConfigureAwait(false);
            if (stun is { } duration && victim is Player { IsMobaClone: true } champion)
            {
                await MobaCc.StunAsync(brain.Owner, champion, duration).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Damage over time: <paramref name="ticks"/> hits of <paramref name="factor"/> one second apart.</summary>
    private static void Bleed(Monster monster, IAttackable victim, double factor, int ticks, CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < ticks; i++)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    if (!monster.IsAlive || !victim.IsAlive)
                    {
                        return;
                    }

                    await victim.AttackByAsync(monster, null, false, factor).ConfigureAwait(false);
                }
            }
            catch
            {
                // the creature died or the match ended
            }
        });
    }

    /// <summary>Takes <paramref name="multiplier"/> of the damage the creature receives for some seconds.</summary>
    private static void Shield(Monster monster, float multiplier, int seconds, CancellationToken token)
        => Timed(monster, new SimpleElement(multiplier, AggregateType.Multiplicate), Stats.DamageReceiveDecrement, seconds, token);

    /// <summary>Makes the creature hit harder for some seconds.</summary>
    private static void Empower(Monster monster, float multiplier, int seconds, CancellationToken token)
        => Timed(monster, new SimpleElement(multiplier, AggregateType.Multiplicate), Stats.AttackDamageIncrease, seconds, token);

    private static void Timed(Monster monster, IElement element, AttributeDefinition stat, int seconds, CancellationToken token)
    {
        monster.Attributes.AddElement(element, stat);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(seconds), token).ConfigureAwait(false);
            }
            catch
            {
                // the match ended
            }

            monster.Attributes.RemoveElement(element, stat);
        });
    }
}
