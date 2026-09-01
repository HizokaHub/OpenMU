// <copyright file="MobaCommandBannerPassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;

/// <summary>
/// Dark Lord passive "Estandarte de mando": a permanent aura. Allied lane creeps within
/// <see cref="AuraRadius"/> tiles deal <see cref="CreepDamageMultiplier"/> damage; allied
/// champions in range gain <see cref="ChampionAttackSpeedMultiplier"/> attack speed.
/// Re-evaluated once a second from <see cref="MobaPassives.TickAsync"/>.
/// </summary>
/// <remarks>Magnitudes are first-pass (balance pass).</remarks>
public static class MobaCommandBannerPassive
{
    /// <summary>Aura radius in tiles.</summary>
    private const int AuraRadius = 6;

    /// <summary>Damage multiplier (Multiplicate) granted to allied creeps in range.</summary>
    private const float CreepDamageMultiplier = 1.12f;

    /// <summary>Attack-speed multiplier (Multiplicate) granted to allied champions in range.</summary>
    private const float ChampionAttackSpeedMultiplier = 1.05f;

    private static readonly ConditionalWeakTable<IAttackable, AuraBuff> Buffs = new();

    private static long _tick;

    /// <summary>Refreshes the aura: applies the buff to allies who entered range, drops it from those who left.</summary>
    /// <param name="gameContext">The game context.</param>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        _tick++;

        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        var darkLords = players
            .Where(p => p is { IsMobaClone: true, IsAlive: true } && p.CurrentMap is not null
                        && MobaPassives.FamilyOf(p) == MobaFamily.DarkLord
                        && MobaTeams.GetTeam(p) != MobaTeam.None)
            .ToList();

        foreach (var darkLord in darkLords)
        {
            var map = darkLord.CurrentMap!;
            foreach (var ally in map.GetAttackablesInRange(darkLord.Position, AuraRadius))
            {
                if (!ally.IsAlive || !MobaTeams.AreAllies(darkLord, ally))
                {
                    continue;
                }

                if (ally is Monster monster && !MobaStructures.IsStructure(monster))
                {
                    EnsureBuffed(ally, creep: true);
                }
                else if (ally is Player { IsMobaClone: true })
                {
                    EnsureBuffed(ally, creep: false);
                }
            }
        }

        // Drop the buff from anyone not in range of a Dark Lord this tick. Collect first -
        // Remove() mutates the ConditionalWeakTable, which must not happen mid-enumeration.
        List<IAttackable>? stale = null;
        foreach (var pair in Buffs)
        {
            if (pair.Value.Tick != _tick)
            {
                (stale ??= new List<IAttackable>()).Add(pair.Key);
            }
        }

        if (stale is not null)
        {
            foreach (var target in stale)
            {
                if (Buffs.TryGetValue(target, out var buff))
                {
                    Remove(target, buff);
                }
            }
        }
    }

    private static void EnsureBuffed(IAttackable target, bool creep)
    {
        var buff = Buffs.GetOrCreateValue(target);
        if (buff.Tick == _tick)
        {
            return; // already handled this tick (e.g. near two Dark Lords)
        }

        buff.Tick = _tick;
        if (buff.Applied.Count > 0)
        {
            return; // already buffed on a previous tick - keep it, the refreshed Tick spares it from the sweep
        }

        if (creep)
        {
            Apply(target, buff, Stats.MinimumPhysBaseDmg, CreepDamageMultiplier);
            Apply(target, buff, Stats.MaximumPhysBaseDmg, CreepDamageMultiplier);
        }
        else
        {
            Apply(target, buff, Stats.AttackSpeedAny, ChampionAttackSpeedMultiplier);
        }
    }

    private static void Apply(IAttackable target, AuraBuff buff, AttributeDefinition attribute, float multiplier)
    {
        var element = new SimpleElement(multiplier, AggregateType.Multiplicate);
        target.Attributes.AddElement(element, attribute);
        buff.Applied.Add((element, attribute));
    }

    private static void Remove(IAttackable target, AuraBuff buff)
    {
        foreach (var (element, attribute) in buff.Applied)
        {
            target.Attributes.RemoveElement(element, attribute);
        }

        buff.Applied.Clear();
        Buffs.Remove(target);
    }

    private sealed class AuraBuff
    {
        public long Tick;

        public List<(SimpleElement Element, AttributeDefinition Attribute)> Applied { get; } = new();
    }
}
