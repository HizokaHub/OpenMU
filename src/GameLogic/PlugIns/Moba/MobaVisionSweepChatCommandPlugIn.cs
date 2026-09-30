// <copyright file="MobaVisionSweepChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;

/// <summary>Chat command '/sweep' of the MOBA vision system (see <see cref="MobaVision"/>): Sweep enemy wards around you (90 s cooldown).</summary>
[Guid("A9D4E2B7-1C38-4F5A-B6E0-7C2F8D1A3E95")]
[PlugIn]
[Display(Name = "MOBA: /sweep command", Description = "Command '/sweep' - Sweep enemy wards around you (90 s cooldown).")]
[ChatCommandHelp(Command, "Sweep enemy wards around you (90 s cooldown).", typeof(EmptyChatCommandArgs))]
public class MobaVisionSweepChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/sweep";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
        => await player.ShowBlueMessageAsync(MobaVision.TrySweep(player)).ConfigureAwait(false);
}
