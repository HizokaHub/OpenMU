// <copyright file="MobaMatchRecorder.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;

/// <summary>One champion of a finished match, as the ranking of the web stores it.</summary>
/// <param name="AccountLogin">The login of the account (internal; null for bots).</param>
/// <param name="CharacterName">The name of the character.</param>
/// <param name="ClassIndex">The base class, 0-6.</param>
/// <param name="Team">The team, 1 blue and 2 red.</param>
/// <param name="IsBot">Whether it was a bot.</param>
/// <param name="Won">Whether its team won.</param>
/// <param name="Level">The champion level at the end.</param>
/// <param name="Kills">Kills.</param>
/// <param name="Deaths">Deaths.</param>
/// <param name="Assists">Assists.</param>
/// <param name="CreepScore">Minions and monsters killed.</param>
/// <param name="GoldEarned">Gold earned.</param>
/// <param name="DamageDealt">Damage dealt.</param>
/// <param name="DamageTaken">Damage taken.</param>
/// <param name="GoldenKills">Golden invaders killed with the last hit.</param>
public sealed record MobaMatchPlayerRecord(
    string? AccountLogin,
    string CharacterName,
    byte ClassIndex,
    byte Team,
    bool IsBot,
    bool Won,
    int Level,
    int Kills,
    int Deaths,
    int Assists,
    int CreepScore,
    long GoldEarned,
    long DamageDealt,
    long DamageTaken,
    int GoldenKills);

/// <summary>A finished match.</summary>
/// <param name="DurationSeconds">How long it lasted.</param>
/// <param name="WinnerTeam">The winning team, 1 blue and 2 red.</param>
/// <param name="Format">The format of the match (Classic, Aram...).</param>
/// <param name="GoldenInvasion">Whether the golden invasion happened.</param>
/// <param name="Players">Its champions.</param>
public sealed record MobaMatchRecord(int DurationSeconds, byte WinnerTeam, string Format, bool GoldenInvasion, IReadOnlyList<MobaMatchPlayerRecord> Players);

/// <summary>
/// Hands the results of every finished match, and the state of the server, to the persistence of the ranking of the web. The game logic does not know the
/// database of the ranking: the host (Startup) installs <see cref="MatchSink"/> and <see cref="StatusSink"/>. Without them (or without the ranking schema in the
/// database) nothing is recorded and the game works as always.
/// </summary>
public static class MobaMatchRecorder
{
    private static readonly TimeSpan StatusEvery = TimeSpan.FromSeconds(15);

    private static DateTime _lastStatusUtc = DateTime.MinValue;

    /// <summary>Gets or sets what stores a finished match (installed by the host).</summary>
    public static Func<MobaMatchRecord, Task>? MatchSink { get; set; }

    /// <summary>Gets or sets what stores the state of the server: source, human players, champions in a match (installed by the host).</summary>
    public static Func<string, int, int, Task>? StatusSink { get; set; }

    /// <summary>Records a finished match (fire and forget; failures are logged and never reach the game).</summary>
    /// <param name="champions">The champions of the match (humans, ghosts and bots).</param>
    /// <param name="winner">The winning team.</param>
    /// <param name="duration">How long it lasted.</param>
    /// <param name="format">The format.</param>
    /// <param name="goldenInvasion">Whether the golden invasion happened.</param>
    public static void RecordMatch(IEnumerable<Player> champions, MobaTeam winner, TimeSpan duration, string format, bool goldenInvasion)
    {
        var sink = MatchSink;
        if (sink is null)
        {
            return;
        }

        try
        {
            var players = champions
                .Where(p => p.IsMobaClone && MobaTeams.GetTeam(p) != MobaTeam.None)
                .Select(p => new MobaMatchPlayerRecord(
                    p is MobaBotPlayer ? null : p.Account?.LoginName,
                    p.SelectedCharacter?.Name ?? "?",
                    MobaMinimapKind.BaseClassIndex(p.SelectedCharacter?.CharacterClass?.Number ?? 0),
                    (byte)MobaTeams.GetTeam(p),
                    p is MobaBotPlayer,
                    MobaTeams.GetTeam(p) == winner,
                    p.MobaState.Level,
                    p.MobaState.Kills,
                    p.MobaState.Deaths,
                    p.MobaState.Assists,
                    p.MobaState.CreepScore,
                    p.MobaState.GoldEarned,
                    p.MobaState.DamageDealt,
                    p.MobaState.DamageTaken,
                    p.MobaState.GoldenKills))
                .ToList();
            var record = new MobaMatchRecord(Math.Max(0, (int)duration.TotalSeconds), (byte)winner, format, goldenInvasion, players);
            _ = Task.Run(async () =>
            {
                try
                {
                    await sink(record).ConfigureAwait(false);
                }
                catch
                {
                    // the ranking is optional
                }
            });
        }
        catch
        {
            // the ranking is optional
        }
    }

    /// <summary>Reports the state of the server to the web (at most once every 15 seconds).</summary>
    /// <param name="source">Who reports (one per game context).</param>
    /// <param name="humanPlayers">Human players connected.</param>
    /// <param name="inMatch">Champions inside a match arena.</param>
    public static void ReportStatus(string source, int humanPlayers, int inMatch)
    {
        var sink = StatusSink;
        var now = DateTime.UtcNow;
        if (sink is null || now - _lastStatusUtc < StatusEvery)
        {
            return;
        }

        _lastStatusUtc = now;
        _ = Task.Run(async () =>
        {
            try
            {
                await sink(source, humanPlayers, inMatch).ConfigureAwait(false);
            }
            catch
            {
                // the ranking is optional
            }
        });
    }
}
