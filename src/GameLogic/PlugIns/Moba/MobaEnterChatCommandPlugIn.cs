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
/// GM chat command which enters a MOBA match: swaps the session onto an ephemeral
/// clone of the real character and warps it into the MOBA Arena (map 200).
/// </summary>
/// <remarks>
/// Building block of the custom MOBA game mode (see GAMEDESIGN.md). The real entry
/// flow is the queue NPC in Lorencia + matchmaking + ready-check; this is the GM
/// shortcut used to build and play-test the mode. Use <c>/mobaleave</c> to drop the
/// clone and return to the real character. This first version keeps the clone in the
/// live session, so a disconnect ends the match (reconnection survival is a later
/// topic).
/// </remarks>
[Guid("58B0FF0B-4DCA-4B90-AC0F-10CE2D89EE9B")]
[PlugIn]
[Display(Name = "MOBA: enter arena command", Description = "GM command '/moba' - enter a MOBA match as an ephemeral clone.")]
[ChatCommandHelp(Command, "Enter a MOBA match as an ephemeral clone (map 200).", typeof(EmptyChatCommandArgs))]
public class MobaEnterChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/moba";

    private static readonly Point ArenaEntryPoint = new(128, 128);

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        if (player.SelectedCharacter is not { } real)
        {
            return;
        }

        if (player.MobaRealCharacter is not null)
        {
            await player.ShowBlueMessageAsync("[MOBA] You are already in a match. Use /mobaleave first.").ConfigureAwait(false);
            return;
        }

        var exitGate = await this.GetExitGateAsync(player, MobaCloneFactory.ArenaMapNumber.ToString(), ArenaEntryPoint).ConfigureAwait(false);
        if (exitGate is null)
        {
            return;
        }

        var clone = await MobaCloneFactory.BuildCloneAsync(player).ConfigureAwait(false);

        player.MobaRealCharacter = real;
        player.SuppressPersistence = true;

        await player.SetSelectedCharacterAsync(null).ConfigureAwait(false);
        await player.SetSelectedCharacterAsync(clone).ConfigureAwait(false);
        await player.WarpToAsync(exitGate).ConfigureAwait(false);

        await player.ShowBlueMessageAsync("[MOBA] Entered the arena as a clone - level 400, no items, progress NOT saved. Use /mobaleave to return.").ConfigureAwait(false);
    }
}
