// <copyright file="MobaJungleChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM command <c>/mobajungle</c> to look at the jungle without walking the whole map: <c>list</c> shows the spots, <c>reset</c> respawns the whole jungle
/// (and the Selupan event), <c>selupan</c> goes to the nest, and a number goes in front of that spot.
/// </summary>
[Guid("5E2B8C07-9A41-4D36-B8F2-71C0A3E6D914")]
[PlugIn]
[Display(Name = "MOBA: jungle command", Description = "GM command '/mobajungle <list|reset|selupan|n>'.")]
[ChatCommandHelp(Command, "Jungle: /mobajungle list | reset | selupan | <spot number> (teleports in front of the spot).", typeof(MobaJungleChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaJungleChatCommandPlugIn : ChatCommandPlugInBase<MobaJungleChatCommandArgs>
{
    private const string Command = "/mobajungle";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaJungleChatCommandArgs arguments)
    {
        if (player.CurrentMap is not { } map)
        {
            return;
        }

        var what = (arguments.What ?? "list").Trim().ToLowerInvariant();
        if (what == "reset")
        {
            var count = await MobaJungle.StartAsync(map, player.GameContext).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[jungla] {count} monstruos (29 spots + evento Selupan).").ConfigureAwait(false);
            return;
        }

        if (what == "selupan")
        {
            var nest = MobaLayout.NearestWalkable(MobaSelupanEvent.SelupanSpot.X + 4, MobaSelupanEvent.SelupanSpot.Y + 4);
            await player.MoveAsync(nest).ConfigureAwait(false);
            return;
        }

        if (int.TryParse(what, out var number) && number >= 1 && number <= MobaJungle.Spots.Count)
        {
            var spot = MobaJungle.Spots[number - 1];
            var (fx, fy) = MobaJungleFormation.FacingAt(spot.X, spot.Y);
            var front = MobaLayout.NearestWalkable(spot.X + (fx * 6), spot.Y + (fy * 6));
            await player.MoveAsync(front).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[jungla] {number}: {spot.Name} en ({spot.X},{spot.Y}).").ConfigureAwait(false);
            return;
        }

        var lines = MobaJungle.Spots.Select((s, i) => $"{i + 1}={s.Name}").ToList();
        for (var i = 0; i < lines.Count; i += 4)
        {
            await player.ShowBlueMessageAsync("[jungla] " + string.Join("  ", lines.Skip(i).Take(4))).ConfigureAwait(false);
        }

        await player.ShowBlueMessageAsync("[jungla] /mobajungle <n> | selupan | reset").ConfigureAwait(false);
    }
}
