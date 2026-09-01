// <copyright file="MobaCombustionPassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// Wizard passive "Combustión": every spell that hits an enemy leaves a burn that ticks
/// magic damage, stacking up to <see cref="MaxStacks"/>. Each spell hit refreshes the
/// duration; the burn ticks once a second from <see cref="MobaPassives.TickAsync"/>.
/// </summary>
/// <remarks>
/// The DoT is applied through <c>ApplyPoisonDamageAsync</c> (the engine's DoT primitive) -
/// functionally a burn, cosmetically it shows like poison. Magnitudes are first-pass.
/// </remarks>
public static class MobaCombustionPassive
{
    /// <summary>Maximum burn stacks on a single target.</summary>
    public const int MaxStacks = 3;

    /// <summary>Magic damage per stack, per one-second tick.</summary>
    private const uint DamagePerStackPerTick = 12;

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(1);

    private static readonly ConditionalWeakTable<IAttackable, Burn> Burns = new();

    /// <summary>Applies / refreshes a burn stack from a Wizard champion's spell hit.</summary>
    /// <param name="wizard">The casting Wizard champion.</param>
    /// <param name="victim">The enemy that got hit.</param>
    /// <param name="hit">The hit info (a miss does not ignite).</param>
    public static void OnSpellHit(Player wizard, IAttackable victim, HitInfo hit)
    {
        if (hit.HealthDamage == 0 && hit.ShieldDamage == 0)
        {
            return;
        }

        if (ReferenceEquals(wizard, victim) || MobaTeams.AreAllies(wizard, victim) || !victim.IsAlive)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var burn = Burns.GetOrCreateValue(victim);
        burn.Stacks = burn.ExpiresUtc <= now ? 1 : Math.Min(MaxStacks, burn.Stacks + 1);
        burn.ExpiresUtc = now + Duration;
        burn.Source = wizard;
        if (burn.NextTickUtc <= now)
        {
            burn.NextTickUtc = now + TickInterval;
        }
    }

    /// <summary>Ticks / expires every active burn. Called once a second.</summary>
    public static async ValueTask TickAsync()
    {
        var now = DateTime.UtcNow;
        foreach (var pair in Burns)
        {
            var victim = pair.Key;
            var burn = pair.Value;

            if (burn.Stacks <= 0)
            {
                continue;
            }

            if (burn.ExpiresUtc <= now || !victim.IsAlive || burn.Source is not { IsAlive: true } source)
            {
                burn.Stacks = 0;
                continue;
            }

            if (now < burn.NextTickUtc)
            {
                continue;
            }

            burn.NextTickUtc = now + TickInterval;
            try
            {
                await victim.ApplyPoisonDamageAsync(source, DamagePerStackPerTick * (uint)burn.Stacks).ConfigureAwait(false);
            }
            catch
            {
                burn.Stacks = 0;
            }
        }
    }

    private sealed class Burn
    {
        public int Stacks;

        public DateTime ExpiresUtc;

        public DateTime NextTickUtc;

        public Player? Source;
    }
}
