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

    /// <summary>The extra maximum life while Swell Life lasts (+12 %).</summary>
    public const float SwellLifeHealthBoost = 0.12f;

    /// <summary>Defense: the skill number.</summary>
    private const short Defense = 18;

    /// <summary>Swell Life: the skill number.</summary>
    private const short SwellLife = 48;

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

            StripGaugeCosts(configuration);

            // 2026-11-25 buff rework: the S6 effects below are switched off in the configuration in memory, because the rank-scaled values of MobaBuffPower replace them
            // (Defense and Soul Barrier cut the damage taken, Greater Defense / Greater Damage were flat numbers, Damage Reflection a flat 42 %, Increase Health +133 life,
            // Swell Life +12 % at every rank, Berserker -32 % of the life with no damage bonus in the MOBA damage model).
            foreach (var number in new short[] { Defense, 16, 27, 28, 217, 267, SwellLife })
            {
                if (configuration.Skills.FirstOrDefault(s => s.Number == number)?.MagicEffectDef is not { } effect)
                {
                    continue;
                }

                foreach (var powerUp in effect.PowerUpDefinitions.Concat(effect.PowerUpDefinitionsPvp))
                {
                    var target = powerUp.TargetAttribute;
                    if (powerUp.Boost is { } boost
                        && (target == Stats.DamageReceiveDecrement || target == Stats.SoulBarrierReceiveDecrement || target == Stats.GreaterDefenseBonus || target == Stats.GreaterDamageBonus
                            || target == Stats.DamageReflection || target == Stats.TotalVitality || target == Stats.SwellLifeHealthIncrease))
                    {
                        boost.RelatedValues.Clear();
                        boost.MaximumValue = 0;
                        boost.ConstantValue.Value = target == Stats.DamageReceiveDecrement ? 1f : 0f;
                    }
                }
            }

            // Berserker: the cost is a flat 25 % of the maximum life (the damage bonus is the rank-scaled one of MobaBuffPower).
            if (configuration.Skills.FirstOrDefault(s => s.Number == 218)?.MagicEffectDef is { } berserker)
            {
                foreach (var powerUp in berserker.PowerUpDefinitions.Concat(berserker.PowerUpDefinitionsPvp))
                {
                    if (powerUp.TargetAttribute == Stats.BerserkerHealthDecrement && powerUp.Boost is { } boost)
                    {
                        boost.RelatedValues.Clear();
                        boost.ConstantValue.Value = -(float)MobaBuffPower.BerserkerHealthCost;
                    }
                }
            }

            _configuredFor = configuration;
        }
    }

    // The AG of a Soul Master is reserved for its Teleport (see MobaBlink): the skills only the Wizard family has ask for none.
    private static void StripGaugeCosts(GameConfiguration configuration)
    {
        foreach (var skill in configuration.Skills)
        {
            var wizardOnly = skill.QualifiedCharacters.Count > 0 && skill.QualifiedCharacters.All(c => MobaPassives.FamilyOf(c.Number) == MobaFamily.Wizard);
            if (!wizardOnly)
            {
                continue;
            }

            foreach (var cost in skill.ConsumeRequirements.Where(r => r.Attribute == Stats.CurrentAbility).ToList())
            {
                skill.ConsumeRequirements.Remove(cost);
            }
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
