// <copyright file="MobaItemCaps.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Hard caps on the item-boostable attributes that don't already have a knob of their own
/// (attack speed, max HP/mana%) so stacking gear can't run away with them - crit and
/// "excellent hit" chance are capped inline in <see cref="MobaCombatStats"/> /
/// <see cref="AttackableExtensions"/> instead, since those already roll through a single
/// read point. These two don't (attack speed and max HP/mana are aggregated live by the
/// attribute system from every equipped piece + buff), so the cap is re-applied every MOBA
/// tick (see <see cref="MobaPassives.TickAsync"/>): read the champion's current aggregated
/// value and, if over the cap, top up a single compensating AddRaw element (replaced every
/// time, never stacked) to pull it back down - the same idiom as <see cref="MobaFrenzyPassive"/>.
/// First-pass numbers; tune with <c>/mobabotfight</c>.
/// </summary>
public static class MobaItemCaps
{
    /// <summary>Hard cap on the final <see cref="Stats.AttackSpeed"/> for a MOBA champion.</summary>
    public const float MaxAttackSpeed = 70f;

    /// <summary>
    /// Hard cap on how far items may push max HP / mana above the champion-level curve
    /// (<see cref="MobaProgression"/>), e.g. 1.20 = the curve's value +20%.
    /// </summary>
    public const float MaxResourceMultiplier = 1.20f;

    private static readonly ConditionalWeakTable<Player, State> States = new();

    /// <summary>Re-applies the caps for one champion. Cheap; called from the MOBA tick.</summary>
    /// <param name="champion">The champion.</param>
    public static void Apply(Player champion)
    {
        if (!champion.IsMobaClone || champion.Attributes is not { } attributes)
        {
            return;
        }

        var state = States.GetOrCreateValue(champion);
        CapAttribute(attributes, Stats.AttackSpeed, MaxAttackSpeed, ref state.AttackSpeedElement);

        var (hpMul, _) = MobaProgression.RoleTilt(MobaPassives.FamilyOf(champion));
        var maxHealthCap = MobaProgression.HealthAt(champion.MobaLevel) * hpMul * MaxResourceMultiplier;
        CapAttribute(attributes, Stats.MaximumHealth, maxHealthCap, ref state.HealthElement);

        var maxManaCap = MobaProgression.ManaAt(champion.MobaLevel) * MaxResourceMultiplier;
        CapAttribute(attributes, Stats.MaximumMana, maxManaCap, ref state.ManaElement);
    }

    private static void CapAttribute(IAttributeSystem attributes, AttributeDefinition stat, float cap, ref IElement? tracked)
    {
        if (tracked is { } previous)
        {
            attributes.RemoveElement(previous, stat);
            tracked = null;
        }

        var current = attributes[stat];
        if (current > cap)
        {
            var element = new SimpleElement(cap - current, AggregateType.AddRaw);
            attributes.AddElement(element, stat);
            tracked = element;
        }
    }

    private sealed class State
    {
        public IElement? AttackSpeedElement;

        public IElement? HealthElement;

        public IElement? ManaElement;
    }
}
