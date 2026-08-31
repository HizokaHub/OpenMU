// <copyright file="MobaWaveChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
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
    /// Flat combat stats forced onto every wave creep of both teams, per instance (via
    /// the monster's <see cref="Attributes"/> holder, never the shared config), so a
    /// blue Spider and a red Butterfly fight on exactly equal terms - only the sprite
    /// differs. Placeholder numbers; the real per-class creep table comes in the balance
    /// pass.
    /// </summary>
    private const float CreepHealth = 3000f;

    private const float CreepMinDamage = 45f;

    private const float CreepMaxDamage = 60f;

    private const float CreepDefense = 20f;

    private const float CreepAttackRate = 150f;

    private const float CreepDefenseRate = 30f;

    /// <summary>
    /// Wave composition per team, front rank first. Each entry is spawned as a
    /// horizontal line; ranks are stacked a few tiles behind each other. The two teams
    /// use visibly different small S6 mobs so that, in a melee, you can tell whose
    /// creeps are whose at a glance (a proper team-coloured HP bar comes later).
    /// </summary>
    private static readonly (short Number, int Count)[] BlueWaveComposition =
    {
        (3, 3),  // Spider - small melee (front)
        (24, 3), // Worm - small melee (back)
    };

    private static readonly (short Number, int Count)[] RedWaveComposition =
    {
        (26, 3),  // Goblin - small melee (front)
        (418, 3), // Strange Rabbit - small melee (back)
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
        var composition = team == MobaTeam.Red ? RedWaveComposition : BlueWaveComposition;
        var lane = team == MobaTeam.Red ? BlueLaneWaypoints.Reverse().ToArray() : BlueLaneWaypoints;
        var spawn = lane[0];

        // The wave marches from spawn toward the next waypoint; ranks stack behind it.
        var behindStep = spawn.Y < lane[^1].Y ? -RankGapY : RankGapY;

        var rank = 0;
        var total = 0;
        foreach (var (number, count) in composition)
        {
            var baseDefinition = player.GameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == number);
            if (baseDefinition is null)
            {
                await player.ShowBlueMessageAsync($"[MOBA] Monster #{number} is not configured.").ConfigureAwait(false);
                continue;
            }

            // Per-match copy of the definition (kept for future per-match tweaks like a
            // fireball skill). NOTE: MonsterDefinition.Clone re-links each MonsterAttribute
            // to the SHARED config instance (AssignCollection matches by Id), so mutating
            // definition.Attributes[...] would corrupt the real Lorencia mobs. Combat stats
            // are instead forced per-instance below (ForceCreepStats), and starting HP via
            // MonsterSpawnArea.MaximumHealthOverride (same mechanism PlayerSummon uses).
            var definition = baseDefinition.Clone(player.GameContext.Configuration);

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
                    MaximumHealthOverride = (int)CreepHealth,
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
                ForceCreepStats(monster);
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

    /// <summary>
    /// Forces the flat creep combat stats onto a single spawned monster instance, so
    /// both teams' creeps fight identically no matter which base mob they use. Works on
    /// the per-instance <see cref="MonsterAttributeHolder"/> (an added raw element that
    /// cancels the base value and sets the target), never the shared configuration.
    /// </summary>
    private static void ForceCreepStats(Monster monster)
    {
        // MaximumHealth must match the MaximumHealthOverride we start Health at, or the
        // health-percent the client bar shows is current / base-mob-max (~60) and stays
        // pinned at 100% until the creep is nearly dead.
        SetAbsolute(monster, Stats.MaximumHealth, CreepHealth);
        SetAbsolute(monster, Stats.MinimumPhysBaseDmg, CreepMinDamage);
        SetAbsolute(monster, Stats.MaximumPhysBaseDmg, CreepMaxDamage);
        SetAbsolute(monster, Stats.DefenseBase, CreepDefense);
        SetAbsolute(monster, Stats.AttackRatePvm, CreepAttackRate);
        SetAbsolute(monster, Stats.DefenseRatePvm, CreepDefenseRate);

        static void SetAbsolute(Monster monster, AttributeDefinition stat, float value)
        {
            var current = monster.Attributes[stat];
            monster.Attributes.AddElement(new SimpleElement(value - current, AggregateType.AddRaw), stat);
        }
    }
}
