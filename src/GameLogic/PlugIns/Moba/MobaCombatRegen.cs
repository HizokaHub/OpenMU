// <copyright file="MobaCombatRegen.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// LoL-style regen rules for MOBA champions: HEALTH and SHIELD do not regenerate while
/// the champion is in combat (took or dealt damage in the last few seconds); they only
/// tick back up once the champion has been out of combat. Mana is unaffected. Driven from
/// the match tick.
/// </summary>
public static class MobaCombatRegen
{
    private static readonly TimeSpan InCombatWindow = TimeSpan.FromSeconds(6);

    private static readonly ConditionalWeakTable<Player, RegenLock> Locks = new();

    private sealed class RegenLock
    {
        public bool Applied;

        public SimpleElement? HealthMul;

        public SimpleElement? HealthAbs;

        public SimpleElement? ShieldMul;

        public SimpleElement? ShieldAbs;
    }

    /// <summary>Applies / lifts the "no HP &amp; SD regen in combat" lock for every MOBA champion.</summary>
    /// <param name="gameContext">The game context.</param>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        try
        {
            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            foreach (var champion in players.Where(p => p.IsMobaClone))
            {
                if (champion.Attributes is not { } attributes)
                {
                    continue;
                }

                // In combat = took OR dealt damage in the window.
                var inCombat = MobaCombatLog.InCombat(champion, InCombatWindow);
                var state = Locks.GetOrCreateValue(champion);

                if (inCombat && !state.Applied)
                {
                    state.HealthMul = Zero(attributes, Stats.HealthRecoveryMultiplier);
                    state.HealthAbs = Zero(attributes, Stats.HealthRecoveryAbsolute);
                    state.ShieldMul = Zero(attributes, Stats.ShieldRecoveryMultiplier);
                    state.ShieldAbs = Zero(attributes, Stats.ShieldRecoveryAbsolute);
                    state.Applied = true;
                }
                else if (!inCombat && state.Applied)
                {
                    Remove(attributes, Stats.HealthRecoveryMultiplier, state.HealthMul);
                    Remove(attributes, Stats.HealthRecoveryAbsolute, state.HealthAbs);
                    Remove(attributes, Stats.ShieldRecoveryMultiplier, state.ShieldMul);
                    Remove(attributes, Stats.ShieldRecoveryAbsolute, state.ShieldAbs);
                    state.HealthMul = state.HealthAbs = state.ShieldMul = state.ShieldAbs = null;
                    state.Applied = false;
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static SimpleElement Zero(IAttributeSystem attributes, AttributeDefinition stat)
    {
        var element = new SimpleElement(-attributes[stat], AggregateType.AddRaw);
        attributes.AddElement(element, stat);
        return element;
    }

    private static void Remove(IAttributeSystem attributes, AttributeDefinition stat, SimpleElement? element)
    {
        if (element is not null)
        {
            attributes.RemoveElement(element, stat);
        }
    }
}
