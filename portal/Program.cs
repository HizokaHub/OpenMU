using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Npgsql;

// The public web of the MOBA server: landing page, account creation, ranking, player profiles, server status and news.
// It is standalone (it does not run inside the game server): it reads the game database and a few files next to it.
var builder = WebApplication.CreateBuilder(args);
var options = builder.Configuration.GetSection("Portal").Get<PortalOptions>() ?? new PortalOptions();
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(NpgsqlDataSource.Create(options.ConnectionString));
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("register", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = options.RegistrationsPerHourPerIp, Window = TimeSpan.FromHours(1), QueueLimit = 0 }));
    limiter.AddPolicy("api", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "?",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
app.UseRateLimiter();
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();

var api = app.MapGroup("/api").RequireRateLimiting("api");

api.MapGet("/config", (PortalOptions o) => new
{
    name = o.Name,
    subtitle = o.Subtitle,
    notice = o.Notice,
    theme = o.Theme,
    tagline = o.Tagline,
    serverHost = o.ServerHost,
    serverPort = o.ServerPort,
    registrationEnabled = o.RegistrationEnabled,
    downloadAvailable = File.Exists(Path.Combine(app.Environment.ContentRootPath, o.DownloadFile)),
});

api.MapGet("/status", async (PortalOptions o, NpgsqlDataSource db) =>
{
    var online = await ProbeAsync(o.ServerHost, o.ServerPort).ConfigureAwait(false);
    int? players = null;
    int? inMatch = null;
    try
    {
        await using var command = db.CreateCommand("select coalesce(sum(players), 0), coalesce(sum(in_match), 0) from moba.server_status where updated_at > now() - interval '60 seconds'");
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (await reader.ReadAsync().ConfigureAwait(false))
        {
            players = (int)reader.GetInt64(0);
            inMatch = (int)reader.GetInt64(1);
        }
    }
    catch (PostgresException)
    {
        // the ranking schema does not exist yet
    }
    catch (NpgsqlException)
    {
        // the database is not reachable
    }

    return new { online, players, inMatch };
});

api.MapGet("/check", async (string? login, NpgsqlDataSource db) =>
{
    if (!LoginPattern.IsMatch(login ?? string.Empty))
    {
        return Results.Ok(new { available = false, reason = "El usuario debe tener de 4 a 10 letras o números." });
    }

    await using var command = db.CreateCommand("select 1 from data.\"Account\" where lower(\"LoginName\") = lower($1)");
    command.Parameters.AddWithValue(login!);
    var exists = await command.ExecuteScalarAsync().ConfigureAwait(false) is not null;
    return Results.Ok(new { available = !exists, reason = exists ? "Ese usuario ya existe." : null });
});

app.MapPost("/api/register", async (RegisterRequest request, PortalOptions o, NpgsqlDataSource db) =>
{
    if (!o.RegistrationEnabled)
    {
        return Results.BadRequest(new { error = "El registro está cerrado por ahora." });
    }

    var login = (request.Login ?? string.Empty).Trim();
    var password = request.Password ?? string.Empty;
    var email = (request.Email ?? string.Empty).Trim();
    if (!LoginPattern.IsMatch(login))
    {
        return Results.BadRequest(new { error = "El usuario debe tener de 4 a 10 letras o números (sin espacios ni símbolos)." });
    }

    if (password.Length is < 4 or > 20 || password.Any(c => c < 33 || c > 126))
    {
        return Results.BadRequest(new { error = "La contraseña debe tener de 4 a 20 caracteres, sin espacios." });
    }

    if (email.Length > 60 || (email.Length > 0 && !EmailPattern.IsMatch(email)))
    {
        return Results.BadRequest(new { error = "El correo no parece válido (puedes dejarlo vacío)." });
    }

    await using var check = db.CreateCommand("select 1 from data.\"Account\" where lower(\"LoginName\") = lower($1)");
    check.Parameters.AddWithValue(login);
    if (await check.ExecuteScalarAsync().ConfigureAwait(false) is not null)
    {
        return Results.Conflict(new { error = "Ese usuario ya existe. Prueba con otro." });
    }

    var securityCode = RandomNumberGenerator.GetInt32(1_000_000, 10_000_000).ToString();
    var hash = BCrypt.Net.BCrypt.HashPassword(password);
    await using var insert = db.CreateCommand(
        "insert into data.\"Account\" (\"Id\", \"LoginName\", \"PasswordHash\", \"SecurityCode\", \"EMail\", \"RegistrationDate\", \"State\", \"TimeZone\", \"VaultPassword\", \"IsVaultExtended\", \"IsTemplate\", \"LanguageIsoCode\", \"IsBot\") "
        + "values ($1, $2, $3, $4, $5, now(), 0, 0, '', false, false, 'es', false)");
    insert.Parameters.AddWithValue(Guid.NewGuid());
    insert.Parameters.AddWithValue(login);
    insert.Parameters.AddWithValue(hash);
    insert.Parameters.AddWithValue(securityCode);
    insert.Parameters.AddWithValue(email);
    await insert.ExecuteNonQueryAsync().ConfigureAwait(false);
    app.Logger.LogInformation("Account '{Login}' created from the web.", login);
    return Results.Ok(new { ok = true, login, securityCode, serverHost = o.ServerHost, serverPort = o.ServerPort });
}).RequireRateLimiting("register");

api.MapGet("/ranking", async (NpgsqlDataSource db, string? sort, string? q, int? page) =>
{
    var column = (sort ?? "points") switch
    {
        "wins" => "wins",
        "kills" => "kills",
        "kda" => "((kills + assists)::numeric / greatest(deaths, 1))",
        "golden" => "golden_kills",
        "level" => "best_level",
        _ => "points",
    };
    const int PageSize = 20;
    var offset = Math.Max(0, (page ?? 1) - 1) * PageSize;
    var rows = new List<object>();
    var total = 0;
    var ready = true;
    try
    {
        await using (var count = db.CreateCommand("select count(*) from moba.player_stats where ($1 = '' or character_name ilike '%' || $1 || '%')"))
        {
            count.Parameters.AddWithValue((q ?? string.Empty).Trim());
            total = (int)(long)(await count.ExecuteScalarAsync().ConfigureAwait(false))!;
        }

        await using var command = db.CreateCommand(
            "select character_name, class_index, matches, wins, kills, deaths, assists, best_level, golden_kills, points, last_match "
            + "from moba.player_stats where ($1 = '' or character_name ilike '%' || $1 || '%') "
            + $"order by {column} desc, wins desc, matches desc, character_name limit {PageSize} offset {offset}");
        command.Parameters.AddWithValue((q ?? string.Empty).Trim());
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        var position = offset;
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            rows.Add(new
            {
                position = ++position,
                name = reader.GetString(0),
                classIndex = (int)reader.GetInt16(1),
                matches = (int)reader.GetInt64(2),
                wins = (int)reader.GetInt64(3),
                kills = reader.GetInt64(4),
                deaths = reader.GetInt64(5),
                assists = reader.GetInt64(6),
                bestLevel = (int)reader.GetInt16(7),
                goldenKills = reader.GetInt64(8),
                points = Math.Round(reader.GetDecimal(9), 1),
                lastMatch = reader.GetDateTime(10),
            });
        }
    }
    catch (PostgresException)
    {
        ready = false;
    }

    return new { ready, total, pageSize = PageSize, rows };
});

api.MapGet("/player/{name}", async (string name, NpgsqlDataSource db) =>
{
    try
    {
        await using var stats = db.CreateCommand(
            "select character_name, class_index, matches, wins, kills, deaths, assists, creep_score, best_level, golden_kills, points, last_match from moba.player_stats where lower(character_name) = lower($1)");
        stats.Parameters.AddWithValue(name);
        await using var reader = await stats.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
        {
            return Results.NotFound(new { error = "Ese jugador todavía no tiene partidas." });
        }

        var summary = new
        {
            name = reader.GetString(0),
            classIndex = (int)reader.GetInt16(1),
            matches = (int)reader.GetInt64(2),
            wins = (int)reader.GetInt64(3),
            kills = reader.GetInt64(4),
            deaths = reader.GetInt64(5),
            assists = reader.GetInt64(6),
            creepScore = reader.GetInt64(7),
            bestLevel = (int)reader.GetInt16(8),
            goldenKills = reader.GetInt64(9),
            points = Math.Round(reader.GetDecimal(10), 1),
            lastMatch = reader.GetDateTime(11),
        };
        await reader.CloseAsync().ConfigureAwait(false);

        var recent = new List<object>();
        await using var matches = db.CreateCommand(
            "select m.id, m.ended_at, m.duration_seconds, p.class_index, p.won, p.kills, p.deaths, p.assists, p.level, p.creep_score, p.gold_earned, m.golden_invasion "
            + "from moba.match_player p join moba.match m on m.id = p.match_id where lower(p.character_name) = lower($1) and not p.is_bot order by m.ended_at desc limit 15");
        matches.Parameters.AddWithValue(name);
        await using var matchReader = await matches.ExecuteReaderAsync().ConfigureAwait(false);
        while (await matchReader.ReadAsync().ConfigureAwait(false))
        {
            recent.Add(new
            {
                id = matchReader.GetInt64(0),
                endedAt = matchReader.GetDateTime(1),
                seconds = matchReader.GetInt32(2),
                classIndex = (int)matchReader.GetInt16(3),
                won = matchReader.GetBoolean(4),
                kills = matchReader.GetInt32(5),
                deaths = matchReader.GetInt32(6),
                assists = matchReader.GetInt32(7),
                level = (int)matchReader.GetInt16(8),
                creepScore = matchReader.GetInt32(9),
                gold = matchReader.GetInt32(10),
                goldenInvasion = matchReader.GetBoolean(11),
            });
        }

        return Results.Ok(new { player = summary, recent });
    }
    catch (PostgresException)
    {
        return Results.NotFound(new { error = "El ranking todavía no está activo." });
    }
});

api.MapGet("/news", () =>
{
    var path = Path.Combine(app.Environment.ContentRootPath, options.NewsFile);
    return File.Exists(path) ? Results.Content(File.ReadAllText(path), "application/json") : Results.Json(Array.Empty<object>());
});

app.MapGet("/download", (PortalOptions o) =>
{
    var path = Path.Combine(app.Environment.ContentRootPath, o.DownloadFile);
    return File.Exists(path)
        ? Results.File(path, path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? "application/zip" : "application/octet-stream", Path.GetFileName(path), enableRangeProcessing: true)
        : Results.NotFound(new { error = "El cliente todavía no está disponible para descargar." });
});

app.Run();

static async Task<bool> ProbeAsync(string host, int port)
{
    try
    {
        using var client = new TcpClient();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(700));
        await client.ConnectAsync(host, port, cancel.Token).ConfigureAwait(false);
        return true;
    }
    catch
    {
        return false;
    }
}

internal sealed record RegisterRequest(string? Login, string? Password, string? Email);

internal sealed partial class PortalOptions
{
    public string Name { get; set; } = "Eclipse Arena";

    public string Subtitle { get; set; } = "MU MOBA · ONLINE";

    public string Notice { get; set; } = string.Empty;

    public string Theme { get; set; } = "gold";

    public string Tagline { get; set; } = string.Empty;

    public string ConnectionString { get; set; } = string.Empty;

    public string ServerHost { get; set; } = "127.0.0.1";

    public int ServerPort { get; set; } = 44406;

    public bool RegistrationEnabled { get; set; } = true;

    public int RegistrationsPerHourPerIp { get; set; } = 3;

    public string DownloadFile { get; set; } = "downloads/EclipseArena-Setup.exe";

    public string NewsFile { get; set; } = "news.json";
}

internal static partial class Patterns
{
}

public partial class Program
{
    internal static readonly Regex LoginPattern = new("^[A-Za-z0-9]{4,10}$", RegexOptions.Compiled);

    internal static readonly Regex EmailPattern = new("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled);
}
