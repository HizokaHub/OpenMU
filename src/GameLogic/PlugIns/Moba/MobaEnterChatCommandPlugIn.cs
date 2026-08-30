// <copyright file="MobaEnterChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which enters a MOBA match. It flags the account as being in a
/// match and disconnects the client; on reconnect the session enters the world as
/// an ephemeral clone in the MOBA Arena instead of the real character.
/// </summary>
/// <remarks>
/// Building block of the custom MOBA game mode (see GAMEDESIGN.md). The real entry
/// flow is the queue NPC + matchmaking + ready-check; this is the GM shortcut for
/// play-testing. The reconnect is deliberate: it lets the client run its normal
/// select-character / enter-world sequence, which avoids the desync a live
/// selected-character swap caused. Use <c>/mobaleave</c> to end the match.
/// </remarks>
[Guid("58B0FF0B-4DCA-4B90-AC0F-10CE2D89EE9B")]
[PlugIn]
[Display(Name = "MOBA: enter arena command", Description = "GM command '/moba' - enter a MOBA match as an ephemeral clone (reconnects).")]
[ChatCommandHelp(Command, "Enter a MOBA match as an ephemeral clone (map 200). Reconnects the client.", typeof(EmptyChatCommandArgs))]
public class MobaEnterChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/moba";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        if (player.Account is not { } account || player.SelectedCharacter is not { } real)
        {
            return;
        }

        if (MobaMatchRegistry.IsInMatch(account.GetId()))
        {
            await player.ShowBlueMessageAsync("[MOBA] You are already in a match. Use /mobaleave to end it.").ConfigureAwait(false);
            return;
        }

        var clone = await MobaCloneFactory.BuildCloneAsync(player, real).ConfigureAwait(false);

        // The clone is created through this session's persistence context, so detach it
        // and suppress this session's save: DisconnectAsync() would otherwise try to
        // INSERT the clone (duplicate character name).
        MobaCloneFactory.DetachClone(player, clone);
        player.SuppressPersistence = true;

        MobaMatchRegistry.Enter(account.GetId(), clone);
        await player.ShowBlueMessageAsync("[MOBA] Match starting - reconnecting you as a clone...").ConfigureAwait(false);
        await player.DisconnectAsync().ConfigureAwait(false);
    }
}
