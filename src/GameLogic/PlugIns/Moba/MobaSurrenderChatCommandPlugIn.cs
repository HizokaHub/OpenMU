// <copyright file="MobaSurrenderChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Chat command <c>/rendirse</c>: proposes or votes the surrender of the team (see <see cref="MobaSurrender"/>).
/// </summary>
[Guid("3F6A91C4-7B25-4E08-9D13-C2A84E6B0F57")]
[PlugIn]
[Display(Name = "MOBA: surrender command", Description = "'/rendirse' - proposes or votes the surrender of your team (after 15 minutes of match).")]
[ChatCommandHelp(Command, "Propose or vote the surrender of your team (after 15 minutes of match; 3 of 4 humans must agree).", typeof(EmptyChatCommandArgs))]
public class MobaSurrenderChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/rendirse";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments) => MobaSurrender.VoteAsync(player);
}
