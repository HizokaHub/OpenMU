// <copyright file="MobaMatchPhase.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tracks which shop tier (T1/T2/T3) the match's creep drops (see <see cref="MobaCreepDrops"/>)
/// are currently in. A match starts in the T1 phase and advances - never regresses - once
/// <b>both</b> teams have at least 2 champions at the phase's champion-level threshold, so
/// one team being far ahead doesn't unlock T3 drops for the team that's behind too. The
/// thresholds (9 / 22) put the phases at about minute 8 (T2) and 24 (T3) for an average
/// player on the <see cref="MobaLevels.ExpToNext"/> curve.
/// </summary>
public static class MobaMatchPhase
{
    /// <summary>Champion level at which 2+ champions per team unlock T2 creep drops.</summary>
    public const int T2LevelThreshold = 9;

    /// <summary>Champion level at which 2+ champions per team unlock T3 creep drops.</summary>
    public const int T3LevelThreshold = 22;

    /// <summary>Champions per team required at the threshold level to advance the phase.</summary>
    public const int ChampionsRequiredPerTeam = 2;

    /// <summary>Re-evaluates the phase of the arena from the champions currently in its match. Cheap; call from the MOBA tick.</summary>
    /// <param name="arena">The arena.</param>
    /// <param name="champions">Every MOBA champion currently in the match.</param>
    public static void Evaluate(MobaArena arena, IReadOnlyCollection<Player> champions)
    {
        arena.LeaderLevel = Math.Max(1, champions.Select(c => c.MobaLevel).DefaultIfEmpty(1).Max());
        var before = arena.Phase;
        if (arena.Phase < MobaShopTier.T3 && BothTeamsReach(champions, T3LevelThreshold))
        {
            arena.Phase = MobaShopTier.T3;
        }
        else if (arena.Phase < MobaShopTier.T2 && BothTeamsReach(champions, T2LevelThreshold))
        {
            arena.Phase = MobaShopTier.T2;
        }

        if (arena.Phase != before)
        {
            MobaStructureSpawner.ApplyPhaseHealth(arena.Map, arena.Phase);
        }
    }

    /// <summary>Resets the phase and the clock of the arena to T1 / zero (call when a new match starts on it).</summary>
    /// <param name="arena">The arena.</param>
    public static void Reset(MobaArena arena)
    {
        arena.Phase = MobaShopTier.T1;
        arena.LeaderLevel = 1;
        arena.ResetClock();
        MobaDropReport.Reset();
        MobaStructureSpawner.ApplyPhaseHealth(arena.Map, arena.Phase);
    }

    private static bool BothTeamsReach(IReadOnlyCollection<Player> champions, int level)
        => CountAtLevel(champions, MobaTeam.Blue, level) >= ChampionsRequiredPerTeam
           && CountAtLevel(champions, MobaTeam.Red, level) >= ChampionsRequiredPerTeam;

    private static int CountAtLevel(IReadOnlyCollection<Player> champions, MobaTeam team, int level)
        => champions.Count(c => MobaTeams.GetTeam(c) == team && c.MobaLevel >= level);
}
