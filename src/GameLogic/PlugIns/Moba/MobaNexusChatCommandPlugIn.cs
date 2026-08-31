// <copyright file="MobaNexusChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which toggles the pair of team nexuses on the caller's current map.
/// A nexus does not move or shoot; when it is destroyed the match ends and its team
/// loses (<see cref="MobaMatchEnder"/>).
/// </summary>
/// <remarks>Test tool for Fase 2 (see GAMEDESIGN.md). Run it on the MOBA Arena (map 200).</remarks>
[Guid("F1A9D2C6-3E85-4B70-8C24-9D6B1E7F3A58")]
[PlugIn]
[Display(Name = "MOBA: toggle nexuses command", Description = "GM command '/mobanexus' - spawn/remove the team nexuses (destroy one to end the match).")]
[ChatCommandHelp(Command, "Spawn or remove the MOBA nexuses (destroying one ends the match).", typeof(EmptyChatCommandArgs))]
public class MobaNexusChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/mobanexus";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        if (player.CurrentMap is not { } map)
        {
            return;
        }

        if (MobaStructureSpawner.HasNexuses(map.MapId))
        {
            var removed = await MobaStructureSpawner.RemoveNexusesAsync(map).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[MOBA] Removed {removed} nexus(es) from '{map.Definition.Name}'.").ConfigureAwait(false);
            return;
        }

        var count = await MobaStructureSpawner.SpawnNexusesAsync(map, player.GameContext).ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[MOBA] Spawned {count} nexus(es) on '{map.Definition.Name}'. Destroy one to end the match.").ConfigureAwait(false);
    }
}
