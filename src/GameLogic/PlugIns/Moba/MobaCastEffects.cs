// <copyright file="MobaCastEffects.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Effects that fire when a MOBA champion casts a skill (from <c>TryConsumeForSkillAsync</c>):
/// a short recovery lock on heavy skills (wind-up telegraph), and a decaying temporary
/// shield on the defensive buff skills.
/// </summary>
public static class MobaCastEffects
{
    /// <summary>Heavy skills: number -> post-cast self-root in ms (you are exposed right after the big hit).</summary>
    private static readonly Dictionary<short, int> RecoveryLockMs = new()
    {
        [40] = 550,  // Nova
        [42] = 400,  // Rageful Blow
        [232] = 450, // Strike of Destruction
        [43] = 350,  // Death Stab
        [65] = 400,  // Electric Spike
        [237] = 500, // Gigantic Storm
    };

    /// <summary>Shield skills: number -> (shield as fraction of max HP, seconds it lasts).</summary>
    private static readonly Dictionary<short, (double MaxHpFraction, double Seconds)> ShieldSkills = new()
    {
        [27] = (0.18, 3.0), // Greater Defense
        [26] = (0.12, 3.0), // Heal (also a small shield here)
        [16] = (0.22, 3.0), // Soul Barrier
        [18] = (0.15, 3.0), // Defense (BK)
    };

    private static readonly ConditionalWeakTable<Player, ShieldState> Shields = new();

    private sealed class ShieldState
    {
        public SimpleElement? MaxElement;

        public DateTime ExpiresUtc;
    }

    /// <summary>Applies the cast effects for one skill cast.</summary>
    /// <param name="champion">The casting champion.</param>
    /// <param name="skill">The skill that was cast.</param>
    public static async ValueTask OnCastAsync(Player champion, Skill skill)
    {
        var number = (short)skill.Number;

        if (RecoveryLockMs.TryGetValue(number, out var lockMs))
        {
            // A short self-stun AFTER the cast reads as wind-up / recovery frames.
            _ = DelayedSelfStunAsync(champion, TimeSpan.FromMilliseconds(lockMs));
        }

        if (ShieldSkills.TryGetValue(number, out var shield) && champion.Attributes is { } a)
        {
            var amount = (float)(a[Stats.MaximumHealth] * shield.MaxHpFraction);
            var state = Shields.GetOrCreateValue(champion);

            // Refresh: drop the previous grant first.
            RemoveShield(a, state);

            // MaximumShield is composable; CurrentShield is a raw value - set it directly.
            state.MaxElement = new SimpleElement(amount, AggregateType.AddRaw);
            a.AddElement(state.MaxElement, Stats.MaximumShield);
            a[Stats.CurrentShield] = Math.Min(a[Stats.MaximumShield], a[Stats.CurrentShield] + amount);
            state.ExpiresUtc = DateTime.UtcNow.AddSeconds(shield.Seconds);
        }

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>Called from the match tick: expires temp shields whose timer has run out.</summary>
    /// <param name="gameContext">The game context.</param>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        try
        {
            var now = DateTime.UtcNow;
            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            foreach (var champion in players.Where(p => p.IsMobaClone))
            {
                if (Shields.TryGetValue(champion, out var state)
                    && state.MaxElement is not null
                    && now >= state.ExpiresUtc
                    && champion.Attributes is { } a)
                {
                    RemoveShield(a, state);
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private static void RemoveShield(IAttributeSystem a, ShieldState state)
    {
        if (state.MaxElement is not null)
        {
            a.RemoveElement(state.MaxElement, Stats.MaximumShield);
            a[Stats.CurrentShield] = Math.Min(a[Stats.CurrentShield], a[Stats.MaximumShield]);
        }

        state.MaxElement = null;
    }

    private static async Task DelayedSelfStunAsync(Player champion, TimeSpan duration)
    {
        try
        {
            await Task.Delay(60).ConfigureAwait(false); // let the cast's own hit resolve first
            if (champion.IsAlive)
            {
                await MobaCc.StunAsync(champion, champion, duration).ConfigureAwait(false);
            }
        }
        catch
        {
            // best effort
        }
    }
}
