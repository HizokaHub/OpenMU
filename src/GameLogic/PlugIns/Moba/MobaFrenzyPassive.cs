// <copyright file="MobaFrenzyPassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Rage Fighter passive "Frenesí": every landed hit grants a stack of attack speed
/// (up to <see cref="MaxStacks"/>) and refreshes the timer; when <see cref="StackWindow"/>
/// lapses with no hit, all stacks fall off at once.
/// </summary>
/// <remarks>
/// On reaching max stacks the next skill hit briefly stuns its target (armed once per
/// climb to max, consumed by the next skill). Magnitudes here are first-pass.
/// </remarks>
public static class MobaFrenzyPassive
{
    /// <summary>Maximum attack-speed stacks.</summary>
    public const int MaxStacks = 5;

    /// <summary>Flat attack speed (AddRaw to <see cref="Stats.AttackSpeedAny"/>) per stack.</summary>
    private const float AttackSpeedPerStack = 8f;

    /// <summary>Stun length applied by the max-stack skill hit.</summary>
    private static readonly TimeSpan StunDuration = TimeSpan.FromMilliseconds(600);

    private static readonly TimeSpan StackWindow = TimeSpan.FromSeconds(3);

    private static readonly ConditionalWeakTable<Player, State> States = new();

    /// <summary>Registers a landed hit by the champion, adding / refreshing a stack.</summary>
    /// <param name="champion">The Rage Fighter champion.</param>
    public static void OnHit(Player champion)
    {
        if (!champion.IsMobaClone)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var state = States.GetOrCreateValue(champion);
        var previousStacks = state.Stacks;
        var expired = (now - state.LastHitUtc) > StackWindow;
        state.Stacks = expired ? 1 : Math.Min(MaxStacks, state.Stacks + 1);
        state.LastHitUtc = now;

        // Arm the stun once, on the climb into max stacks.
        if (state.Stacks >= MaxStacks && previousStacks < MaxStacks)
        {
            state.StunArmed = true;
        }

        Apply(champion, state);
    }

    /// <summary>
    /// Called for a Rage Fighter champion's skill hit: if the frenzy is at max stacks and
    /// the stun is armed, briefly stuns the victim (armed state is consumed).
    /// </summary>
    /// <param name="champion">The Rage Fighter champion.</param>
    /// <param name="victim">The victim of the skill hit.</param>
    public static void OnSkillHit(Player champion, IAttackable victim)
    {
        if (!States.TryGetValue(champion, out var state) || !state.StunArmed)
        {
            return;
        }

        state.StunArmed = false;
        _ = MobaCc.StunAsync(champion, victim, StunDuration);
    }

    /// <summary>Drops the buff from any champion whose stack window has lapsed.</summary>
    public static void SweepExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in States)
        {
            var state = pair.Value;
            if (state.Stacks > 0 && (now - state.LastHitUtc) > StackWindow)
            {
                state.Stacks = 0;
                state.StunArmed = false;
                Apply(pair.Key, state);
            }
        }
    }

    private static void Apply(Player champion, State state)
    {
        if (champion.Attributes is not { } attributes)
        {
            return;
        }

        if (state.Element is { } previous)
        {
            attributes.RemoveElement(previous, Stats.AttackSpeedAny);
            state.Element = null;
        }

        if (state.Stacks > 0)
        {
            var element = new SimpleElement(state.Stacks * AttackSpeedPerStack, AggregateType.AddRaw);
            attributes.AddElement(element, Stats.AttackSpeedAny);
            state.Element = element;
        }
    }

    private sealed class State
    {
        public int Stacks;

        public DateTime LastHitUtc;

        public SimpleElement? Element;

        public bool StunArmed;
    }
}
