// <copyright file="MobaStatEconomy.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Stat-point economy for MOBA champions. A champion earns the same number of points
/// every level (linear); the total over 30 levels is deliberately less than what is
/// needed to max every stat, so the stat build - like the skill tree - is a personal
/// choice. The Dark Lord (5 stats, uses CMD) and the Rage Fighter / Summoner get more.
/// </summary>
public static class MobaStatEconomy
{
    /// <summary>Hard cap per single stat (STR / AGI / VIT / ENE / CMD).</summary>
    public const int MaxPerStat = 30_000;

    /// <summary>Points a fresh clone starts with, for testing (roughly one maxed stat).</summary>
    public const int TestStartPoints = 30_000;

    private const int NormalTotal = 100_000;

    private const int RageFighterSummonerTotal = 112_000;

    private const int DarkLordTotal = 130_000;

    /// <summary>Points granted per champion level for the given champion's class.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>Points per level.</returns>
    public static int PointsPerLevel(Player champion)
        => PointsPerLevelForClass(champion.SelectedCharacter?.CharacterClass?.Number ?? 0);

    /// <summary>Points granted per champion level for a class number.</summary>
    /// <param name="classNumber">The character class number.</param>
    /// <returns>Points per level.</returns>
    public static int PointsPerLevelForClass(byte classNumber)
    {
        var total = MobaPassives.FamilyOf(classNumber) switch
        {
            MobaFamily.DarkLord => DarkLordTotal,
            MobaFamily.RageFighter or MobaFamily.Summoner => RageFighterSummonerTotal,
            _ => NormalTotal,
        };

        // Levels 2..30 grant points (29 level-ups); level 1 starts at 0 in real play.
        return total / Math.Max(1, MobaLevels.MaxLevel - 1);
    }
}
