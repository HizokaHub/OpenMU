// <copyright file="MobaGoldenChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM command <c>/mobagolden</c> to try the golden invasion without waiting for minute 22 (the automatic start at minute 22 is off until the invasion is
/// released: <c>autoon|autooff</c> switches it): <c>start</c> begins it with the fly-over (60 s),
/// <c>now</c> without it (the invaders appear in 3 s), <c>stop</c> removes the invaders, <c>list</c> counts them and <c>dragon</c> goes to the Great Golden Dragon of
/// the half of the caller.
/// </summary>
[Guid("2B7E4D90-3A15-4C68-B0E9-5D1F8A6C7E33")]
[PlugIn]
[Display(Name = "MOBA: golden invasion command", Description = "GM command '/mobagolden <start|now|stop|list|dragon>'.")]
[ChatCommandHelp(Command, "Golden invasion: /mobagolden start | now | stop | list | dragon | autoon | autooff.", typeof(MobaGoldenChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaGoldenChatCommandPlugIn : ChatCommandPlugInBase<MobaGoldenChatCommandArgs>
{
    private const string Command = "/mobagolden";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaGoldenChatCommandArgs arguments)
    {
        if (player.CurrentMap is not { } map)
        {
            return;
        }

        switch ((arguments.What ?? "list").Trim().ToLowerInvariant())
        {
            case "start":
            case "now":
                var seconds = arguments.What!.Trim().Equals("now", StringComparison.OrdinalIgnoreCase) ? 3 : MobaGolden.FlyoverSeconds;
                var started = await MobaGolden.StartAsync(map, player.GameContext, seconds).ConfigureAwait(false);
                await player.ShowBlueMessageAsync(started ? $"[dorados] La invasión empieza (los invasores en {seconds} s)." : "[dorados] La invasión ya empezó.").ConfigureAwait(false);
                break;
            case "autoon":
            case "autooff":
                MobaGolden.AutoStartEnabled = arguments.What!.Trim().Equals("autoon", StringComparison.OrdinalIgnoreCase);
                await player.ShowBlueMessageAsync($"[dorados] Inicio automático al minuto 22: {(MobaGolden.AutoStartEnabled ? "ACTIVADO" : "desactivado")}.").ConfigureAwait(false);
                break;
            case "stop":
                await MobaGolden.StopAsync(map).ConfigureAwait(false);
                await player.ShowBlueMessageAsync("[dorados] Invasión detenida, invasores retirados.").ConfigureAwait(false);
                break;
            case "dragon":
                var spot = MobaTeams.GetTeam(player) == MobaTeam.Red ? MobaGolden.RedDragonSpot : MobaGolden.BlueDragonSpot;
                await player.MoveAsync(MobaLayout.NearestWalkable(spot.X + 5, spot.Y + 5)).ConfigureAwait(false);
                break;
            default:
                var live = MobaGolden.LiveMonsters(map);
                await player.ShowBlueMessageAsync($"[dorados] {(MobaGolden.HasStarted(map) ? "invasión en curso" : "sin invasión")}, {live.Count} invasores vivos.").ConfigureAwait(false);
                break;
        }
    }
}

/// <summary>Arguments for <c>/mobagolden &lt;start|now|stop|list|dragon&gt;</c>.</summary>
public class MobaGoldenChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets what to do.</summary>
    [Argument("what")]
    public string What { get; set; } = "list";
}
