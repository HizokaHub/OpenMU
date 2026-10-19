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
/// Dev command <c>/mobafight 1|2|3|4|stop</c>. <c>4</c>: like 3 but blue also has a bot in mid (3 bots + you vs 3 bots). <c>3</c>: three lanes - you (blue, mid) and two blue bots (top, bot) against three red bots (top, mid, bot), one champion per lane. <c>1</c>: the caller's champion goes to team
/// Blue against one random red bot. <c>2</c>: a 2v2 of bots only - two random blue bots against two random red bots, the caller just watches
/// against two random red bots. Both start a lane wave for each team every 50 s.
/// <c>stop</c> removes every bot and lane creep and stops the waves (structures stay).
/// </summary>
[Guid("9E4B7A12-3C58-4D96-B1F0-5A2D8C6E7F31")]
[PlugIn]
[Display(Name = "MOBA: quick fight", Description = "Dev command '/mobafight 1|2|3|4|stop' - quick 1v1 (you vs a bot) / 2v2 (bots only, you watch) / 3 lanes (you + 2 bots vs 3 bots) with lane waves every 50 s.")]
[ChatCommandHelp(Command, "Quick fight against random bots with lane waves every 50 s: /mobafight 1 (1v1), /mobafight 2 (2 bots vs 2 bots), /mobafight 3 (you + 2 bots vs 3 bots, one per lane), /mobafight 4 (3 bots + you vs 3 bots, one per lane), /mobafight stop (clear bots + creeps + waves)", null, CharacterStatus.GameMaster)]
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
        var words = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(w => w.ToLowerInvariant()).ToList();
        var arg = words.FirstOrDefault();
        var callerJungles = words.Skip(1).Any(w => w is "jungla" or "jungle" or "yo");
        var arena = await MobaArenaBoot.ArenaOfAsync(player).ConfigureAwait(false);
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

        if (arg is not ("1" or "2" or "3" or "4" or "5" or "6"))
        {
            await player.ShowBlueMessageAsync("[mobafight] Uso: /mobafight 1 (1v1) | /mobafight 2 (2v2) | /mobafight 3 (3 carriles) | /mobafight 4 (3v3 + vos) | /mobafight 5 (5v5) | /mobafight 6 (5v5 con roles: top, mid, 2 bot y jungla; solo miras) | /mobafight 6 jungla (vos sos la jungla azul) | /mobafight stop").ConfigureAwait(false);
            return;
        }

        if (!player.IsMobaClone)
        {
            await player.ShowBlueMessageAsync("[mobafight] Primero entrá a la arena con /moba.").ConfigureAwait(false);
            return;
        }

        var n = int.Parse(arg);
        var threeLanes = n is 3 or 4 or 5 or 6;
        var sixVsSix = n == 6;
        var fiveVsFive = n == 5;
        var withThirdBlueBot = n == 4;

        // Start from a clean arena so repeated runs don't pile up bots / creeps.
        await StopAsync(arena).ConfigureAwait(false);
        MobaMatchPhase.Reset(MobaArena.Of(arena));

        var structures = 0;
        if (!MobaStructureSpawner.HasTurrets(arena))
        {
            structures += await MobaStructureSpawner.SpawnTurretsAsync(arena, player.GameContext).ConfigureAwait(false);
        }

        if (!MobaStructureSpawner.HasNexuses(arena))
        {
            structures += await MobaStructureSpawner.SpawnNexusesAsync(arena, player.GameContext).ConfigureAwait(false);
        }

        await MobaShop.EnsureVendorsAsync(arena, player.GameContext).ConfigureAwait(false);

        // 1v1: the caller plays blue. 2v2: bots only, the caller is a neutral spectator (no team, so nobody targets them).
        var spectating = n == 2;
        if (spectating)
        {
            MobaTeams.Clear(player);
        }
        else
        {
            MobaTeams.Set(player, MobaTeam.Blue);
        }

        // 3 lanes: red has one bot per lane (top, mid, bot); blue has bots in top and bot, and the caller takes mid.
        // /mobafight 4: 3 blue bots + the caller (who roams freely to inspect) against 3 red bots; red mirrors the blue bots' classes
        // so the bots themselves are even (random classes made the first games lopsided).
        var blueFamilies = RandomFamilies(sixVsSix ? (callerJungles ? 4 : 5) : fiveVsFive ? 5 : withThirdBlueBot ? 3 : threeLanes ? 2 : n);
        var redFamilies = sixVsSix
            ? blueFamilies.Concat(RandomFamilies(callerJungles ? 1 : 0)).ToList()
            : withThirdBlueBot || fiveVsFive
            ? blueFamilies.ToList()
            : RandomFamilies(threeLanes ? 3 : n);

        // 6: 1 top, 1 mid, 2 bot and the jungler per team (the jungler is the last of the list); the caller only watches, or is the blue jungler.
        var sixLanes = new[] { 0, MobaLayout.MidLane, 2, 2, MobaBotChatCommandPlugIn.JungleLane };
        MobaBotObjectives.Reset(MobaArena.Of(arena));

        // 5v5: 1 top, 2 mid, 2 bot per team; the caller (blue) just roams.
        var fiveLanes = new[] { 0, 1, 1, 2, 2 };
        var red = threeLanes
            ? await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Red, redFamilies, sixVsSix ? sixLanes : fiveVsFive ? fiveLanes : new[] { 0, 1, 2 }).ConfigureAwait(false)
            : await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Red, redFamilies).ConfigureAwait(false);
        var blue = threeLanes
            ? await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Blue, blueFamilies, sixVsSix ? sixLanes.Take(blueFamilies.Count).ToArray() : fiveVsFive ? fiveLanes : withThirdBlueBot ? new[] { 0, 1, 2 } : new[] { 0, 2 }).ConfigureAwait(false)
            : spectating
                ? await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Blue, blueFamilies).ConfigureAwait(false)
                : 0;

        var jungle = threeLanes ? await MobaJungle.StartAsync(arena, player.GameContext, delayed: sixVsSix).ConfigureAwait(false) : 0;

        if (sixVsSix)
        {
            // 6: the caller watches from the middle of the map, or starts at the blue shop as the blue jungler.
            var spot = callerJungles ? MobaLayout.NearestWalkable(MobaLayout.ShopOf(MobaTeam.Blue).X + 3, MobaLayout.ShopOf(MobaTeam.Blue).Y + 3) : MobaLayout.NearestWalkable(MobaLayout.ShopOf(MobaTeam.Blue).X + 3, MobaLayout.ShopOf(MobaTeam.Blue).Y + 3);
            await player.MoveAsync(spot).ConfigureAwait(false);
        }
        else if (threeLanes)
        {
            // The caller walks the mid lane: start at its blue end.
            var midStart = MobaWaveSpawner.LaneWaypointsFor(MobaTeam.Blue, MobaLayout.MidLane)[0];
            var spot = MobaLayout.NearestWalkable(midStart.X + 2, midStart.Y + 2);
            await player.MoveAsync(spot).ConfigureAwait(false);
        }

        // Multi-bot fights also test the teleport scroll: every bot starts with one.
        var scrolls = 0;
        if (spectating || threeLanes)
        {
            foreach (var bot in MobaBotPlayer.All.Where(b => ReferenceEquals(b.CurrentMap, arena)))
            {
                scrolls += await MobaShop.GiveTeleportScrollAsync(bot).ConfigureAwait(false) ? 1 : 0;
            }
        }

        MobaWavePeriodicSpawner.Start(arena, player.GameContext, TimeSpan.FromSeconds(WaveIntervalSeconds));

        player.Logger.LogInformation("[MOBA-FIGHT] /mobafight {N}v{N}: {Blue} blue bot(s), {Red} red bot(s), {S} structures, {J} jungle monsters, waves every {Sec}s.", n, n, blue, red, structures, jungle, WaveIntervalSeconds);
        await player.ShowBlueMessageAsync(
            (sixVsSix
                ? $"[mobafight] 6: {blue} bots azules vs {red} bots rojos (1 TOP, 1 MID, 2 BOT y 1 JUNGLA por equipo; {(callerJungles ? "vos sos la jungla azul" : "vos jugás en el equipo azul como un campeón más, sin rol")}). {jungle} monstruos de jungla ya; Medusa y Fénix a los 3:00, huevos a los 4:00, God of Darkness a los 5:00 y Kundun a los 6:00. Los bots se coordinan para los objetivos: busca [MOBA-OBJ] en el log."
                : fiveVsFive
                ? $"[mobafight] 5v5: {blue} bots azules vs {red} bots rojos (clases al azar, las mismas en ambos equipos; 1 TOP, 2 MID, 2 BOT por equipo) + vos libre en azul. {jungle} monstruos de jungla."
                : threeLanes
                ? (withThirdBlueBot
                    ? $"[mobafight] {blue} bots azules (TOP, MID, BOT, con las mismas clases que los rojos) + vos (azul, libre; empezás en MID) vs {red} bots rojos (TOP, MID, BOT)."
                    : $"[mobafight] 3 carriles: vos (azul, carril MID) + {blue} bots azules (TOP y BOT) vs {red} bots rojos (TOP, MID, BOT).")
                : spectating
                    ? $"[mobafight] {n}v{n} de bots: {blue} azules vs {red} rojos (vos solo mirás)."
                    : $"[mobafight] {n}v{n}: vos en equipo azul vs {red} bot rojo.") + $" Oleadas cada {WaveIntervalSeconds} s.{(scrolls > 0 ? $" {scrolls} pergamino(s) de teletransporte entregados." : string.Empty)} /mobafight stop para limpiar.").ConfigureAwait(false);
    }

    private static async ValueTask<(int Bots, int Creeps)> StopAsync(GameMap arena)
    {
        MobaWavePeriodicSpawner.Stop(arena);
        MobaBotObjectives.Reset(MobaArena.Of(arena));
        await MobaJungle.StopAsync(arena).ConfigureAwait(false);
        var bots = await MobaBotPlayer.ClearAllAsync(arena).ConfigureAwait(false);
        var creeps = await MobaWaveSpawner.DespawnAllCreepsAsync(arena).ConfigureAwait(false);
        return (bots, creeps);
    }

    private static List<byte> RandomFamilies(int count)
        => MobaBotChatCommandPlugIn.AllFamilies.OrderBy(_ => Random.Shared.Next()).Take(count).ToList();
}
