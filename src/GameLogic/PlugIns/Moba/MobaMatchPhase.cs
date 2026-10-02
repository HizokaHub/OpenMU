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

    private static MobaShopTier _current = MobaShopTier.T1;

    /// <summary>Gets the current creep-drop phase (never regresses within a match).</summary>
    public static MobaShopTier Current => _current;

    /// <summary>Re-evaluates the phase from the champions currently in the match. Cheap; call from the MOBA tick.</summary>
    /// <param name="champions">Every MOBA champion currently in the match.</param>
    public static void Evaluate(IReadOnlyCollection<Player> champions)
    {
        var before = _current;
        if (_current < MobaShopTier.T3 && BothTeamsReach(champions, T3LevelThreshold))
        {
            _current = MobaShopTier.T3;
        }
        else if (_current < MobaShopTier.T2 && BothTeamsReach(champions, T2LevelThreshold))
        {
            _current = MobaShopTier.T2;
        }

        if (_current != before)
        {
            MobaStructureSpawner.ApplyPhaseHealth(_current);
        }
    }

    /// <summary>Resets the phase to T1 (call when a new match starts on the arena).</summary>
    public static void Reset()
    {
        _current = MobaShopTier.T1;
        MobaDropReport.Reset();
        MobaStructureSpawner.ApplyPhaseHealth(_current);
    }

    private static bool BothTeamsReach(IReadOnlyCollection<Player> champions, int level)
        => CountAtLevel(champions, MobaTeam.Blue, level) >= ChampionsRequiredPerTeam
           && CountAtLevel(champions, MobaTeam.Red, level) >= ChampionsRequiredPerTeam;

    private static int CountAtLevel(IReadOnlyCollection<Player> champions, MobaTeam team, int level)
        => champions.Count(c => MobaTeams.GetTeam(c) == team && c.MobaLevel >= level);
}
