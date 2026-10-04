// <copyright file="MobaDefense.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// MOBA mitigation: a champion's invested VIT (and later items) buys a PERCENT damage
/// reduction with diminishing returns, and a handful of skills carry armour penetration.
/// Layered on top of <see cref="MobaSkillDamage"/> in <c>CalculateDamageAsync</c>.
/// </summary>
public static class MobaDefense
{
    /// <summary>VIT at which mitigation reaches half of <see cref="MaxMitigation"/> (diminishing-returns constant).</summary>
    private const double VitHalfPoint = 12_000;

    /// <summary>Hard cap on the VIT-derived percent mitigation.</summary>
    private const double MaxMitigation = 0.70;

    /// <summary>Everyone has this much base mitigation even with zero VIT invested (base armour) - stops pure glass-cannon one-shots.</summary>
    private const double BaseMitigation = 0.12;

    /// <summary>
    /// No single non-true hit may remove more than this fraction of the victim's max HP
    /// (HP + shield damage combined). This is the hard anti-one-shot rule: it guarantees a
    /// champion always survives at least a few hits, so fights last seconds, not frames.
    /// </summary>
    private const double MaxHitFractionOfMaxHp = 0.30;

    /// <summary>Length of the sliding window of the burst brake.</summary>
    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(2);

    private const double BurstFreeFraction = 0.30;

    /// <summary>Between <see cref="BurstFreeFraction"/> and this fraction a hit counts <see cref="BurstMidShare"/>; beyond, <see cref="BurstTailShare"/>.</summary>
    private const double BurstMidFraction = 0.50;

    private const double BurstMidShare = 0.50;

    private const double BurstTailShare = 0.20;

    private static readonly ConditionalWeakTable<Player, BurstState> Bursts = new();

    /// <summary>Last mitigation applied per defender (for the [MOBA-DMG] trace): raw pre-mitigation, final, and the effective mitigation fraction.</summary>
    public static readonly ConditionalWeakTable<Player, MitigationTrace> LastMitigation = new();

    private sealed class BurstState
    {
        public Queue<(long At, int Amount)> Hits { get; } = new();

        public double Sum { get; set; }
    }

    /// <summary>One recorded mitigation step, read back by the combat trace on the same hit.</summary>
    public sealed class MitigationTrace
    {
        /// <summary>Gets or sets the pre-mitigation damage.</summary>
        public int Raw { get; set; }

        /// <summary>Gets or sets the post-mitigation damage.</summary>
        public int Final { get; set; }

        /// <summary>Gets or sets the effective mitigation fraction after penetration (0..1).</summary>
        public double Fraction { get; set; }

        /// <summary>Gets or sets the UTC time this step was recorded.</summary>
        public DateTime WhenUtc { get; set; }
    }

    /// <summary>Per-skill armour penetration (fraction of the target's mitigation ignored), by Persistence skill number.</summary>
    private static readonly Dictionary<short, double> PenetrationBySkill = new()
    {
        [52] = 0.55,  // Penetration (the whole point of the skill)
        [43] = 0.40,  // Death Stab
        [232] = 0.35, // Strike of Destruction
        [65] = 0.30,  // Electric Spike
        [263] = 0.35, // Dark Side
        [270] = 0.30, // Phoenix Shot
        [42] = 0.20,  // Rageful Blow
    };

    /// <summary>
    /// The fraction of incoming damage a champion mitigates from invested VIT alone,
    /// 0..<see cref="MaxMitigation"/>. This is the stat-build term ("A") blended in
    /// <see cref="FinalMitigationOf"/> - use that one for actual damage mitigation.
    /// </summary>
    /// <param name="defender">The defending champion.</param>
    /// <returns>The mitigation fraction.</returns>
    public static double MitigationOf(Player defender)
    {
        if (defender.Attributes is not { } a)
        {
            return 0;
        }

        var investedVit = Math.Max(0, a[Stats.TotalVitality] - MobaCloneFactory.BaselineStatValue);
        var fromVit = (MaxMitigation - BaseMitigation) * (investedVit / (investedVit + VitHalfPoint));
        return BaseMitigation + fromVit;
    }

    /// <summary>
    /// The champion's final mitigation fraction: the VIT-only term ("A", <see cref="MitigationOf"/>)
    /// blended with the item-defense term ("B", <see cref="MobaItemPower.DefenseFractionOf"/> scaled
    /// to the same 0..<see cref="MaxMitigation"/> range), weighted by <see cref="MobaMasterTree.Blend(double?, double, double)"/>
    /// (45 % stats, 20 % master tree defense nodes, 35 % items).
    /// </summary>
    /// <param name="defender">The defending champion.</param>
    /// <returns>The final mitigation fraction, 0..<see cref="MaxMitigation"/>.</returns>
    public static double FinalMitigationOf(Player defender)
    {
        var fromStats = MitigationOf(defender);
        var fromItems = MobaItemPower.DefenseFractionOf(defender) * MaxMitigation;
        var fromTree = MobaMasterTree.Fraction(defender, MobaTreeKind.Defense) * MaxMitigation;
        return Math.Clamp(MobaMasterTree.Blend(fromTree, fromStats, fromItems), 0.0, MaxMitigation);
    }

    /// <summary>Per-hit attribution of the victim's mitigation for the burst analysis ([MOBA-DMG+]).</summary>
    /// <param name="defender">The defending champion.</param>
    /// <returns>A compact tag with the stat / tree / item terms and the final mitigation.</returns>
    public static string AttributionTag(Player defender)
    {
        var tree = MobaMasterTree.Fraction(defender, MobaTreeKind.Defense);
        return FormattableString.Invariant($"stats={MitigationOf(defender):P0} tree={(tree is { } t ? (t * MaxMitigation).ToString("P0", System.Globalization.CultureInfo.InvariantCulture) : "n/a")} items={MobaItemPower.DefenseFractionOf(defender) * MaxMitigation:P0} final={FinalMitigationOf(defender):P0}");
    }

    /// <summary>
    /// The burst brake (2026-10-02, tightened 2026-10-04): the damage a champion takes inside a 2 s window counts in full up to
    /// 30 % of its HP + SD pool, at 50 % up to 50 % of the pool and at 20 % beyond, so a focus-fire combo can no longer delete
    /// a champion from ~85 % in two seconds while sustained damage over longer windows is untouched.
    /// </summary>
    private static int ApplyBurstBrake(Player defender, int damage, float maxHp)
    {
        if (maxHp <= 0 || damage <= 0)
        {
            return damage;
        }

        var state = Bursts.GetOrCreateValue(defender);
        lock (state)
        {
            var now = Environment.TickCount64;
            while (state.Hits.Count > 0 && now - state.Hits.Peek().At > BurstWindow.TotalMilliseconds)
            {
                state.Sum -= state.Hits.Dequeue().Amount;
            }

            // The window keeps the pre-brake damage; the brake is the difference of the counting curve.
            var free = maxHp * BurstFreeFraction;
            var mid = maxHp * BurstMidFraction;
            double Counted(double x) => x <= free
                ? x
                : x <= mid
                    ? free + ((x - free) * BurstMidShare)
                    : free + ((mid - free) * BurstMidShare) + ((x - mid) * BurstTailShare);

            var braked = Math.Max(1, (int)(Counted(state.Sum + damage) - Counted(state.Sum)));
            state.Hits.Enqueue((now, damage));
            state.Sum += damage;
            return braked;
        }
    }

    /// <summary>
    /// Applies MOBA mitigation to a raw damage value: reduces it by the defender's final
    /// (stat + item blend) mitigation, minus the casting skill's armour penetration.
    /// </summary>
    /// <param name="rawDamage">The pre-mitigation damage.</param>
    /// <param name="defender">The defending champion.</param>
    /// <param name="skillNumber">The skill number, or 0 for a basic attack.</param>
    /// <param name="critMultiplier">The critical multiplier, applied before the per-hit cap and the burst brake (1 = no crit).</param>
    /// <returns>The post-mitigation damage (at least 1 if the input was positive).</returns>
    public static int Apply(int rawDamage, Player defender, short skillNumber, double critMultiplier = 1.0)
    {
        if (rawDamage <= 0)
        {
            return rawDamage;
        }

        var mitigation = FinalMitigationOf(defender);
        if (skillNumber != 0 && PenetrationBySkill.TryGetValue(skillNumber, out var pen))
        {
            mitigation *= 1.0 - pen;
        }

        var durability = MobaMasterTree.DurabilityDamageReduction * (MobaMasterTree.Fraction(defender, MobaTreeKind.Durability) ?? 0.0);
        var final = Math.Max(1, (int)(rawDamage * (1.0 - mitigation) * (1.0 - durability) * critMultiplier));

        // Anti-one-shot: no single hit removes more than a fixed fraction of the target's
        // max HP. Big burst still wins fights - it just can't delete a champion in one frame.
        var capped = final;
        if (defender.Attributes is { } da)
        {
            var hitCap = (int)(da[Stats.MaximumHealth] * MaxHitFractionOfMaxHp);
            if (hitCap > 0 && capped > hitCap)
            {
                capped = hitCap;
            }
        }

        if (defender.Attributes is { } ba)
        {
            capped = ApplyBurstBrake(defender, capped, ba[Stats.MaximumHealth] + ba[Stats.MaximumShield]);
        }

        var trace = LastMitigation.GetOrCreateValue(defender);
        trace.Raw = rawDamage;
        trace.Final = capped;
        trace.Fraction = mitigation;
        trace.WhenUtc = DateTime.UtcNow;

        return capped;
    }
}
