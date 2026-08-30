// <copyright file="MobaLeaveChatCommandPlugIn.cs" company="MUnique">
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
/// GM chat command which ends the current MOBA match: clears the account's match
/// membership and disconnects the client. On reconnect the session enters the world
/// as the real character again.
/// </summary>
/// <remarks>
/// Counterpart of <c>/moba</c>. The clone and everything done with it in the arena
/// are discarded (nothing was ever persisted).
/// </remarks>
[Guid("2F1E6C7A-0B4D-49E8-9C1A-7D3E5A9B2F04")]
[PlugIn]
[Display(Name = "MOBA: leave arena command", Description = "GM command '/mobaleave' - end the match and reconnect as the real character.")]
[ChatCommandHelp(Command, "End the MOBA match and reconnect as your real character.", typeof(EmptyChatCommandArgs))]
public class MobaLeaveChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/mobaleave";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        if (player.Account is not { } account || !MobaMatchRegistry.IsInMatch(account.GetId()))
        {
            await player.ShowBlueMessageAsync("[MOBA] You are not in a match.").ConfigureAwait(false);
            return;
        }

        var clone = MobaMatchRegistry.Leave(account.GetId());
        if (clone is not null && !ReferenceEquals(clone, player.SelectedCharacter))
        {
            MobaCloneFactory.DiscardClone(player, clone);
        }

        await player.ShowBlueMessageAsync("[MOBA] Match ended - reconnecting you as your real character...").ConfigureAwait(false);
        await player.DisconnectAsync().ConfigureAwait(false);
    }
}
