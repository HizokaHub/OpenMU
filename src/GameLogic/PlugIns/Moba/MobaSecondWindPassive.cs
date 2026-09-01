// <copyright file="MobaSecondWindPassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Knight passive "Segundo aliento": the first time the champion drops below
/// <see cref="HealthThreshold"/> health it braces for <see cref="Duration"/> - incoming
/// damage is reduced and its own hits leech health - then goes on cooldown.
/// </summary>
/// <remarks>Magnitudes are first-pass (balance pass).</remarks>
public static class MobaSecondWindPassive
{
    /// <summary>Health fraction that arms the passive.</summary>
    private const float HealthThreshold = 0.40f;

    /// <summary>Multiplier applied to <see cref="Stats.DamageReceiveDecrement"/> while braced (0.80 = -20% damage taken).</summary>
    private const float DamageTakenMultiplier = 0.80f;

    /// <summary>Fraction of the damage dealt that is returned as health while braced.</summary>
    private const float LeechFraction = 0.15f;

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(14);

    private static readonly ConditionalWeakTable<Player, State> States = new();

    /// <summary>Called when a Knight champion got hit - arms the brace if low enough and off cooldown.</summary>
    /// <param name="knight">The Knight champion.</param>
    public static void OnGotHit(Player knight)
    {
        if (!knight.IsMobaClone || !knight.IsAlive || knight.Attributes is not { } attributes)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var state = States.GetOrCreateValue(knight);
        if (now < state.BuffUntilUtc || now < state.NextReadyUtc)
        {
            return;
        }

        var maxHealth = attributes[Stats.MaximumHealth];
        if (maxHealth <= 0 || attributes[Stats.CurrentHealth] / maxHealth >= HealthThreshold)
        {
            return;
        }

        var element = new SimpleElement(DamageTakenMultiplier, AggregateType.Multiplicate);
        attributes.AddElement(element, Stats.DamageReceiveDecrement);
        state.Element = element;
        state.BuffUntilUtc = now + Duration;
        state.NextReadyUtc = now + Cooldown;

        _ = knight.ShowBlueMessageAsync("[MOBA] ¡Segundo aliento!");
    }

    /// <summary>Called when a Knight champion dealt a hit - leeches health while braced.</summary>
    /// <param name="knight">The Knight champion.</param>
    /// <param name="hit">The hit info.</param>
    public static void OnDealtHit(Player knight, HitInfo hit)
    {
        if (hit.HealthDamage == 0 || knight.Attributes is not { } attributes)
        {
            return;
        }

        if (!States.TryGetValue(knight, out var state) || DateTime.UtcNow >= state.BuffUntilUtc)
        {
            return;
        }

        var healed = attributes[Stats.CurrentHealth] + (hit.HealthDamage * LeechFraction);
        attributes[Stats.CurrentHealth] = Math.Min(attributes[Stats.MaximumHealth], healed);
    }

    /// <summary>Drops the brace from any champion whose window has lapsed.</summary>
    public static void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in States)
        {
            var state = pair.Value;
            if (state.Element is { } element && now >= state.BuffUntilUtc)
            {
                pair.Key.Attributes?.RemoveElement(element, Stats.DamageReceiveDecrement);
                state.Element = null;
            }
        }
    }

    private sealed class State
    {
        public DateTime BuffUntilUtc;

        public DateTime NextReadyUtc;

        public SimpleElement? Element;
    }
}
