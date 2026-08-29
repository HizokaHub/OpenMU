// <copyright file="MobaLeaveChatCommandPlugIn.cs" company="MUnique">
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
/// GM chat command which leaves the MOBA Arena: warps the caller back to Lorencia
/// and re-enables progress saving for the session.
/// </summary>
/// <remarks>
/// Counterpart of <c>/moba</c>. Only clears <see cref="Player.SuppressPersistence"/>;
/// it does not force a save, so anything done inside the arena stays discarded.
/// </remarks>
[Guid("2F1E6C7A-0B4D-49E8-9C1A-7D3E5A9B2F04")]
[PlugIn]
[Display(Name = "MOBA: leave arena command", Description = "GM command '/mobaleave' - warp back to Lorencia and re-enable saving.")]
[ChatCommandHelp(Command, "Leave the MOBA Arena and re-enable progress saving.", typeof(EmptyChatCommandArgs))]
public class MobaLeaveChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/mobaleave";

    private const ushort LorenciaMapNumber = 0;

    private static readonly Point LorenciaSpawnPoint = new(140, 125);

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        var exitGate = await this.GetExitGateAsync(player, LorenciaMapNumber.ToString(), LorenciaSpawnPoint).ConfigureAwait(false);
        if (exitGate is null)
        {
            return;
        }

        player.SuppressPersistence = false;
        await player.WarpToAsync(exitGate).ConfigureAwait(false);
        await player.ShowBlueMessageAsync("[MOBA] Left the arena - progress saving re-enabled. Anything done in the arena was discarded.").ConfigureAwait(false);
    }
}
