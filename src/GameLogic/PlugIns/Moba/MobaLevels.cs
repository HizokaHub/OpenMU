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
    /// passive drip is the steady source, farm is a top-up. (Was 42 - hit level 5 in
    /// ~4 waves.)
    /// </summary>
    public const int CreepKillExp = 10;

    /// <summary>Fraction of <see cref="CreepKillExp"/> shared with each allied champion near the kill.</summary>
    public const double CreepKillNearbyShare = 0.5;

    /// <summary>Tiles around a kill within which allied champions get the shared EXP.</summary>
    public const int ShareRadius = 12;

    /// <summary>Base EXP for killing an enemy champion, plus <see cref="ChampionKillPerVictimLevel"/> per victim level.</summary>
    public const int ChampionKillExp = 150;

    /// <summary>Extra champion-kill EXP per level of the victim.</summary>
    public const int ChampionKillPerVictimLevel = 14;

    /// <summary>EXP for each allied champion near an enemy champion kill (assist).</summary>
    public const int AssistExp = 70;

    /// <summary>EXP granted to every champion of the team that destroyed a turret.</summary>
    public const int TurretKillExp = 220;

    /// <summary>Passive EXP drip per tick to every champion in a match (time-based, not tied to kills).</summary>
    public const int PassiveExpPerTick = 10;

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

        return 90 + (currentLevel * 25);
    }

    /// <summary>Whether the given champion level is a skill-pick milestone.</summary>
    /// <param name="level">The champion level.</param>
    /// <returns><see langword="true"/> if a pick is offered at this level.</returns>
    public static bool IsMilestone(int level) => Array.IndexOf(MilestoneLevels, level) >= 0;
}
