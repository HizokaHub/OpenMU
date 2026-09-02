// <copyright file="MobaCc.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

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

    /// <summary>Attributes that count as a hard "can't act" crowd control.</summary>
    private static readonly AttributeDefinition[] HardCcAttributes = { Stats.IsStunned, Stats.IsFrozen, Stats.IsAsleep };

    /// <summary>
    /// Sweeps every MOBA champion's active magic effects and shortens any hard-CC effect
    /// (freeze / stun / sleep) whose remaining duration exceeds <see cref="MaxHardCc"/>.
    /// Call from the match tick.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public static async ValueTask CapCrowdControlAsync(IGameContext gameContext)
    {
        try
        {
            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            foreach (var champion in players.Where(p => p.IsMobaClone))
            {
                if (champion.MagicEffectList is not { } list)
                {
                    continue;
                }

                foreach (var effect in list.ActiveEffects.Values.ToArray())
                {
                    if (effect.Duration <= MaxHardCc)
                    {
                        continue;
                    }

                    var isHardCc = effect.PowerUpElements.Any(e => Array.IndexOf(HardCcAttributes, e.Target) >= 0);
                    if (isHardCc)
                    {
                        effect.Duration = MaxHardCc;
                        effect.ResetTimer();
                    }
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
