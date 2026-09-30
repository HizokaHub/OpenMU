// <copyright file="MobaGold.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Grants MOBA match gold (the clone's Zen - see <see cref="MobaCloneFactory"/>,
/// <see cref="MobaShop"/>) and applies the equipped item Zen bonus (Excellent Defense
/// "Zen after kill", <see cref="Stats.MoneyAmountRate"/>) on top. A separate currency/
/// economy from <see cref="MobaExperience"/>/<see cref="MobaLevels"/>: gold buys shop gear
/// mid-match, EXP buys champion levels - see GAMEDESIGN.md "Economía". First-pass numbers;
/// tune with <c>/mobabotfight</c>. The base values are the original ones times the global
/// scale K = 2.7, found with the income simulator (<c>MobaGoldEconomyReport</c>) so that an
/// average player with perfect purchases (reselling the previous tier at 70 %) completes T1
/// by level 8 (~min 8.5), T2 by ~min 23 and T3 by ~min 40.
/// </summary>
public static class MobaGold
{
    /// <summary>Gold to the champion that last-hits an enemy lane creep.</summary>
    public const int CreepLastHitGold = 54;

    /// <summary>Gold to every other nearby champion of the killing team when a creep dies (proximity, no last hit needed).</summary>
    public const int CreepProximityGold = 27;

    /// <summary>Base gold for killing an enemy champion, plus <see cref="ChampionKillGoldPerVictimLevel"/> per victim level.</summary>
    public const int ChampionKillGoldBase = 405;

    /// <summary>Extra champion-kill gold per level of the victim.</summary>
    public const int ChampionKillGoldPerVictimLevel = 22;

    /// <summary>Gold for each allied champion near an enemy champion kill (assist).</summary>
    public const int AssistGold = 160;

    /// <summary>Gold granted every time a champion levels up, on top of the skill point.</summary>
    public const int LevelUpGold = 110;

    /// <summary>Kill streak (since the victim's last death) at which "shutdown gold" starts paying out.</summary>
    public const int ShutdownStreakThreshold = 3;

    /// <summary>Extra gold to the killer per streak-kill of the victim beyond <see cref="ShutdownStreakThreshold"/>.</summary>
    public const int ShutdownGoldPerStreakKill = 95;

    /// <summary>Passive gold per tick (same <see cref="MobaLevels.PassiveTickSeconds"/> cadence as the EXP drip).</summary>
    public const int PassiveGoldPerTick = 27;

    /// <summary>
    /// Passive gold multiplier for a champion <see cref="MobaLevels.CatchUpLevelGap"/>+ levels
    /// behind the match leader (anti-snowball, smaller than the EXP catch-up bonus - gold buys
    /// permanent power, so it stays modest).
    /// </summary>
    public const double BehindPassiveMultiplier = 1.5;

    /// <summary>Gold multiplier during the T1 phase of the match (see <see cref="MobaMatchPhase"/>).</summary>
    public const double PhaseMultiplierT1 = 1.0;

    /// <summary>Gold multiplier during the T2 phase.</summary>
    public const double PhaseMultiplierT2 = 1.0;

    /// <summary>Gold multiplier during the T3 phase.</summary>
    public const double PhaseMultiplierT3 = 2.6;

    private static readonly ConditionalWeakTable<Player, Streak> Streaks = new();

    /// <summary>
    /// Gets the gold multiplier of a match phase. Tier prices grow with the phase, so income
    /// grows with it too - otherwise buying power would only ever come from accumulation.
    /// </summary>
    /// <param name="phase">The match phase.</param>
    /// <returns>The multiplier applied to phase-scaled gold sources.</returns>
    public static double PhaseMultiplierOf(MobaShopTier phase) => phase switch
    {
        MobaShopTier.T2 => PhaseMultiplierT2,
        MobaShopTier.T3 => PhaseMultiplierT3,
        _ => PhaseMultiplierT1,
    };

    /// <summary>Grants gold to a champion, scaled by its equipped item Zen bonus.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="amount">The base gold amount (before the item bonus).</param>
    /// <param name="reason">Short tag for logging.</param>
    /// <param name="scaleByPhase">Whether the amount is multiplied by the match phase (<see cref="PhaseMultiplierOf"/>); champion-kill gold opts out because it already scales with the victim's level.</param>
    public static ValueTask GrantAsync(Player champion, int amount, string reason, bool scaleByPhase = true)
    {
        if (!champion.IsMobaClone || amount <= 0)
        {
            return ValueTask.CompletedTask;
        }

        var itemBonus = champion.Attributes?[Stats.MoneyAmountRate] ?? 1f;
        var phase = scaleByPhase ? PhaseMultiplierOf(MobaMatchPhase.Current) : 1.0;
        var final = (int)Math.Round(amount * phase * Math.Max(1f, itemBonus));
        champion.Money += final;

        champion.Logger.LogDebug(
            "[MOBA-GOLD] {Name} +{Amount} ({Reason}), total={Total}",
            champion.SelectedCharacter?.Name,
            final,
            reason,
            champion.Money);

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Champion-kill gold for the killer (scaled by the victim's level and kill streak -
    /// "shutdown gold" past <see cref="ShutdownStreakThreshold"/>), plus assist gold for
    /// nearby allies. Resets the victim's streak and grows the killer's. Call alongside the
    /// EXP grant in <see cref="MobaExperience.HandleChampionDeathAsync"/>.
    /// </summary>
    /// <param name="killer">The killer.</param>
    /// <param name="victim">The victim.</param>
    /// <param name="assisters">Nearby allied champions of the killer sharing the kill.</param>
    public static async ValueTask GrantChampionKillAsync(Player killer, Player victim, IEnumerable<Player> assisters)
    {
        var victimStreak = Streaks.GetOrCreateValue(victim);
        var gold = ChampionKillGoldBase + (victim.MobaLevel * ChampionKillGoldPerVictimLevel);
        var isShutdown = victimStreak.Count >= ShutdownStreakThreshold;
        if (isShutdown)
        {
            gold += (victimStreak.Count - ShutdownStreakThreshold + 1) * ShutdownGoldPerStreakKill;
        }

        await GrantAsync(killer, gold, isShutdown ? "shutdown" : "champion", scaleByPhase: false).ConfigureAwait(false);

        foreach (var assister in assisters)
        {
            await GrantAsync(assister, AssistGold, "assist").ConfigureAwait(false);
        }

        victimStreak.Count = 0;
        Streaks.GetOrCreateValue(killer).Count++;
    }

    /// <summary>Resets a champion's kill streak (call on death, even without a valid killer).</summary>
    /// <param name="champion">The champion.</param>
    public static void ResetStreak(Player champion) => Streaks.GetOrCreateValue(champion).Count = 0;

    private sealed class Streak
    {
        public int Count;
    }
}
