// <copyright file="MobaWaveChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which spawns one MOBA lane wave for a team on the caller's current
/// map: a few creeps that march the mid lane and fight enemies along the way.
/// </summary>
/// <remarks>
/// Test tool for Fase 2 (see GAMEDESIGN.md). Usage: <c>/mobawave</c> (blue) or
/// <c>/mobawave red</c>. For a continuous match rhythm use <c>/mobawaves</c> instead.
/// Run it while standing on the MOBA Arena (map 200).
/// </remarks>
[Guid("C3A9F1D2-5E47-4B80-9A16-2D8C7B0E4F35")]
[PlugIn]
[Display(Name = "MOBA: spawn lane wave command", Description = "GM command '/mobawave [red]' - spawn a marching lane wave for a team.")]
[ChatCommandHelp(Command, "Spawn a MOBA lane wave (blue marches south, 'red' marches north).", typeof(MobaTeamChatCommandArgs))]
public class MobaWaveChatCommandPlugIn : ChatCommandPlugInBase<MobaTeamChatCommandArgs>
{
    private const string Command = "/mobawave";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaTeamChatCommandArgs arguments)
    {
        if (player.CurrentMap is not { } map)
        {
            return;
        }

        var team = arguments.ResolveTeam();
        var count = await MobaWaveSpawner.SpawnWaveAsync(map, player.GameContext, team).ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[MOBA] Spawned a {team} lane wave of {count} creeps on '{map.Definition.Name}'.").ConfigureAwait(false);
    }
}
