// <copyright file="MobaTurretsChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which toggles the pair of mid-lane turrets (one per team) on the
/// caller's current map. A turret does not move, shoots the highest-priority enemy in
/// range (creeps first, then champions, plus turret-aggro when a champion attacks an
/// ally under it), and is the lowest-priority target for lane creeps.
/// </summary>
/// <remarks>Test tool for Fase 2 (see GAMEDESIGN.md). Run it on the MOBA Arena (map 200).</remarks>
[Guid("E7C2A5B9-4D18-4A63-9F70-1B8E3C6D2A45")]
[PlugIn]
[Display(Name = "MOBA: toggle lane turrets command", Description = "GM command '/mobaturrets' - spawn/remove the mid-lane turrets.")]
[ChatCommandHelp(Command, "Spawn or remove the MOBA mid-lane turrets (one per team).", typeof(EmptyChatCommandArgs))]
public class MobaTurretsChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/mobaturrets";

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

        if (MobaStructureSpawner.HasTurrets(map.MapId))
        {
            var removed = await MobaStructureSpawner.RemoveTurretsAsync(map).ConfigureAwait(false);
            await player.ShowBlueMessageAsync($"[MOBA] Removed {removed} turret(s) from '{map.Definition.Name}'.").ConfigureAwait(false);
            return;
        }

        var count = await MobaStructureSpawner.SpawnTurretsAsync(map, player.GameContext).ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[MOBA] Spawned {count} turret(s) on '{map.Definition.Name}'.").ConfigureAwait(false);
    }
}
