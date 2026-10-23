// <copyright file="MobaLvlChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Short alias of <c>/mobalevel</c>: <c>/mobalvl &lt;level&gt;</c> jumps the caller's MOBA champion to a level (1..30).
/// </summary>
[Guid("5C8E2B17-90D4-4A6F-B3E1-7D2A4F9C1E68")]
[PlugIn]
[Display(Name = "MOBA: set champion level (short)", Description = "Dev command '/mobalvl <level>'.")]
[ChatCommandHelp(Command, "Jump your MOBA champion to a level (1..30): /mobalvl <level>", typeof(MobaLevelChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaLvlChatCommandPlugIn : MobaLevelChatCommandPlugIn
{
    private const string Command = "/mobalvl";

    /// <inheritdoc />
    public override string Key => Command;
}
