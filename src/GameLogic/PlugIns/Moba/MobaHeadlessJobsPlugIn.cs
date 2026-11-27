// <copyright file="MobaHeadlessJobsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Balance duels without any client (2026-11-27): the server runs the GM chat commands it finds in job files, with an invisible bot standing in the arena
/// as the game master. Off unless the environment variable <c>MOBA_DUEL_JOBS</c> names a folder (a normal server never has it). A job is a file
/// <c>*.job</c> with one command per line; it is renamed to <c>*.run</c> while it runs and to <c>*.done</c> at the end. A <c>/mobaduel</c> line waits until
/// the duels are over. A file <c>stop</c> in the folder cancels the duels in progress.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: headless duel jobs", Description = "Runs the balance duel commands of the job files in the MOBA_DUEL_JOBS folder, without a client.")]
[Guid("3C7B1E94-52A8-4F0D-9B6E-1D48A7C2F530")]
public class MobaHeadlessJobsPlugIn : IPeriodicTaskPlugIn
{
    /// <summary>The environment variable which names the folder of the jobs.</summary>
    public const string FolderVariable = "MOBA_DUEL_JOBS";

    /// <summary>The only commands a job may run: the balance ones.</summary>
    private static readonly string[] AllowedCommands = { "/mobaduel", "/mobaf10" };

    private static int _busy;
    private static MobaBotPlayer? _host;

    /// <inheritdoc />
    public void ForceStart()
    {
        // nothing to force: the folder is looked at on every tick
    }

    /// <inheritdoc />
    public ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        var folder = Environment.GetEnvironmentVariable(FolderVariable);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder) || Volatile.Read(ref _busy) == 1)
        {
            return ValueTask.CompletedTask;
        }

        var job = Directory.GetFiles(folder, "*.job").OrderBy(f => f, StringComparer.Ordinal).FirstOrDefault();
        if (job is null || Interlocked.Exchange(ref _busy, 1) == 1)
        {
            return ValueTask.CompletedTask;
        }

        // in the background: a job lasts minutes and the periodic tick must go on
        _ = Task.Run(() => RunJobAsync(gameContext, folder, job));
        return ValueTask.CompletedTask;
    }

    private static async Task RunJobAsync(GameContext gameContext, string folder, string job)
    {
        var logger = gameContext.LoggerFactory.CreateLogger<MobaHeadlessJobsPlugIn>();
        var name = Path.GetFileNameWithoutExtension(job);
        var running = Path.ChangeExtension(job, ".run");
        var outcome = ".done";
        try
        {
            // a server which is still starting may not write the file in one go: the writer renames it into place, so it is complete here
            File.Move(job, running, true);
            var lines = (await File.ReadAllLinesAsync(running).ConfigureAwait(false)).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            var host = await HostAsync(gameContext).ConfigureAwait(false);
            foreach (var line in lines)
            {
                var key = line.Split(' ', 2)[0];
                if (!AllowedCommands.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"comando no permitido en un pedido: {key}");
                }

                var command = gameContext.PlugInManager.GetStrategy<IChatCommandPlugIn>(key)
                    ?? throw new InvalidOperationException($"no existe el comando {key}");
                logger.LogInformation("[MOBA-JOB] {Job} > {Line}", name, line);
                await command.HandleCommandAsync(host, line).ConfigureAwait(false);
                if (string.Equals(key, "/mobaduel", StringComparison.OrdinalIgnoreCase))
                {
                    await WaitForDuelsAsync(folder).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            outcome = ".error";
            logger.LogError(ex, "[MOBA-JOB] {Job} failed.", name);
        }
        finally
        {
            try
            {
                File.Move(running, Path.ChangeExtension(running, outcome), true);
            }
            catch (IOException ex)
            {
                logger.LogWarning(ex, "[MOBA-JOB] {Job}: the job file could not be renamed.", name);
            }

            logger.LogInformation("[MOBA-JOB] {Job} finished ({Outcome}).", name, outcome);
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task WaitForDuelsAsync(string folder)
    {
        // the command starts the duels in the background: give them a moment to take the flag
        await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        var stop = Path.Combine(folder, "stop");
        while (MobaDuel.IsRunning)
        {
            if (File.Exists(stop))
            {
                MobaDuel.Cancel();
            }

            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
        }
    }

    /// <summary>The bot which plays the game master: it only stands in the arena (no brain), so the duel commands have a player to start from.</summary>
    private static async ValueTask<Player> HostAsync(GameContext gameContext)
    {
        if (_host is { } existing)
        {
            return existing;
        }

        var arena = await gameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false)
            ?? throw new InvalidOperationException("no existe el mapa de la arena");
        MobaArenaBoot.ClearSafezone(arena);

        // the same way the duels make their duelists: a bot which never starts lends its persistence context to build the one which enters the world
        var carrier = new MobaBotPlayer(gameContext, MobaTeam.Blue, isDummy: true);
        var spot = MobaLayout.NearestWalkable(MobaLayout.ShopOf(MobaTeam.Blue).X, MobaLayout.ShopOf(MobaTeam.Blue).Y);
        var host = await MobaSkillProbe.SpawnAsync(carrier, MobaDuel.MainClasses[0], "dHost", MobaTeam.Blue, new Point((byte)spot.X, (byte)spot.Y), 1).ConfigureAwait(false)
            ?? throw new InvalidOperationException("no se pudo crear el anfitrión de los duelos");
        await host.BecomeHostAsync().ConfigureAwait(false);
        host.SelectedCharacter!.CharacterStatus = CharacterStatus.GameMaster;
        _host = host;
        return host;
    }
}
