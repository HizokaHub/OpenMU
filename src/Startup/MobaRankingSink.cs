// <copyright file="MobaRankingSink.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Startup;

using System;
using System.Threading.Tasks;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using Npgsql;
using Serilog;

/// <summary>
/// Stores the results of the MOBA matches and the state of the server in the <c>moba</c> schema of the database (see <c>tools/moba-ranking-schema.sql</c>), where
/// the web portal reads them. The schema is optional: until it exists nothing is written (it is looked for again every minute, so it starts to work as soon as the
/// script was run), and any failure is only logged.
/// </summary>
internal static class MobaRankingSink
{
    private static readonly TimeSpan SchemaCheckEvery = TimeSpan.FromMinutes(1);

    private static NpgsqlDataSource? _dataSource;
    private static ILogger? _logger;
    private static bool _schemaReady;
    private static DateTime _lastSchemaCheckUtc = DateTime.MinValue;

    /// <summary>Installs the sinks of the game logic.</summary>
    /// <param name="connectionString">The connection string of the game database.</param>
    /// <param name="logger">The logger.</param>
    public static void Install(string connectionString, ILogger logger)
    {
        _logger = logger;
        _dataSource = NpgsqlDataSource.Create(connectionString);
        MobaMatchRecorder.MatchSink = InsertMatchAsync;
        MobaMatchRecorder.StatusSink = UpsertStatusAsync;
        logger.Information("[MOBA-RANKING] Ranking sink installed (it writes once the 'moba' schema exists).");
    }

    private static async Task<bool> SchemaReadyAsync()
    {
        if (_schemaReady)
        {
            return true;
        }

        if (_dataSource is null || DateTime.UtcNow - _lastSchemaCheckUtc < SchemaCheckEvery)
        {
            return false;
        }

        _lastSchemaCheckUtc = DateTime.UtcNow;
        await using var command = _dataSource.CreateCommand("select to_regclass('moba.match') is not null and to_regclass('moba.match_player') is not null and to_regclass('moba.server_status') is not null");
        _schemaReady = await command.ExecuteScalarAsync().ConfigureAwait(false) is true;
        if (_schemaReady)
        {
            _logger?.Information("[MOBA-RANKING] The 'moba' schema exists: matches and server status are recorded now.");
        }

        return _schemaReady;
    }

    private static async Task InsertMatchAsync(MobaMatchRecord match)
    {
        if (!await SchemaReadyAsync().ConfigureAwait(false) || _dataSource is null)
        {
            return;
        }

        await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        long matchId;
        await using (var insert = new NpgsqlCommand(
            "insert into moba.match (duration_seconds, winner_team, format, golden_invasion) values ($1, $2, $3, $4) returning id",
            connection,
            transaction))
        {
            insert.Parameters.AddWithValue((int)match.DurationSeconds);
            insert.Parameters.AddWithValue((short)match.WinnerTeam);
            insert.Parameters.AddWithValue(match.Format);
            insert.Parameters.AddWithValue(match.GoldenInvasion);
            matchId = (long)(await insert.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        short slot = 0;
        foreach (var player in match.Players)
        {
            await using var row = new NpgsqlCommand(
                "insert into moba.match_player (match_id, slot, account_login, character_name, class_index, team, is_bot, won, level, kills, deaths, assists, creep_score, gold_earned, damage_dealt, damage_taken, golden_kills) "
                + "values ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17)",
                connection,
                transaction);
            row.Parameters.AddWithValue(matchId);
            row.Parameters.AddWithValue(slot++);
            row.Parameters.AddWithValue((object?)player.AccountLogin ?? DBNull.Value);
            row.Parameters.AddWithValue(player.CharacterName);
            row.Parameters.AddWithValue((short)player.ClassIndex);
            row.Parameters.AddWithValue((short)player.Team);
            row.Parameters.AddWithValue(player.IsBot);
            row.Parameters.AddWithValue(player.Won);
            row.Parameters.AddWithValue((short)Math.Clamp(player.Level, 0, short.MaxValue));
            row.Parameters.AddWithValue(player.Kills);
            row.Parameters.AddWithValue(player.Deaths);
            row.Parameters.AddWithValue(player.Assists);
            row.Parameters.AddWithValue(player.CreepScore);
            row.Parameters.AddWithValue((int)Math.Clamp(player.GoldEarned, 0, int.MaxValue));
            row.Parameters.AddWithValue(player.DamageDealt);
            row.Parameters.AddWithValue(player.DamageTaken);
            row.Parameters.AddWithValue(player.GoldenKills);
            await row.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await transaction.CommitAsync().ConfigureAwait(false);
        _logger?.Information("[MOBA-RANKING] Match recorded (#{Id}, {Players} champions).", matchId, match.Players.Count);
    }

    private static async Task UpsertStatusAsync(string source, int players, int inMatch)
    {
        if (!await SchemaReadyAsync().ConfigureAwait(false) || _dataSource is null)
        {
            return;
        }

        await using var command = _dataSource.CreateCommand(
            "insert into moba.server_status (source, players, in_match, updated_at) values ($1, $2, $3, now()) "
            + "on conflict (source) do update set players = excluded.players, in_match = excluded.in_match, updated_at = excluded.updated_at");
        command.Parameters.AddWithValue(source);
        command.Parameters.AddWithValue(players);
        command.Parameters.AddWithValue(inMatch);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }
}
