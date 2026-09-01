// <copyright file="MobaCc.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Small crowd-control helpers for MOBA passives / skills. Reuses the engine's stun
/// magic effect (number 61) with an arbitrary duration.
/// </summary>
public static class MobaCc
{
    private const short StunMagicEffectNumber = 61;

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
