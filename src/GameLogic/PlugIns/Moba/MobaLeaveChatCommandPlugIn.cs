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
/// GM chat command which ends the current MOBA match: swaps the session back onto
/// the real character, discards the clone and warps to Lorencia.
/// </summary>
/// <remarks>
/// Counterpart of <c>/moba</c>. The clone and everything done with it in the arena
/// are discarded; the real character is restored exactly as it was.
/// </remarks>
[Guid("2F1E6C7A-0B4D-49E8-9C1A-7D3E5A9B2F04")]
[PlugIn]
[Display(Name = "MOBA: leave arena command", Description = "GM command '/mobaleave' - drop the clone and return to the real character.")]
[ChatCommandHelp(Command, "Leave the MOBA match and return to your real character.", typeof(EmptyChatCommandArgs))]
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
        if (player.MobaRealCharacter is not { } real)
        {
            await player.ShowBlueMessageAsync("[MOBA] You are not in a match.").ConfigureAwait(false);
            return;
        }

        var exitGate = await this.GetExitGateAsync(player, LorenciaMapNumber.ToString(), LorenciaSpawnPoint).ConfigureAwait(false);
        if (exitGate is null)
        {
            return;
        }

        var clone = player.SelectedCharacter;

        await player.SetSelectedCharacterAsync(null).ConfigureAwait(false);

        if (clone is not null && !ReferenceEquals(clone, real))
        {
            MobaCloneFactory.DiscardClone(player, clone);
        }

        player.MobaRealCharacter = null;
        player.SuppressPersistence = false;

        await player.SetSelectedCharacterAsync(real).ConfigureAwait(false);
        await player.WarpToAsync(exitGate).ConfigureAwait(false);

        await player.ShowBlueMessageAsync("[MOBA] Left the match - real character restored, nothing from the arena was kept.").ConfigureAwait(false);
    }
}
