// <copyright file="MobaMana.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The MOBA mana economy. Skills cost <see cref="CostMultiplier"/> times their S6 mana cost and the
/// champion regenerates mana at a rate derived from its own loadout: fast enough that, without potions,
/// it is never unable to recast its most expensive skill for more than <see cref="MaxDryWaitSeconds"/>
/// seconds, but never so fast that mana stops mattering (the rate is bounded to a share of the pool).
/// The S6 mana regeneration (about 1 per second) is switched off for champions.
/// </summary>
public static class MobaMana
{
    /// <summary>Factor applied to the S6 mana cost of every skill cast by a champion.</summary>
    public const float CostMultiplier = 3f;

    /// <summary>The longest a champion may be unable to cast its most expensive skill for lack of mana, without potions.</summary>
    public const double MaxDryWaitSeconds = 5;

    /// <summary>Lower bound of the regeneration, as a share of the maximum mana per second (a full pool in 25 s).</summary>
    public const double MinRegenPoolShare = 0.04;

    /// <summary>Upper bound of the regeneration, as a share of the maximum mana per second (a full pool in ~8 s).</summary>
    public const double MaxRegenPoolShare = 0.12;

    /// <summary>Seconds between two <c>[MOBA-MANA]</c> telemetry lines of a human champion.</summary>
    private const double TelemetrySeconds = 5;

    private static readonly ConditionalWeakTable<Player, RegenState> States = new();

    /// <summary>Gets the mana a champion regenerates per second.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>Mana per second.</returns>
    public static double RegenPerSecond(Player champion)
    {
        var pool = champion.Attributes?[Stats.MaximumMana] ?? 0;
        if (pool <= 0)
        {
            return 0;
        }

        var maxCost = MostExpensiveSkillCost(champion);
        var wanted = maxCost / MaxDryWaitSeconds;
        return Math.Clamp(wanted, pool * MinRegenPoolShare, pool * MaxRegenPoolShare);
    }

    /// <summary>The mana cost (after <see cref="CostMultiplier"/>) of the dearest skill the champion has ranked up.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The cost, or 0 without ranked skills.</returns>
    public static double MostExpensiveSkillCost(Player champion)
    {
        var cost = 0d;
        foreach (var entry in champion.SelectedCharacter?.LearnedSkills ?? Enumerable.Empty<MUnique.OpenMU.DataModel.Entities.SkillEntry>())
        {
            if (entry.Level <= 0 || entry.Skill is not { } skill)
            {
                continue;
            }

            var baseCost = skill.ConsumeRequirements.FirstOrDefault(r => r.Attribute == Stats.CurrentMana)?.MinimumValue ?? 0;
            cost = Math.Max(cost, baseCost * CostMultiplier);
        }

        return cost;
    }

    /// <summary>Switches the S6 mana regeneration off for a champion (called when its level scaling is applied).</summary>
    /// <param name="attributes">The champion's attributes.</param>
    public static void DisableNativeRegen(IAttributeSystem attributes)
    {
        attributes.AddElement(new SimpleElement(-attributes[Stats.ManaRecoveryMultiplier], AggregateType.AddRaw), Stats.ManaRecoveryMultiplier);
        attributes.AddElement(new SimpleElement(-attributes[Stats.ManaRecoveryAbsolute], AggregateType.AddRaw), Stats.ManaRecoveryAbsolute);
    }

    /// <summary>Adds the regenerated mana of the time elapsed since the last tick to every living champion.</summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        try
        {
            var now = DateTime.UtcNow;
            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            foreach (var champion in players.Where(p => p.IsMobaClone && p.IsAlive))
            {
                if (champion.Attributes is not { } attributes)
                {
                    continue;
                }

                var state = States.GetOrCreateValue(champion);
                var elapsed = state.LastUtc == default ? 0 : Math.Min((now - state.LastUtc).TotalSeconds, 2);
                state.LastUtc = now;
                var max = attributes[Stats.MaximumMana];
                var before = attributes[Stats.CurrentMana];

                // Mana the champion spent (or drank) since the last tick: what it has now vs. what the last tick left it with.
                var spentOrDrunk = state.LastMana - (double)before;
                if (state.LastMana >= 0)
                {
                    state.SpentOrDrunk += spentOrDrunk;
                }

                if (elapsed > 0 && before < max)
                {
                    state.Carry += RegenPerSecond(champion) * elapsed;
                    var whole = Math.Floor(state.Carry);
                    state.Carry -= whole;
                    var after = (uint)Math.Min(max, before + whole);
                    state.Regenerated += after - before;
                    attributes[Stats.CurrentMana] = after;
                }

                state.LastMana = attributes[Stats.CurrentMana];
                if (champion is not MobaBotPlayer && (now - state.LastLogUtc).TotalSeconds >= TelemetrySeconds)
                {
                    // Only logs while something happened, so an idle full pool doesn't flood the log.
                    if (state.Regenerated > 0 || Math.Abs(state.SpentOrDrunk) > 0.5)
                    {
                        champion.Logger.LogInformation(
                            "[MOBA-MANA] {name}: {cur}/{max} | regen {rate:0.0}/s (priciest skill {cost}) | in the last {secs:0}s: +{regen} regenerated, {spent:0} spent or drunk",
                            champion.SelectedCharacter?.Name,
                            attributes[Stats.CurrentMana],
                            max,
                            RegenPerSecond(champion),
                            MostExpensiveSkillCost(champion),
                            Math.Min((now - state.LastLogUtc).TotalSeconds, 60),
                            state.Regenerated,
                            state.SpentOrDrunk);
                    }

                    state.LastLogUtc = now;
                    state.Regenerated = 0;
                    state.SpentOrDrunk = 0;
                }
            }
        }
        catch
        {
            // best effort
        }
    }

    private sealed class RegenState
    {
        public DateTime LastUtc;

        public double Carry;

        public double LastMana = -1;

        public double Regenerated;

        public double SpentOrDrunk;

        public DateTime LastLogUtc;
    }
}
