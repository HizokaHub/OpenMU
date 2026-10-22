// <copyright file="MobaMatchResults.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Builds the results table of a finished match from the champions' match state.
/// </summary>
public static class MobaMatchResults
{
    /// <summary>
    /// Builds the results of a match.
    /// </summary>
    /// <param name="champions">The champions of the match (humans, ghosts and bots).</param>
    /// <param name="winner">The winning team.</param>
    /// <param name="duration">How long the match lasted.</param>
    /// <returns>The summary, the blue team first and each team by kills.</returns>
    public static MobaMatchSummary Build(IEnumerable<Player> champions, MobaTeam winner, TimeSpan duration)
    {
        var rows = champions
            .Where(p => p.IsMobaClone && MobaTeams.GetTeam(p) != MobaTeam.None)
            .Select(RowOf)
            .OrderBy(r => (int)r.Team)
            .ThenByDescending(r => r.Kills)
            .ThenByDescending(r => r.Gold)
            .ToList();
        return new MobaMatchSummary(winner, (int)duration.TotalSeconds, rows);
    }

    private static MobaResultRow RowOf(Player champion)
    {
        var state = champion.MobaState;
        var classNumber = champion.SelectedCharacter?.CharacterClass?.Number ?? 0;
        return new MobaResultRow(
            champion.SelectedCharacter?.Name ?? "?",
            MobaTeams.GetTeam(champion),
            MobaMinimapKind.BaseClassIndex(classNumber),
            champion is MobaBotPlayer,
            state.Level,
            state.Kills,
            state.Deaths,
            state.Assists,
            state.CreepScore,
            state.GoldEarned,
            state.DamageDealt,
            state.DamageTaken);
    }
}
