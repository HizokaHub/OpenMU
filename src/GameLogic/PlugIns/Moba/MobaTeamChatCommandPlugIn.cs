// <copyright file="MobaTeamChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which assigns the caller (the clone in a MOBA match) to a team.
/// </summary>
/// <remarks>
/// Test tool for Fase 2. Usage: <c>/mobateam blue</c> or <c>/mobateam red</c>.
/// The team drives creep / turret / nexus friend-or-foe decisions.
/// </remarks>
[Guid("A7D31E64-9F02-4C58-8B1D-3E6A0C9F2B47")]
[PlugIn]
[Display(Name = "MOBA: set team command", Description = "GM command '/mobateam blue|red' - set your MOBA team.")]
[ChatCommandHelp(Command, "Set your MOBA team (blue|red).", typeof(MobaTeamChatCommandArgs))]
public class MobaTeamChatCommandPlugIn : ChatCommandPlugInBase<MobaTeamChatCommandArgs>
{
    private const string Command = "/mobateam";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaTeamChatCommandArgs arguments)
    {
        var team = arguments.ResolveTeam();
        MobaTeams.Set(player, team);
        await player.ShowBlueMessageAsync($"[MOBA] You are on team {team}.").ConfigureAwait(false);
    }
}
