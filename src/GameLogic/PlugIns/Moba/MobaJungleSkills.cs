// <copyright file="MobaJungleSkills.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Special attacks of the jungle bosses (2026-10-17). The animation and effects are the ones the client already has for each model: Medusa, Kundun and
/// the Dark Phoenix cast ordinary skill numbers (packet 0x19, the client draws the skill of that number), Selupan casts its nine monster skills
/// (34-42, packet 0x69, <see cref="IMobaMonsterSkillPlugIn"/>). The damage is always dealt by the server through the normal attack of the monster
/// with a damage factor, so it uses the same mitigation as any other monster hit.
/// </summary>
public static class MobaJungleSkills
{
    // Client skill numbers (AT_SKILL_* in _enum.h).
    private const short EvilSpirit = 9;

    private const short Poison = 1;

    private const short Decay = 38;

    private const short BossFire = 50;

    private const short FireSlash = 55;

    private const short MonsterSummon = 200;

    private const short MonsterMagicDefense = 201;

    private const short MonsterPhysicalDefense = 202;

    private const short GiganticStorm = 237;

    private const short ChaoticDiseier = 238;

    // Selupan monster skills (ATMON_SKILL_SERUFAN_* in _enum.h).
    private const int SelupanPoisonGem = 34;

    private const int SelupanCoolStorm = 35;

    private const int SelupanCoolShock = 36;

    private const int SelupanFalling = 37;

    private const int SelupanSummon = 38;

    private const int SelupanHeal = 39;

    private const int SelupanFreeze = 40;

    private const int SelupanTeleport = 41;

    private const int SelupanSuperman = 42;

    private const int MaxMinions = 4;

    private const int MaxSelupanMinions = 6;

    /// <summary>Tries to use a special attack instead of the basic one (called when the target is in attack range).</summary>
    /// <param name="owner">The AI of the monster.</param>
    /// <param name="target">The target.</param>
    /// <returns><see langword="true"/> if a special attack was used.</returns>
    public static async ValueTask<bool> TryUseAsync(MobaJungleIntelligence owner, IAttackable target)
    {
        try
        {
            return owner.Kind switch
            {
                MobaJungle.Kind.Medusa => await MedusaAsync(owner, target).ConfigureAwait(false),
                MobaJungle.Kind.Kundun => await KundunAsync(owner, target).ConfigureAwait(false),
                MobaJungle.Kind.DarkPhoenix => await PhoenixAsync(owner, target).ConfigureAwait(false),
                MobaJungle.Kind.Selupan => await SelupanAsync(owner, target).ConfigureAwait(false),
                _ => false,
            };
        }
        catch
        {
            // a failed special attack must never stop the AI
            return false;
        }
    }

    /// <summary>Called when the monster has to walk to its target: Selupan teleports next to it instead.</summary>
    /// <param name="owner">The AI of the monster.</param>
    /// <param name="target">The target.</param>
    /// <returns><see langword="true"/> if the monster did something instead of walking.</returns>
    public static async ValueTask<bool> TryApproachAsync(MobaJungleIntelligence owner, IAttackable target)
    {
        if (owner.Kind != MobaJungle.Kind.Selupan || !Ready(owner, "teleport", 8))
        {
            return false;
        }

        try
        {
            var monster = owner.Monster;
            var spot = FreeCellNear(target.Position, 2, monster.Position);
            if (spot is null || spot.Value.EuclideanDistanceTo(owner.Home) > MobaJungleIntelligence.LeashTiles)
            {
                return false;
            }

            await ShowMonsterSkillAsync(monster, target, SelupanTeleport).ConfigureAwait(false);
            await monster.MoveAsync(spot.Value).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async ValueTask<bool> MedusaAsync(MobaJungleIntelligence owner, IAttackable target)
    {
        var monster = owner.Monster;

        // Gigantic Storm: a storm on the target, area, stuns for a moment.
        if (Ready(owner, "storm", 14))
        {
            await ShowSkillAsync(monster, target, GiganticStorm).ConfigureAwait(false);
            foreach (var victim in Victims(monster, target.Position, 5))
            {
                await HitAsync(monster, victim, 1.2).ConfigureAwait(false);
                await MobaCc.StunAsync(victim, victim, TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            }

            return true;
        }

        // Chaotic Diseier: a burst around herself.
        if (Ready(owner, "chaotic", 10))
        {
            await ShowSkillAsync(monster, target, ChaoticDiseier).ConfigureAwait(false);
            foreach (var victim in Victims(monster, monster.Position, 4))
            {
                await HitAsync(monster, victim, 1.5).ConfigureAwait(false);
            }

            return true;
        }

        // Decay: a poison cloud on the target.
        if (Ready(owner, "decay", 8))
        {
            await ShowSkillAsync(monster, target, Decay).ConfigureAwait(false);
            foreach (var victim in Victims(monster, target.Position, 3))
            {
                await HitAsync(monster, victim, 1.0).ConfigureAwait(false);
                StartDamageOverTime(monster, victim, 0.02, 5);
            }

            return true;
        }

        // Evil Spirit: the spirits hit the target.
        if (Ready(owner, "spirit", 5))
        {
            await ShowSkillAsync(monster, target, EvilSpirit).ConfigureAwait(false);
            await HitAsync(monster, target, 1.3).ConfigureAwait(false);
            return true;
        }

        return false;
    }

    private static async ValueTask<bool> KundunAsync(MobaJungleIntelligence owner, IAttackable target)
    {
        var monster = owner.Monster;
        var health = HealthFraction(monster);

        if (health < 0.4 && Ready(owner, "physdef", 20))
        {
            await ShowSkillAsync(monster, monster, MonsterPhysicalDefense).ConfigureAwait(false);
            Heal(monster, 0.04f);
            return true;
        }

        if (health < 0.6 && Ready(owner, "magicdef", 20))
        {
            await ShowSkillAsync(monster, monster, MonsterMagicDefense).ConfigureAwait(false);
            Heal(monster, 0.04f);
            return true;
        }

        if (health < 0.8 && Ready(owner, "summon", 25) && owner.Minions.Count(m => m.IsAlive) < MaxMinions)
        {
            await ShowSkillAsync(monster, monster, MonsterSummon).ConfigureAwait(false);
            await SummonAsync(owner, MobaJungle.Kind.BloodyOrc, 2, target).ConfigureAwait(false);
            return true;
        }

        // Poison: fire and poison over the targets around.
        if (Ready(owner, "poison", 8))
        {
            await ShowSkillAsync(monster, target, Poison).ConfigureAwait(false);
            foreach (var victim in Victims(monster, target.Position, 3))
            {
                await HitAsync(monster, victim, 1.2).ConfigureAwait(false);
                StartDamageOverTime(monster, victim, 0.02, 5);
            }

            return true;
        }

        // Fire Slash: a heavy blow that burns.
        if (Ready(owner, "fireslash", 6))
        {
            await ShowSkillAsync(monster, target, FireSlash).ConfigureAwait(false);
            await HitAsync(monster, target, 1.6).ConfigureAwait(false);
            if (target is Player burned)
            {
                StartDamageOverTime(monster, burned, 0.03, 4);
            }

            return true;
        }

        return false;
    }

    private static async ValueTask<bool> PhoenixAsync(MobaJungleIntelligence owner, IAttackable target)
    {
        var monster = owner.Monster;

        // Fire storm around the phoenix; twice as often once it is below half its life.
        if (Ready(owner, "firestorm", HealthFraction(monster) < 0.5 ? 4.5 : 9))
        {
            await ShowSkillAsync(monster, target, BossFire).ConfigureAwait(false);
            foreach (var victim in Victims(monster, monster.Position, 6))
            {
                await HitAsync(monster, victim, 1.4).ConfigureAwait(false);
            }

            return true;
        }

        return false;
    }

    private static async ValueTask<bool> SelupanAsync(MobaJungleIntelligence owner, IAttackable target)
    {
        var monster = owner.Monster;
        var health = HealthFraction(monster);

        // Superman: once, below 30 % life, it hits much harder for a while.
        if (health < 0.3 && !owner.Enraged)
        {
            owner.Enraged = true;
            await ShowMonsterSkillAsync(monster, target, SelupanSuperman).ConfigureAwait(false);
            var minimum = new SimpleElement(monster.Attributes[Stats.MinimumPhysBaseDmg] * 0.5f, AggregateType.AddRaw);
            var maximum = new SimpleElement(monster.Attributes[Stats.MaximumPhysBaseDmg] * 0.5f, AggregateType.AddRaw);
            monster.Attributes.AddElement(minimum, Stats.MinimumPhysBaseDmg);
            monster.Attributes.AddElement(maximum, Stats.MaximumPhysBaseDmg);
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(15), MobaJungle.TokenOf(monster.CurrentMap.MapId)).ConfigureAwait(false);
                    monster.Attributes.RemoveElement(minimum, Stats.MinimumPhysBaseDmg);
                    monster.Attributes.RemoveElement(maximum, Stats.MaximumPhysBaseDmg);
                }
                catch
                {
                    // the match ended
                }
            });
            return true;
        }

        if (health < 0.5 && Ready(owner, "heal", 25))
        {
            await ShowMonsterSkillAsync(monster, target, SelupanHeal).ConfigureAwait(false);
            Heal(monster, 0.08f);
            return true;
        }

        if (health < 0.75 && Ready(owner, "summon", 30) && owner.Minions.Count(m => m.IsAlive) < MaxSelupanMinions)
        {
            await ShowMonsterSkillAsync(monster, target, SelupanSummon).ConfigureAwait(false);
            await SummonAsync(owner, MobaJungle.Kind.Spider, 3, target).ConfigureAwait(false);
            return true;
        }

        if (health < 0.85 && Ready(owner, "falling", 20) && target.GetDistanceTo(monster.Position) >= 2 && FreeCellNear(target.Position, 1, monster.Position) is { } landing
            && landing.EuclideanDistanceTo(owner.Home) <= MobaJungleIntelligence.LeashTiles)
        {
            await ShowMonsterSkillAsync(monster, target, SelupanFalling).ConfigureAwait(false);
            await monster.MoveAsync(landing).ConfigureAwait(false);
            foreach (var victim in Victims(monster, monster.Position, 4))
            {
                await HitAsync(monster, victim, 2.0).ConfigureAwait(false);
            }

            return true;
        }

        if (health < 0.65 && Ready(owner, "freeze", 15))
        {
            await ShowMonsterSkillAsync(monster, target, SelupanFreeze).ConfigureAwait(false);
            await HitAsync(monster, target, 1.0).ConfigureAwait(false);
            if (target is Player frozen)
            {
                await MobaCc.StunAsync(frozen, frozen, TimeSpan.FromSeconds(2.5)).ConfigureAwait(false);
            }

            return true;
        }

        if (Ready(owner, "coolshock", 12))
        {
            await ShowMonsterSkillAsync(monster, target, SelupanCoolShock).ConfigureAwait(false);
            foreach (var victim in Victims(monster, monster.Position, 5))
            {
                await HitAsync(monster, victim, 1.1).ConfigureAwait(false);
                await MobaCc.StunAsync(victim, victim, TimeSpan.FromSeconds(1.5)).ConfigureAwait(false);
            }

            return true;
        }

        if (Ready(owner, "coolstorm", 9))
        {
            await ShowMonsterSkillAsync(monster, target, SelupanCoolStorm).ConfigureAwait(false);
            foreach (var victim in Victims(monster, target.Position, 3))
            {
                await HitAsync(monster, victim, 1.3).ConfigureAwait(false);
                await MobaCc.StunAsync(victim, victim, TimeSpan.FromSeconds(0.8)).ConfigureAwait(false);
            }

            return true;
        }

        // Basic attack: the poison gem, ranged, with poison.
        await ShowMonsterSkillAsync(monster, target, SelupanPoisonGem).ConfigureAwait(false);
        await HitAsync(monster, target, 1.0).ConfigureAwait(false);
        if (target is Player poisoned)
        {
            StartDamageOverTime(monster, poisoned, 0.015, 4);
        }

        return true;
    }

    private static bool Ready(MobaJungleIntelligence owner, string name, double cooldownSeconds)
    {
        var now = DateTime.UtcNow;
        if (owner.SkillReadyAt.TryGetValue(name, out var readyAt) && now < readyAt)
        {
            return false;
        }

        owner.SkillReadyAt[name] = now + TimeSpan.FromSeconds(cooldownSeconds);
        return true;
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

    private static List<Player> Victims(Monster monster, Point center, int radius)
    {
        return monster.CurrentMap!.GetAttackablesInRange(center, radius)
            .OfType<Player>()
            .Where(p => p.IsMobaClone && p.IsAlive && !p.IsAtSafezone())
            .ToList();
    }

    private static async ValueTask HitAsync(Monster monster, IAttackable victim, double factor)
    {
        if (victim.IsAlive)
        {
            await victim.AttackByAsync(monster, null, false, factor).ConfigureAwait(false);
        }
    }

    private static ValueTask ShowSkillAsync(Monster monster, IAttackable target, short skill)
        => monster.ForEachWorldObserverAsync<IShowSkillAnimationPlugIn>(p => p.ShowSkillAnimationAsync(monster, target, skill, true), true);

    private static ValueTask ShowMonsterSkillAsync(Monster monster, IAttackable target, int skill)
        => monster.ForEachWorldObserverAsync<IMobaMonsterSkillPlugIn>(p => p.ShowMonsterSkillAsync(monster, target, skill), true);

    /// <summary>Burns / poisons a champion: <paramref name="fractionOfMaxHealth"/> of its maximum life every second for <paramref name="seconds"/> seconds (true damage).</summary>
    private static void StartDamageOverTime(Monster monster, Player victim, double fractionOfMaxHealth, int seconds)
    {
        var token = MobaJungle.TokenOf(monster.CurrentMap!.MapId);
        _ = Task.Run(async () =>
        {
            try
            {
                for (var i = 0; i < seconds; i++)
                {
                    await Task.Delay(1000, token).ConfigureAwait(false);
                    if (!victim.IsAlive)
                    {
                        return;
                    }

                    var damage = (uint)Math.Max(1, victim.Attributes![Stats.MaximumHealth] * fractionOfMaxHealth);
                    await victim.ApplyPoisonDamageAsync(monster, damage).ConfigureAwait(false);
                }
            }
            catch
            {
                // the match ended or the champion left
            }
        });
    }

    private static async ValueTask SummonAsync(MobaJungleIntelligence owner, MobaJungle.Kind kind, int count, IAttackable target)
    {
        if (owner.GameContext is not { } gameContext || owner.Monster.CurrentMap is not { } map)
        {
            return;
        }

        owner.Minions.RemoveAll(m => !m.IsAlive);
        var group = new MobaJungleGroup();
        for (var i = 0; i < count; i++)
        {
            var spot = FreeCellNear(owner.Monster.Position, 3, null);
            if (spot is null)
            {
                continue;
            }

            var slot = new MobaJungleFormation.Slot(spot.Value.X, spot.Value.Y, 0, owner.Monster.Rotation);
            var minion = await MobaJungle.SpawnMonsterAsync(map, gameContext, kind, slot, group, spot.Value, null, rewards: false).ConfigureAwait(false);
            if (minion is not null)
            {
                owner.Minions.Add(minion);
            }
        }

        if (target is Player { IsMobaClone: true })
        {
            group.Aggro(target);
        }
    }

    /// <summary>A walkable cell within <paramref name="radius"/> tiles of a position, preferably the one nearest to <paramref name="toward"/>.</summary>
    private static Point? FreeCellNear(Point center, int radius, Point? toward)
    {
        Point? best = null;
        var bestScore = double.MaxValue;
        for (var dy = -radius; dy <= radius; dy++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = center.X + dx;
                var y = center.Y + dy;
                if ((dx == 0 && dy == 0) || !MobaLayout.IsWalkable(x, y))
                {
                    continue;
                }

                var cell = new Point((byte)x, (byte)y);
                var score = toward is { } t ? cell.EuclideanDistanceTo(t) : Random.Shared.NextDouble();
                if (score < bestScore)
                {
                    bestScore = score;
                    best = cell;
                }
            }
        }

        return best;
    }
}
