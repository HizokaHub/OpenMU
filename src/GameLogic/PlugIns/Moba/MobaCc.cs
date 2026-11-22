// <copyright file="MobaCc.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Small crowd-control helpers for MOBA passives / skills. Reuses the engine's stun
/// magic effect (number 61) with an arbitrary duration.
/// </summary>
public static class MobaCc
{
    private const short StunMagicEffectNumber = 61;

    /// <summary>
    /// Hard cap on any single hard-CC (freeze / stun / sleep) on a MOBA champion. S6 "Iced"
    /// and "Cold" last up to 10s, which in a MOBA is a near-permanent lock; capped to this.
    /// </summary>
    private static readonly TimeSpan MaxHardCc = TimeSpan.FromMilliseconds(1400);

    /// <summary>Freeze is only a brief root/interrupt in MOBA (not a lockout) - capped shorter than a stun.</summary>
    private static readonly TimeSpan MaxFreeze = TimeSpan.FromMilliseconds(650);

    /// <summary>Attributes that count as a hard "can't act" crowd control.</summary>
    private static readonly AttributeDefinition[] HardCcAttributes = { Stats.IsStunned, Stats.IsFrozen, Stats.IsAsleep };

    /// <summary>Innate tenacity: a family's hard-CC durations are multiplied by this (tanks shrug off CC).</summary>
    private static double TenacityMul(MobaFamily family) => family switch
    {
        MobaFamily.Knight or MobaFamily.RageFighter => 0.70,
        MobaFamily.DarkLord => 0.85,
        _ => 1.0,
    };

    /// <summary>Per-hit CC-duration factor by consecutive-CC stack: 100%, 60%, 36%, 22%, then 15%.</summary>
    private static readonly double[] DiminishingFactor = { 1.0, 0.60, 0.36, 0.22, 0.15 };

    /// <summary>Stacks reset after this long without any new hard CC.</summary>
    private static readonly TimeSpan DiminishReset = TimeSpan.FromSeconds(7);

    private static readonly ConditionalWeakTable<Player, DrState> DrByChampion = new();

    /// <summary>Hard-CC effects already shortened once, so the tick doesn't re-stack them.</summary>
    private static readonly ConditionalWeakTable<MagicEffect, object> Processed = new();

    private sealed class DrState
    {
        public DateTime ImmuneUntil;

        public int Stacks;

        public DateTime LastCcUtc;
    }

    /// <summary>
    /// Sweeps every MOBA champion's active magic effects and shortens any hard-CC effect
    /// (freeze / stun / sleep). The first CC lasts up to <see cref="MaxHardCc"/>; each
    /// further CC within <see cref="DiminishReset"/> lasts a diminishing fraction of that
    /// (basic tenacity). Call from the match tick.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <summary>
    /// The crowd-control rule of F10 (2026-11-20), applied the moment a hard control (stun, freeze, sleep) lands on a champion, not once a second afterwards:
    /// each control in a row lasts half of the previous one, the champion is immune for <see cref="ImmunitySeconds"/> after it, and the stacks reset after
    /// <see cref="DiminishReset"/> without control. The crowd-control resistance of the class, the pieces and the Increase Block buff shortens every control.
    /// (The old rule let every control below 0.65 s / 1.4 s through and an Elf froze its rival 60-75 % of the duel with them.)
    /// </summary>
    /// <param name="owner">The one that gets the control.</param>
    /// <param name="effect">The magic effect about to be added (its duration is shortened here).</param>
    /// <returns><see langword="false"/> if the champion is immune and the effect must be discarded.</returns>
    public static bool ShapeHardCc(IAttackable owner, MagicEffect effect)
    {
        if (!MobaF10.Enabled || owner is not Player { IsMobaClone: true } champion
            || !effect.PowerUpElements.Any(e => Array.IndexOf(HardCcAttributes, e.Target) >= 0))
        {
            return true;
        }

        var dr = DrByChampion.GetOrCreateValue(champion);
        lock (dr)
        {
            var now = DateTime.UtcNow;
            if (now < dr.ImmuneUntil)
            {
                return false;
            }

            if (now - dr.LastCcUtc > DiminishReset)
            {
                dr.Stacks = 0;
            }

            var resist = Math.Min(0.8, MobaItemTraits.CcResistOf(champion));   // already includes the Increase Block buff
            var factor = Math.Pow(0.5, dr.Stacks) * TenacityMul(MobaPassives.FamilyOf(champion)) * (1.0 - resist);
            var shaped = TimeSpan.FromMilliseconds(Math.Max(100, effect.Duration.TotalMilliseconds * factor));
            effect.Duration = shaped;
            effect.ResetTimer();
            dr.Stacks++;
            dr.LastCcUtc = now;
            dr.ImmuneUntil = now + shaped + TimeSpan.FromSeconds(ImmunitySeconds);
        }

        return true;
    }

    /// <summary>Immunity after a hard control, in seconds.</summary>
    public const double ImmunitySeconds = 1.5;

    public static async ValueTask CapCrowdControlAsync(IGameContext gameContext)
    {
        try
        {
            var now = DateTime.UtcNow;
            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            foreach (var champion in players.Where(p => p.IsMobaClone))
            {
                if (champion.MagicEffectList is not { } list)
                {
                    continue;
                }

                foreach (var effect in list.ActiveEffects.Values.ToArray())
                {
                    if (Processed.TryGetValue(effect, out _))
                    {
                        continue;
                    }

                    var isFreeze = effect.PowerUpElements.Any(e => e.Target == Stats.IsFrozen);
                    var isHardCc = isFreeze || effect.PowerUpElements.Any(e => Array.IndexOf(HardCcAttributes, e.Target) >= 0);

                    // Only long CC counts: short self-stuns (cast wind-up) and already-brief
                    // CC pass through without capping or feeding diminishing returns.
                    var longCc = isHardCc && effect.Duration > (isFreeze ? MaxFreeze : MaxHardCc);
                    if (!longCc)
                    {
                        continue;
                    }

                    Processed.Add(effect, string.Empty);

                    var dr = DrByChampion.GetOrCreateValue(champion);
                    if (now - dr.LastCcUtc > DiminishReset)
                    {
                        dr.Stacks = 0;
                    }

                    var baseCap = isFreeze ? MaxFreeze : MaxHardCc;
                    var tenacity = TenacityMul(MobaPassives.FamilyOf(champion)) * (1 - MobaItemTraits.CcResistOf(champion));
                    var factor = DiminishingFactor[Math.Min(dr.Stacks, DiminishingFactor.Length - 1)] * tenacity;
                    var capped = TimeSpan.FromMilliseconds(baseCap.TotalMilliseconds * factor);
                    var requested = effect.Duration;
                    if (effect.Duration > capped)
                    {
                        effect.Duration = capped;
                        effect.ResetTimer();
                    }

                    MobaTelemetry.NoteCc(null, champion, isFreeze ? "freeze" : "stun/hardCC", requested, effect.Duration, dr.Stacks, tenacity);

                    dr.Stacks++;
                    dr.LastCcUtc = now;
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>
    /// Applies a stun to the target for the given duration (best effort - swallows errors).
    /// </summary>
    /// <param name="source">The champion causing the stun (for the game context / config).</param>
    /// <param name="target">The victim.</param>
    /// <param name="duration">How long the stun lasts.</param>
    public static async ValueTask StunAsync(Player source, IAttackable target, TimeSpan duration)
    {
        try
        {
            if (!target.IsAlive)
            {
                return;
            }

            var definition = source.GameContext.Configuration.MagicEffects.FirstOrDefault(m => m.Number == StunMagicEffectNumber);
            var powerUpDefinition = definition?.PowerUpDefinitions.FirstOrDefault(pu => pu.TargetAttribute == Stats.IsStunned);
            if (definition is null || powerUpDefinition is null)
            {
                return;
            }

            var powerUp = target.Attributes.CreateElement(powerUpDefinition);
            var effect = new MagicEffect(duration, definition, [new MagicEffect.ElementWithTarget(powerUp, Stats.IsStunned)]);
            await target.MagicEffectList.AddEffectAsync(effect).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }
}
