// <copyright file="MobaArenaTestChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Arguments of <c>/mobaarenatest</c>.
/// </summary>
public class MobaArenaTestChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the action: new, back, list, structures or close.
    /// </summary>
    [Argument("action", false)]
    public string? Action { get; set; }
}

/// <summary>
/// GM test tool for the arena instances (stage 2): puts the caller on its own copy of the arena map
/// and back, lists the copies, and spawns structures on the map the caller is on, so one can see
/// that two arenas do not share players, structures or any other state.
/// </summary>
[Guid("3B6E2D90-5A1C-4F47-8D3E-9C0A7B1E4F28")]
[PlugIn]
[Display(Name = "MOBA: arena instance test command", Description = "GM command '/mobaarenatest new|back|list|structures|close' - tests extra instances of the arena map.")]
[ChatCommandHelp(Command, "Test the arena instances: new (own copy of the arena), back (shared arena), list, structures (turrets + nexus on your map), close (remove empty copies).", typeof(MobaArenaTestChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaArenaTestChatCommandPlugIn : ChatCommandPlugInBase<MobaArenaTestChatCommandArgs>
{
    private const string Command = "/mobaarenatest";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaArenaTestChatCommandArgs arguments)
    {
        switch (arguments.Action?.Trim().ToLowerInvariant())
        {
            case "new":
                await MoveToNewInstanceAsync(player).ConfigureAwait(false);
                break;
            case "back":
                await MoveBackAsync(player).ConfigureAwait(false);
                break;
            case "structures":
                await SpawnStructuresAsync(player).ConfigureAwait(false);
                break;
            case "close":
                await CloseEmptyAsync(player).ConfigureAwait(false);
                break;
            default:
                await ListAsync(player).ConfigureAwait(false);
                break;
        }
    }

    private static string Describe(GameMap map, int players)
        => $"{(map.IsInstance ? "copia" : "compartida")} {map.Id.ToString()[..8]}: {players} jugador(es), torretas={(MobaStructureSpawner.HasTurrets(map) ? "si" : "no")}, nexos={(MobaStructureSpawner.HasNexuses(map) ? "si" : "no")}";

    private static async ValueTask MoveToNewInstanceAsync(Player player)
    {
        if (player.CurrentMap is not { } current)
        {
            return;
        }

        var map = await player.GameContext.CreateInstanceMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false);
        if (map is null)
        {
            await player.ShowBlueMessageAsync("[MOBA] No se pudo crear la copia de la arena.").ConfigureAwait(false);
            return;
        }

        player.BoundMap = map;
        await player.ShowBlueMessageAsync($"[MOBA] Te muevo a la copia {map.Id.ToString()[..8]} de la arena...").ConfigureAwait(false);
        await player.TeleportToMapAsync(map, current.MapId == map.MapId ? player.Position : MobaLayout.NearestWalkable(116, 20)).ConfigureAwait(false);
    }

    private static async ValueTask MoveBackAsync(Player player)
    {
        if (await player.GameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false) is not { } shared)
        {
            return;
        }

        player.BoundMap = null;
        await player.ShowBlueMessageAsync("[MOBA] Vuelves a la arena compartida...").ConfigureAwait(false);
        await player.TeleportToMapAsync(shared, player.Position).ConfigureAwait(false);
    }

    private static async ValueTask SpawnStructuresAsync(Player player)
    {
        if (player.CurrentMap is not { } map || map.MapId != MobaCloneFactory.ArenaMapNumber)
        {
            await player.ShowBlueMessageAsync("[MOBA] Tienes que estar en la arena.").ConfigureAwait(false);
            return;
        }

        var spawned = 0;
        if (!MobaStructureSpawner.HasTurrets(map))
        {
            spawned += await MobaStructureSpawner.SpawnTurretsAsync(map, player.GameContext).ConfigureAwait(false);
        }

        if (!MobaStructureSpawner.HasNexuses(map))
        {
            spawned += await MobaStructureSpawner.SpawnNexusesAsync(map, player.GameContext).ConfigureAwait(false);
        }

        await player.ShowBlueMessageAsync($"[MOBA] {spawned} estructura(s) creadas en la arena {map.Id.ToString()[..8]}.").ConfigureAwait(false);
    }

    private static async ValueTask CloseEmptyAsync(Player player)
    {
        var players = await player.GameContext.GetPlayersAsync().ConfigureAwait(false);
        var removed = 0;
        foreach (var map in (await player.GameContext.GetMapsAsync().ConfigureAwait(false)).Where(m => m.IsInstance && m.MapId == MobaCloneFactory.ArenaMapNumber).ToList())
        {
            if (players.Any(p => ReferenceEquals(p.CurrentMap, map) || ReferenceEquals(p.BoundMap, map)))
            {
                continue;
            }

            await MobaStructureSpawner.RemoveTurretsAsync(map).ConfigureAwait(false);
            await MobaStructureSpawner.RemoveNexusesAsync(map).ConfigureAwait(false);
            removed += await player.GameContext.RemoveInstanceMapAsync(map).ConfigureAwait(false) ? 1 : 0;
        }

        await player.ShowBlueMessageAsync($"[MOBA] {removed} copia(s) vacia(s) eliminadas.").ConfigureAwait(false);
    }

    private static async ValueTask ListAsync(Player player)
    {
        var players = await player.GameContext.GetPlayersAsync().ConfigureAwait(false);
        var maps = (await player.GameContext.GetMapsAsync().ConfigureAwait(false)).Where(m => m.MapId == MobaCloneFactory.ArenaMapNumber).ToList();
        await player.ShowBlueMessageAsync($"[MOBA] {maps.Count} arena(s). Tu mapa: {(player.CurrentMap is { } mine ? mine.Id.ToString()[..8] : "ninguno")}").ConfigureAwait(false);
        foreach (var map in maps)
        {
            var inMap = players.Count(p => ReferenceEquals(p.CurrentMap, map));
            await player.ShowBlueMessageAsync("[MOBA] " + Describe(map, inMap)).ConfigureAwait(false);
        }
    }
}
