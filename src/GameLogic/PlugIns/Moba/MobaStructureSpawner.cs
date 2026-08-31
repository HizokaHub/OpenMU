// <copyright file="MobaStructureSpawner.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Builds and spawns MOBA structures (lane turrets, later the nexus) on the arena.
/// Test scaffolding for Fase 2 until a real match context places them from per-map data.
/// </summary>
public static class MobaStructureSpawner
{
    private const short TurretMonsterNumber = 32; // Stone Golem - reads as a defensive structure.

    private const float TurretHealth = 9000f;
    private const float TurretMinDamage = 170f;
    private const float TurretMaxDamage = 210f;
    private const float TurretDefense = 60f;
    private const float TurretAttackRate = 600f;
    private const byte TurretAttackRange = 7;
    private static readonly TimeSpan TurretAttackDelay = TimeSpan.FromMilliseconds(1100);

    /// <summary>Mid-lane turret positions: blue guards the north base, red the south base.</summary>
    private static readonly (byte X, byte Y) BlueTurretPos = (116, 92);
    private static readonly (byte X, byte Y) RedTurretPos = (116, 173);

    // Structures spawned per map, so /mobaturrets can toggle them off.
    private static readonly ConcurrentDictionary<ushort, List<Monster>> SpawnedByMap = new();

    /// <summary>Whether turrets are currently spawned on the map.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns><see langword="true"/> if turrets exist.</returns>
    public static bool HasTurrets(ushort mapId) => SpawnedByMap.TryGetValue(mapId, out var list) && list.Count > 0;

    /// <summary>Spawns one lane turret per team on the map.</summary>
    /// <param name="map">The map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The number of turrets spawned.</returns>
    public static async ValueTask<int> SpawnTurretsAsync(GameMap map, IGameContext gameContext)
    {
        var list = SpawnedByMap.GetOrAdd(map.MapId, _ => new List<Monster>());
        var count = 0;

        foreach (var (team, position) in new[] { (MobaTeam.Blue, BlueTurretPos), (MobaTeam.Red, RedTurretPos) })
        {
            var turret = await SpawnTurretAsync(map, gameContext, team, position).ConfigureAwait(false);
            if (turret is not null)
            {
                list.Add(turret);
                count++;
            }
        }

        return count;
    }

    /// <summary>Removes and disposes every turret spawned on the map.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The number of turrets removed.</returns>
    public static async ValueTask<int> RemoveTurretsAsync(GameMap map)
    {
        if (!SpawnedByMap.TryRemove(map.MapId, out var list))
        {
            return 0;
        }

        var removed = 0;
        foreach (var turret in list)
        {
            MobaStructures.Unmark(turret);
            MobaTeams.Clear(turret);
            try
            {
                await map.RemoveAsync(turret).ConfigureAwait(false);
                turret.Dispose();
                removed++;
            }
            catch
            {
                // already gone
            }
        }

        return removed;
    }

    private static async ValueTask<Monster?> SpawnTurretAsync(GameMap map, IGameContext gameContext, MobaTeam team, (byte X, byte Y) position)
    {
        var baseDefinition = gameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == TurretMonsterNumber);
        if (baseDefinition is null)
        {
            return null;
        }

        // Scalar props copy cleanly on Clone (unlike the Attributes collection).
        var definition = baseDefinition.Clone(gameContext.Configuration);
        definition.AttackRange = TurretAttackRange;
        definition.ViewRange = TurretAttackRange;
        definition.MoveRange = 0;
        definition.AttackDelay = TurretAttackDelay;
        definition.MoveDelay = TimeSpan.FromSeconds(10);

        var area = new MonsterSpawnArea
        {
            GameMap = map.Definition,
            MonsterDefinition = definition,
            SpawnTrigger = SpawnTrigger.OnceAtEventStart,
            Quantity = 1,
            X1 = position.X,
            X2 = position.X,
            Y1 = position.Y,
            Y2 = position.Y,
            MaximumHealthOverride = (int)TurretHealth,
        };

        var intelligence = new MobaStructureIntelligence(team, MobaStructureType.Turret);
        var turret = new Monster(
            area,
            definition,
            map,
            gameContext.DropGenerator,
            intelligence,
            gameContext.PlugInManager,
            gameContext.PathFinderPool);

        turret.Initialize();
        ForceTurretStats(turret);
        await map.AddAsync(turret).ConfigureAwait(false);
        turret.OnSpawn();
        intelligence.Start();
        return turret;
    }

    private static void ForceTurretStats(Monster turret)
    {
        SetAbsolute(turret, Stats.MaximumHealth, TurretHealth);
        SetAbsolute(turret, Stats.MinimumPhysBaseDmg, TurretMinDamage);
        SetAbsolute(turret, Stats.MaximumPhysBaseDmg, TurretMaxDamage);
        SetAbsolute(turret, Stats.DefenseBase, TurretDefense);
        SetAbsolute(turret, Stats.AttackRatePvm, TurretAttackRate);

        static void SetAbsolute(Monster turret, AttributeDefinition stat, float value)
        {
            var current = turret.Attributes[stat];
            turret.Attributes.AddElement(new SimpleElement(value - current, AggregateType.AddRaw), stat);
        }
    }
}
