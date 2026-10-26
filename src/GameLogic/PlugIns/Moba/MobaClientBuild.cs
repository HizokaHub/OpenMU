// <copyright file="MobaClientBuild.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

/// <summary>
/// The version of the game client. The client reads its build number (<c>eclipse-build.txt</c>, written by the updater with the files of each version) and
/// tells it to the server right after connecting (<c>C1 08 D5 36 build(u32 LE)</c>). The server refuses MOBA matches to clients older than the minimum build
/// that <c>moba-client.json</c> (<c>{"minimumBuild": N, "latestBuild": M}</c>, next to the server, written by <c>tools/publicar-cliente.ps1</c>) asks for.
/// A minimum of 0 (or no file) accepts every client. The file is read again when it changes, so a version can be required without restarting the server.
/// </summary>
public static class MobaClientBuild
{
    /// <summary>The name of the file with the required build, next to the server.</summary>
    public const string FileName = "moba-client.json";

    private static readonly TimeSpan ReloadEvery = TimeSpan.FromSeconds(15);

    private static readonly ConditionalWeakTable<Player, StrongBox<long>> Reported = new();

    private static readonly object Sync = new();

    private static DateTime _checkedUtc = DateTime.MinValue;

    private static DateTime _fileTimeUtc = DateTime.MinValue;

    private static (long Minimum, long Latest) _config;

    /// <summary>Gets or sets the folder where the file is looked for (the folder of the server by default).</summary>
    public static string Folder { get; set; } = AppContext.BaseDirectory;

    /// <summary>Gets the oldest build that may play a match (0: any).</summary>
    public static long Minimum => Current().Minimum;

    /// <summary>Gets the newest published build (for the logs).</summary>
    public static long Latest => Current().Latest;

    /// <summary>Notes the build a client reported.</summary>
    /// <param name="player">The session.</param>
    /// <param name="build">The build number (0 when the client does not have one).</param>
    public static void Report(Player player, long build) => Reported.GetOrCreateValue(player).Value = build;

    /// <summary>Gets the build a client reported (0 when it never did: an old client).</summary>
    /// <param name="player">The session.</param>
    /// <returns>The build.</returns>
    public static long BuildOf(Player player) => Reported.TryGetValue(player, out var box) ? box.Value : 0;

    /// <summary>Checks whether the client of a session is recent enough.</summary>
    /// <param name="player">The session.</param>
    /// <returns>The reason it may not play, or <see langword="null"/> if it may.</returns>
    public static string? Check(Player player) => Check(BuildOf(player), Minimum);

    /// <summary>Checks a reported build against the minimum.</summary>
    /// <param name="reported">The build the client reported (0 if none).</param>
    /// <param name="minimum">The minimum build (0: any).</param>
    /// <returns>The reason it may not play, or <see langword="null"/> if it may.</returns>
    public static string? Check(long reported, long minimum)
    {
        if (minimum <= 0 || reported >= minimum)
        {
            return null;
        }

        return reported <= 0
            ? "Tu cliente está desactualizado. Cierra el juego y ábrelo con el acceso directo de Eclipse Arena para actualizarlo."
            : $"Tu cliente está desactualizado (versión {reported}, la mínima es {minimum}). Cierra el juego y ábrelo con el acceso directo de Eclipse Arena para actualizarlo.";
    }

    /// <summary>Reads the minimum and the latest build from the text of the file.</summary>
    /// <param name="json">The text of <see cref="FileName"/>.</param>
    /// <returns>The minimum and the latest build (0 when missing or malformed).</returns>
    public static (long Minimum, long Latest) Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return (Read(root, "minimumBuild"), Read(root, "latestBuild"));
        }
        catch (JsonException)
        {
            return (0, 0);
        }

        static long Read(JsonElement root, string name)
            => root.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) && number > 0 ? number : 0;
    }

    private static (long Minimum, long Latest) Current()
    {
        lock (Sync)
        {
            var now = DateTime.UtcNow;
            if (now - _checkedUtc < ReloadEvery)
            {
                return _config;
            }

            _checkedUtc = now;
            var path = Path.Combine(Folder, FileName);
            try
            {
                if (!File.Exists(path))
                {
                    _fileTimeUtc = DateTime.MinValue;
                    _config = (0, 0);
                    return _config;
                }

                var written = File.GetLastWriteTimeUtc(path);
                if (written != _fileTimeUtc)
                {
                    _fileTimeUtc = written;
                    _config = Parse(File.ReadAllText(path));
                }
            }
            catch (IOException)
            {
                // being written: keep the last values
            }

            return _config;
        }
    }
}
