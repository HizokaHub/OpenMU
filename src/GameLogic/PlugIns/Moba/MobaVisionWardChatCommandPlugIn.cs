// <copyright file="MobaVisionWardChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;

/// <summary>Chat command '/ward' of the MOBA vision system (see <see cref="MobaVision"/>): Place a MOBA vision ward (75 gold).</summary>
[Guid("F3A1C7D2-5B6E-4E9A-8C10-2D7B9E4A1C61")]
[PlugIn]
[Display(Name = "MOBA: /ward command", Description = "Command '/ward' - Place a MOBA vision ward (75 gold).")]
[ChatCommandHelp(Command, "Place a MOBA vision ward (75 gold).", typeof(EmptyChatCommandArgs))]
public class MobaVisionWardChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/ward";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
        => await player.ShowBlueMessageAsync(MobaVision.TryPlaceWard(player)).ConfigureAwait(false);
}
