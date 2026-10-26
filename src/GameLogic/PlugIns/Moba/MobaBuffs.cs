// <copyright file="MobaBuffs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Attributes;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The duration of the class buffs in a match (2026-10-25, decided with the user: between 45 and 130 seconds). The S6 durations run from
/// 4 seconds (Defense) to 30 minutes (Expansion of Wizardry) and, for most buffs, grow with the Energy of the caster, which means nothing
/// with the flat stats of a champion. <see cref="EnsureConfigured"/> rewrites them in the configuration in memory (like
/// <see cref="MobaPets"/>); the cooldowns are in <see cref="MobaCooldowns"/>.
/// </summary>
public static class MobaBuffs
{
    /// <summary>Shortest buff of a match, in seconds.</summary>
    public const float MinSeconds = 45;

    /// <summary>Longest buff of a match, in seconds.</summary>
    public const float MaxSeconds = 130;

    /// <summary>
    /// The damage taken while Defense lasts (the S6 buff halves it, but only for 4 seconds; for 45 seconds that would make a Blade Knight
    /// untouchable), so the cut is 20 %.
    /// </summary>
    public const float DefenseDamageTaken = 0.8f;

    /// <summary>Defense: the skill number.</summary>
    private const short Defense = 18;

    private static readonly object ConfigLock = new();

    private static GameConfiguration? _configuredFor;

    /// <summary>Gets the duration in seconds of every buff with a flat duration (heals and Recovery act at once and have none).</summary>
    public static IReadOnlyDictionary<short, float> DurationSeconds { get; } = new Dictionary<short, float>
    {
        [16] = 60,    // Soul Barrier
        [233] = 90,   // Expansion of Wizardry
        [18] = 45,    // Defense
        [48] = 90,    // Swell Life
        [27] = 60,    // Greater Defense
        [28] = 60,    // Greater Damage
        [77] = 130,   // Infinity Arrow
        [64] = 60,    // Increase Critical Damage
        [217] = 45,   // Damage Reflection
        [218] = 60,   // Berserker
        [266] = 45,   // Ignore Defense
        [267] = 90,   // Increase Health
        [268] = 60,   // Increase Block (crowd-control resistance in a match)
    };

    /// <summary>
    /// Sets the duration of the buffs (and the strength of Defense) in the configuration, once per configuration.
    /// </summary>
    /// <param name="configuration">The game configuration.</param>
    public static void EnsureConfigured(GameConfiguration configuration)
    {
        if (ReferenceEquals(_configuredFor, configuration))
        {
            return;
        }

        lock (ConfigLock)
        {
            if (ReferenceEquals(_configuredFor, configuration))
            {
                return;
            }

            foreach (var (number, seconds) in DurationSeconds)
            {
                if (configuration.Skills.FirstOrDefault(s => s.Number == number)?.MagicEffectDef is not { } effect)
                {
                    continue;
                }

                SetFlat(effect.Duration, seconds);
                SetFlat(effect.DurationPvp, seconds);
            }

            // Defense: a milder cut for a much longer time.
            if (configuration.Skills.FirstOrDefault(s => s.Number == Defense)?.MagicEffectDef is { } defense)
            {
                foreach (var powerUp in defense.PowerUpDefinitions.Concat(defense.PowerUpDefinitionsPvp))
                {
                    if (powerUp.TargetAttribute == Stats.DamageReceiveDecrement && powerUp.Boost is { } boost)
                    {
                        boost.ConstantValue.Value = DefenseDamageTaken;
                    }
                }
            }

            _configuredFor = configuration;
        }
    }

    private static void SetFlat(PowerUpDefinitionValue? duration, float seconds)
    {
        if (duration is null)
        {
            return;
        }

        duration.ConstantValue.Value = seconds;
        duration.RelatedValues.Clear();
        duration.MaximumValue = seconds;
    }
}
