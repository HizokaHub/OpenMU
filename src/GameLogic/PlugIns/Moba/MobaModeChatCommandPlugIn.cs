// <copyright file="MobaModeChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which shows, lists or sets the game mode (<see cref="IMobaGameMode"/>) of the arena the caller is in.
/// </summary>
/// <remarks>
/// Usage: <c>/mobamode</c> shows the mode of the arena; <c>/mobamode list</c> lists the modes of the server;
/// <c>/mobamode &lt;id&gt;</c> sets it. A mode can only be changed before the match starts.
/// </remarks>
[Guid("9E3C7A52-61B8-4D0F-A4E7-2B5D8C1F6A93")]
[PlugIn]
[Display(Name = "MOBA: game mode command", Description = "GM command '/mobamode [list|id]' - show, list or set the game mode of the arena.")]
[ChatCommandHelp(Command, "Show, list or set the MOBA game mode of this arena ('/mobamode', '/mobamode list', '/mobamode classic').", typeof(MobaModeChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaModeChatCommandPlugIn : ChatCommandPlugInBase<MobaModeChatCommandArgs>
{
    private const string Command = "/mobamode";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaModeChatCommandArgs arguments)
    {
        if (await MobaArenaBoot.ArenaOfAsync(player).ConfigureAwait(false) is not { } map)
        {
            await player.ShowBlueMessageAsync("[MOBA] This server has no arena.").ConfigureAwait(false);
            return;
        }

        var arena = MobaArena.Of(map);
        var arg = arguments.Mode?.Trim();
        if (string.IsNullOrEmpty(arg))
        {
            await player.ShowBlueMessageAsync($"[MOBA] Mode of this arena: {arena.Mode.DisplayName} ({arena.Mode.Id}).").ConfigureAwait(false);
            return;
        }

        if (string.Equals(arg, "list", StringComparison.OrdinalIgnoreCase))
        {
            var list = string.Join(", ", MobaGameModes.All.Select(m => $"{m.Id} ({m.DisplayName})"));
            await player.ShowBlueMessageAsync($"[MOBA] Modes: {list}.").ConfigureAwait(false);
            return;
        }

        if (MobaGameModes.Find(arg) is not { } mode)
        {
            await player.ShowBlueMessageAsync($"[MOBA] Unknown mode '{arg}'. Use /mobamode list.").ConfigureAwait(false);
            return;
        }

        if (arena.MatchStartUtc != DateTime.MinValue || arena.IsPreMatch)
        {
            await player.ShowBlueMessageAsync("[MOBA] The mode can only be changed before the match starts.").ConfigureAwait(false);
            return;
        }

        arena.Mode = mode;
        await player.ShowBlueMessageAsync($"[MOBA] Mode of this arena: {mode.DisplayName} ({mode.Id}).").ConfigureAwait(false);
    }
}
