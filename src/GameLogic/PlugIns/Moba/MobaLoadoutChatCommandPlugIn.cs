// <copyright file="MobaLoadoutChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Chat command <c>/misskills</c>: opens the loadout window (in town, or in the base before the match starts).
/// </summary>
[Guid("B15D7F30-2C86-4E49-A0B3-9D4E1F6A7C52")]
[PlugIn]
[Display(Name = "MOBA: loadout command", Description = "'/misskills' - opens the window to pick your skills before the match starts.")]
[ChatCommandHelp(Command, "Open the window to pick your skills (in town or in your base before the match starts).", typeof(EmptyChatCommandArgs))]
public class MobaLoadoutChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/misskills";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments) => MobaLoadoutService.OpenAsync(player);
}
