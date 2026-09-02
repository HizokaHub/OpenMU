// <copyright file="MobaLevels.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Champion level / experience constants for the MOBA mode. All numbers here are the
/// balance knobs for match pacing (target: ~level 30 around 30-40 minutes) - tune in the
/// balance pass.
/// </summary>
public static class MobaLevels
{
    /// <summary>The champion level cap. After this, only gold accumulates.</summary>
    public const int MaxLevel = 30;

    /// <summary>Champion levels at which the player is offered a skill pick (2 choices, keep 1).</summary>
    public static readonly int[] MilestoneLevels = { 5, 10, 15, 20, 25 };

    // --- EXP sources ---

    /// <summary>
    /// EXP to the champion that last-hits an enemy lane creep. Deliberately small: the
    /// passive drip is the steady source, farm is a top-up. Gold (later) is what really
    /// rewards the last hit.
    /// </summary>
    public const int CreepLastHitExp = 10;

    /// <summary>
    /// EXP to every OTHER champion of the killing team within <see cref="ShareRadius"/>
    /// of a dying creep (LoL-style proximity XP - you don't need the last hit). ~30% of
    /// the last-hit value ("70% menos").
    /// </summary>
    public const int CreepProximityExp = 3;

    /// <summary>Tiles around a creep death within which champions of the killing team get proximity EXP.</summary>
    public const int ShareRadius = 12;

    /// <summary>Base EXP for killing an enemy champion, plus <see cref="ChampionKillPerVictimLevel"/> per victim level.</summary>
    public const int ChampionKillExp = 130;

    /// <summary>Extra champion-kill EXP per level of the victim. Small, so killing a fed enemy does not itself snowball.</summary>
    public const int ChampionKillPerVictimLevel = 5;

    /// <summary>EXP for each allied champion near an enemy champion kill (assist).</summary>
    public const int AssistExp = 70;

    /// <summary>EXP granted to every champion of the team that destroyed a turret.</summary>
    public const int TurretKillExp = 220;

    /// <summary>Passive EXP drip per tick to every champion in a match (time-based, not tied to kills). The floor that keeps a losing team levelling.</summary>
    public const int PassiveExpPerTick = 22;

    /// <summary>A champion this many levels below the match leader gets the catch-up EXP bonus.</summary>
    public const int CatchUpLevelGap = 3;

    /// <summary>Catch-up EXP multiplier at the start of the match for a champion <see cref="CatchUpLevelGap"/>+ levels behind.</summary>
    public const double CatchUpExpMultiplierEarly = 2.5;

    /// <summary>
    /// The catch-up bonus decays linearly from <see cref="CatchUpExpMultiplierEarly"/> to
    /// 1.0x over this many minutes, so a comeback is only a leg-up early - by late game a
    /// team that plays well keeps its lead.
    /// </summary>
    public const double CatchUpDecayMinutes = 18;

    /// <summary>The catch-up EXP multiplier for a behind champion at the given match time.</summary>
    /// <param name="matchElapsed">Time since the match started.</param>
    /// <returns>A value between 1.0 and <see cref="CatchUpExpMultiplierEarly"/>.</returns>
    public static double CatchUpExpMultiplier(TimeSpan matchElapsed)
    {
        var t = Math.Clamp(matchElapsed.TotalMinutes / CatchUpDecayMinutes, 0.0, 1.0);
        return CatchUpExpMultiplierEarly + ((1.0 - CatchUpExpMultiplierEarly) * t);
    }

    /// <summary>Seconds between passive EXP ticks.</summary>
    public const double PassiveTickSeconds = 5;

    /// <summary>
    /// EXP required to go from <paramref name="currentLevel"/> to the next level.
    /// Linear ramp; cumulative to level 30 is ~13.5k. With the passive drip alone a
    /// champion reaches ~level 16 by 35 min; farm + kills + turrets carry the rest.
    /// </summary>
    /// <param name="currentLevel">The current champion level.</param>
    /// <returns>The EXP needed for the next level, or <see cref="long.MaxValue"/> at the cap.</returns>
    public static long ExpToNext(int currentLevel)
    {
        if (currentLevel >= MaxLevel)
        {
            return long.MaxValue;
        }

        // Steeper than linear so a fed champion needs disproportionately more per level -
        // it keeps climbing but the gap to a farming opponent stops widening as fast.
        return 90 + (currentLevel * 30) + (currentLevel * currentLevel);
    }

    /// <summary>Whether the given champion level is a skill-pick milestone.</summary>
    /// <param name="level">The champion level.</param>
    /// <returns><see langword="true"/> if a pick is offered at this level.</returns>
    public static bool IsMilestone(int level) => Array.IndexOf(MilestoneLevels, level) >= 0;
}
