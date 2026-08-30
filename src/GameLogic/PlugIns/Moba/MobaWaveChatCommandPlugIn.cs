// <copyright file="MobaWaveChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which spawns one MOBA lane wave for a team on the caller's current
/// map: a few creeps that march the mid lane and fight enemies along the way.
/// </summary>
/// <remarks>
/// Test tool for Fase 2 (see GAMEDESIGN.md). Usage: <c>/mobawave</c> (blue, marches
/// south) or <c>/mobawave red</c> (red, marches north). Spawn a wave for each team to
/// see them clash mid lane. Run it while standing on the MOBA Arena (map 200).
/// </remarks>
[Guid("C3A9F1D2-5E47-4B80-9A16-2D8C7B0E4F35")]
[PlugIn]
[Display(Name = "MOBA: spawn lane wave command", Description = "GM command '/mobawave [red]' - spawn a marching lane wave for a team.")]
[ChatCommandHelp(Command, "Spawn a MOBA lane wave (blue marches south, 'red' marches north).", typeof(MobaTeamChatCommandArgs))]
public class MobaWaveChatCommandPlugIn : ChatCommandPlugInBase<MobaTeamChatCommandArgs>
{
    private const string Command = "/mobawave";

    /// <summary>
    /// The wave composition, front rank first. Each entry is spawned as a horizontal
    /// line; ranks are stacked a few tiles behind each other. Small, low-level S6 mobs
    /// so they read as "creeps".
    /// </summary>
    private static readonly (short Number, int Count)[] WaveComposition =
    {
        (3, 3), // Spider - small melee (front)
        (2, 3), // Budge Dragon - small dragon (back). Gets a ranged fire skill in W2.
    };

    /// <summary>Horizontal spacing (tiles) between creeps in a rank and between their parallel tracks.</summary>
    private const int RankSpacingX = 2;

    /// <summary>Distance (tiles) between successive ranks, measured back from the lane start.</summary>
    private const int RankGapY = 2;

    /// <summary>
    /// Ordered mid-lane waypoints for the BLUE team (south-bound), down column x=116
    /// inside the carved mid-lane corridor (x108-124 forced walkable in Terrain201.att).
    /// The RED team walks the same points reversed. Real per-map lane data comes later.
    /// </summary>
    private static readonly Point[] BlueLaneWaypoints =
    {
        new(116, 60),
        new(116, 110),
        new(116, 160),
        new(116, 205),
    };

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
        var lane = team == MobaTeam.Red ? BlueLaneWaypoints.Reverse().ToArray() : BlueLaneWaypoints;
        var spawn = lane[0];

        // The wave marches from spawn toward the next waypoint; ranks stack behind it.
        var behindStep = spawn.Y < lane[^1].Y ? -RankGapY : RankGapY;

        var rank = 0;
        var total = 0;
        foreach (var (number, count) in WaveComposition)
        {
            var definition = player.GameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == number);
            if (definition is null)
            {
                await player.ShowBlueMessageAsync($"[MOBA] Monster #{number} is not configured.").ConfigureAwait(false);
                continue;
            }

            var rankY = (byte)Math.Clamp(spawn.Y + (rank * behindStep), 0, 255);
            var lineWidth = (count - 1) * RankSpacingX;
            for (var i = 0; i < count; i++)
            {
                // Horizontal line, centred on the lane x. Each creep keeps this X offset
                // for its whole march (its own parallel track).
                var offsetX = (i * RankSpacingX) - (lineWidth / 2);
                var startPoint = new Point((byte)Math.Clamp(spawn.X + offsetX, 0, 255), rankY);
                var creepWaypoints = lane
                    .Select(w => new Point((byte)Math.Clamp(w.X + offsetX, 0, 255), w.Y))
                    .ToArray();

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

                var intelligence = new MobaLaneCreepIntelligence(creepWaypoints, team);
                var monster = new Monster(
                    area,
                    definition,
                    map,
                    player.GameContext.DropGenerator,
                    intelligence,
                    player.GameContext.PlugInManager,
                    player.GameContext.PathFinderPool);

                monster.Initialize();
                await map.AddAsync(monster).ConfigureAwait(false);
                monster.OnSpawn();

                // Start the AI now so the creep marches / fights even with no player
                // watching (the base only starts it on the first observer).
                intelligence.Start();
                total++;
            }

            rank++;
        }

        await player.ShowBlueMessageAsync($"[MOBA] Spawned a {team} lane wave of {total} creeps on '{map.Definition.Name}'.").ConfigureAwait(false);
    }
}
