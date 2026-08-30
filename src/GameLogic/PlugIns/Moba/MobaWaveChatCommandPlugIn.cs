// <copyright file="MobaWaveChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which spawns one MOBA lane wave on the caller's current map: a few
/// creeps that march along a fixed lane path.
/// </summary>
/// <remarks>
/// First test tool for Fase 2 (see GAMEDESIGN.md). W1: pure marching, no combat, no
/// faction, no periodic spawner. Run it while standing on the MOBA Arena (map 200).
/// </remarks>
[Guid("C3A9F1D2-5E47-4B80-9A16-2D8C7B0E4F35")]
[PlugIn]
[Display(Name = "MOBA: spawn lane wave command", Description = "GM command '/mobawave' - spawn one marching lane wave on the current map.")]
[ChatCommandHelp(Command, "Spawn one MOBA lane wave (marching creeps) on your current map.", typeof(EmptyChatCommandArgs))]
public class MobaWaveChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/mobawave";

    /// <summary>
    /// The wave composition: monster definition number and how many of it. Small,
    /// low-level S6 mobs so they read as "creeps".
    /// </summary>
    private static readonly (short Number, int Count)[] WaveComposition =
    {
        (3, 3),  // Spider - small melee
        (15, 3), // Skeleton Archer - small ranged (AttackRange 5)
    };

    /// <summary>
    /// Ordered lane waypoints. Placeholder straight lane down column x=120, which is a
    /// fully-walkable corridor of the flattened arena terrain. Real per-map lane data
    /// comes later.
    /// </summary>
    private static readonly Point[] LaneWaypoints =
    {
        new(120, 60),
        new(120, 110),
        new(120, 160),
        new(120, 205),
    };

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

        var spawn = LaneWaypoints[0];
        var index = 0;
        var total = 0;
        foreach (var (number, count) in WaveComposition)
        {
            var definition = player.GameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == number);
            if (definition is null)
            {
                await player.ShowBlueMessageAsync($"[MOBA] Monster #{number} is not configured.").ConfigureAwait(false);
                continue;
            }

            for (var i = 0; i < count; i++, index++)
            {
                // Stagger the creeps in a column behind the lane start, along the clear corridor.
                var startPoint = new Point(spawn.X, (byte)(spawn.Y - index));
                var area = new MonsterSpawnArea
                {
                    GameMap = map.Definition,
                    MonsterDefinition = definition,
                    SpawnTrigger = SpawnTrigger.OnceAtEventStart,
                    Quantity = 1,
                    X1 = startPoint.X,
                    X2 = startPoint.X,
                    Y1 = startPoint.Y,
                    Y2 = startPoint.Y,
                };

                var monster = new Monster(
                    area,
                    definition,
                    map,
                    player.GameContext.DropGenerator,
                    new MobaLaneCreepIntelligence(LaneWaypoints),
                    player.GameContext.PlugInManager,
                    player.GameContext.PathFinderPool);

                monster.Initialize();
                await map.AddAsync(monster).ConfigureAwait(false);
                monster.OnSpawn();
                total++;
            }
        }

        await player.ShowBlueMessageAsync($"[MOBA] Spawned a lane wave of {total} creeps on '{map.Definition.Name}'.").ConfigureAwait(false);
    }
}
