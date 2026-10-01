// <copyright file="MobaFightChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobafight 1|2|stop</c>. <c>1</c>: the caller's champion goes to team
/// Blue against one random red bot. <c>2</c>: a 2v2 of bots only - two random blue bots against two random red bots, the caller just watches
/// against two random red bots. Both start a lane wave for each team every 50 s.
/// <c>stop</c> removes every bot and lane creep and stops the waves (structures stay).
/// </summary>
[Guid("9E4B7A12-3C58-4D96-B1F0-5A2D8C6E7F31")]
[PlugIn]
[Display(Name = "MOBA: quick fight", Description = "Dev command '/mobafight 1|2|stop' - quick 1v1 (you vs a bot) / 2v2 (bots only, you watch) with lane waves every 50 s.")]
[ChatCommandHelp(Command, "Quick fight against random bots with lane waves every 50 s: /mobafight 1 (1v1), /mobafight 2 (2 bots vs 2 bots), /mobafight stop (clear bots + creeps + waves)", null, CharacterStatus.GameMaster)]
public class MobaFightChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/mobafight";

    private const int WaveIntervalSeconds = 50;

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var arg = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).FirstOrDefault()?.ToLowerInvariant();
        var arena = await player.GameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false);
        if (arena is null)
        {
            await player.ShowBlueMessageAsync("[mobafight] No se encontró el mapa de la arena.").ConfigureAwait(false);
            return;
        }

        if (arg is "stop")
        {
            var (bots, creeps) = await StopAsync(arena).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[mobafight] Detenido: {bots} bot(s) y {creeps} creep(s) eliminados, oleadas paradas.").ConfigureAwait(false);
            return;
        }

        if (arg is not ("1" or "2"))
        {
            await player.ShowBlueMessageAsync("[mobafight] Uso: /mobafight 1 (1v1) | /mobafight 2 (2v2) | /mobafight stop").ConfigureAwait(false);
            return;
        }

        if (!player.IsMobaClone)
        {
            await player.ShowBlueMessageAsync("[mobafight] Primero entrá a la arena con /moba.").ConfigureAwait(false);
            return;
        }

        var n = arg == "1" ? 1 : 2;

        // Start from a clean arena so repeated runs don't pile up bots / creeps.
        await StopAsync(arena).ConfigureAwait(false);

        var structures = 0;
        if (!MobaStructureSpawner.HasTurrets(arena.MapId))
        {
            structures += await MobaStructureSpawner.SpawnTurretsAsync(arena, player.GameContext).ConfigureAwait(false);
        }

        if (!MobaStructureSpawner.HasNexuses(arena.MapId))
        {
            structures += await MobaStructureSpawner.SpawnNexusesAsync(arena, player.GameContext).ConfigureAwait(false);
        }

        await MobaShop.EnsureVendorsAsync(player.GameContext).ConfigureAwait(false);

        // 1v1: the caller plays blue. 2v2: bots only, the caller is a neutral spectator (no team, so nobody targets them).
        var spectating = n > 1;
        if (spectating)
        {
            MobaTeams.Clear(player);
        }
        else
        {
            MobaTeams.Set(player, MobaTeam.Blue);
        }

        var red = await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Red, RandomFamilies(n)).ConfigureAwait(false);
        var blue = spectating
            ? await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Blue, RandomFamilies(n)).ConfigureAwait(false)
            : 0;

        // 2v2 also tests the teleport scroll: every bot starts with one.
        var scrolls = 0;
        if (spectating)
        {
            foreach (var bot in MobaBotPlayer.All)
            {
                scrolls += await MobaShop.GiveTeleportScrollAsync(bot).ConfigureAwait(false) ? 1 : 0;
            }
        }

        MobaWavePeriodicSpawner.Start(arena, player.GameContext, TimeSpan.FromSeconds(WaveIntervalSeconds));

        player.Logger.LogInformation("[MOBA-FIGHT] /mobafight {N}v{N}: {Blue} blue bot(s), {Red} red bot(s), {S} structures, waves every {Sec}s.", n, n, blue, red, structures, WaveIntervalSeconds);
        await player.ShowBlueMessageAsync(
            (spectating
                ? $"[mobafight] {n}v{n} de bots: {blue} azules vs {red} rojos (vos solo mirás)."
                : $"[mobafight] {n}v{n}: vos en equipo azul vs {red} bot rojo.") + $" Oleadas cada {WaveIntervalSeconds} s.{(scrolls > 0 ? $" {scrolls} pergamino(s) de teletransporte entregados." : string.Empty)} /mobafight stop para limpiar.").ConfigureAwait(false);
    }

    private static async ValueTask<(int Bots, int Creeps)> StopAsync(GameMap arena)
    {
        MobaWavePeriodicSpawner.Stop(arena.MapId);
        var bots = await MobaBotPlayer.ClearAllAsync().ConfigureAwait(false);
        var creeps = await MobaWaveSpawner.DespawnAllCreepsAsync(arena).ConfigureAwait(false);
        return (bots, creeps);
    }

    private static List<byte> RandomFamilies(int count)
        => MobaBotChatCommandPlugIn.AllFamilies.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
}
