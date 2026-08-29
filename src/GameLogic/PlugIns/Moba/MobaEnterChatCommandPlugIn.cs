// <copyright file="MobaEnterChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which warps the caller into the MOBA Arena (map 200) and marks
/// the session as non-persisted for the duration.
/// </summary>
/// <remarks>
/// Building block of the custom MOBA game mode (see GAMEDESIGN.md). For now this is a
/// fast way to reach the dedicated arena map and exercise the no-save behaviour while
/// the mode is built; the real entry flow is the queue NPC in Lorencia + matchmaking
/// + ready-check, and the ephemeral clone setup happens on top of this later. Use
/// <c>/mobaleave</c> to return to Lorencia and re-enable saving.
/// </remarks>
[Guid("58B0FF0B-4DCA-4B90-AC0F-10CE2D89EE9B")]
[PlugIn]
[Display(Name = "MOBA: enter arena command", Description = "GM command '/moba' - warp into the MOBA Arena (map 200).")]
[ChatCommandHelp(Command, "Warp into the MOBA Arena (map 200).", typeof(EmptyChatCommandArgs))]
public class MobaEnterChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/moba";

    private const ushort MobaArenaMapNumber = 200;

    private static readonly Point ArenaEntryPoint = new(128, 128);

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        var exitGate = await this.GetExitGateAsync(player, MobaArenaMapNumber.ToString(), ArenaEntryPoint).ConfigureAwait(false);
        if (exitGate is null)
        {
            return;
        }

        player.SuppressPersistence = true;
        await player.WarpToAsync(exitGate).ConfigureAwait(false);
        await player.ShowBlueMessageAsync("[MOBA] Entered the arena - session progress is NOT saved here. Use /mobaleave to return.").ConfigureAwait(false);
    }
}
