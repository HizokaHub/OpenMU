// <copyright file="IMobaResultsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// One champion's line of the results screen.
/// </summary>
/// <param name="Name">The champion's name.</param>
/// <param name="Team">The champion's team.</param>
/// <param name="BaseClass">The base class index (0 Dark Wizard, 1 Dark Knight, 2 Elf, 3 Magic Gladiator, 4 Dark Lord, 5 Summoner, 6 Rage Fighter).</param>
/// <param name="IsBot">Whether a bot played the champion.</param>
/// <param name="Level">The champion level at the end.</param>
/// <param name="Kills">The kills.</param>
/// <param name="Deaths">The deaths.</param>
/// <param name="Assists">The assists.</param>
/// <param name="CreepScore">The minions and jungle monsters last-hit.</param>
/// <param name="Gold">The gold earned.</param>
/// <param name="DamageDealt">The damage dealt to champions.</param>
/// <param name="DamageTaken">The damage taken from champions.</param>
public sealed record MobaResultRow(
    string Name,
    MobaTeam Team,
    byte BaseClass,
    bool IsBot,
    int Level,
    int Kills,
    int Deaths,
    int Assists,
    int CreepScore,
    long Gold,
    long DamageDealt,
    long DamageTaken);

/// <summary>
/// The results of a finished match.
/// </summary>
/// <param name="Winner">The winning team.</param>
/// <param name="DurationSeconds">How long the match lasted.</param>
/// <param name="Rows">The champions, the blue team first.</param>
public sealed record MobaMatchSummary(MobaTeam Winner, int DurationSeconds, IReadOnlyList<MobaResultRow> Rows);

/// <summary>
/// Shows the results panel of the client at the end of a match. The panel's button answers with
/// <c>C1 04 D5 18</c>, which sends the player back to the town.
/// </summary>
public interface IMobaResultsPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the results panel.
    /// </summary>
    /// <param name="summary">The results.</param>
    /// <param name="yourName">The name of the champion of the receiving player (highlighted in the table).</param>
    /// <param name="secondsToReturn">The seconds after which the server sends the player back on its own.</param>
    /// <returns>A task.</returns>
    ValueTask ShowResultsAsync(MobaMatchSummary summary, string yourName, int secondsToReturn);
}
