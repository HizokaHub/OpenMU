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
/// GM chat command which warps the caller into the MOBA Arena (map 200).
/// </summary>
/// <remarks>
/// First building block of the custom MOBA game mode (see GAMEDESIGN.md). For now this
/// is only a fast way to reach the dedicated arena map while the mode is being built;
/// the real entry flow is the queue NPC in Lorencia + matchmaking + ready-check, and
/// the ephemeral clone setup happens on top of this later.
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

        await player.WarpToAsync(exitGate).ConfigureAwait(false);
        await player.ShowLocalizedBlueMessageAsync(nameof(PlayerMessage.MovedPlayerResult), this.Key, player.Name, exitGate.Map!.Name.GetTranslation(player.Culture), player.Position.X, player.Position.Y).ConfigureAwait(false);
    }
}
