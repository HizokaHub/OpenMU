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

    private const float TurretHealth = 80000f;
    private const float TurretMinDamage = 170f;
    private const float TurretMaxDamage = 210f;
    private const float TurretDefense = 60f;
    private const float TurretAttackRate = 600f;
    private const byte TurretAttackRange = 7;
    private static readonly TimeSpan TurretAttackDelay = TimeSpan.FromMilliseconds(1100);

    private const short NexusMonsterNumber = 32; // Stone Golem too (bigger, doesn't shoot).

    private const float NexusHealth = 160000f;
    private const float NexusDefense = 40f;

    /// <summary>Mid-lane turret positions: blue guards the north base, red the south base.</summary>
    private static readonly (byte X, byte Y) BlueTurretPos = (116, 92);
    private static readonly (byte X, byte Y) RedTurretPos = (116, 173);

    /// <summary>Nexus positions, behind each base (behind the creep spawn points).</summary>
    private static readonly (byte X, byte Y) BlueNexusPos = (116, 44);
    private static readonly (byte X, byte Y) RedNexusPos = (116, 224);

    // Structures spawned per map, so the toggle commands can remove them.
    private static readonly ConcurrentDictionary<ushort, List<Monster>> TurretsByMap = new();
    private static readonly ConcurrentDictionary<ushort, List<Monster>> NexusesByMap = new();

    /// <summary>
    /// Structure max-HP multiplier of a match phase (2026-10-02, requested by the user): champion damage grows
    /// 15x from level 1 to 30 while the structures were flat, so late hits took a turret in one or two hits.
    /// </summary>
    /// <param name="tier">The match phase.</param>
    /// <returns>The multiplier (T1 x1, T2 x1.8, T3 x3).</returns>
    public static float StructureHealthMultiplier(MobaShopTier tier) => tier switch
    {
        MobaShopTier.T3 => 3f,
        MobaShopTier.T2 => 1.8f,
        _ => 1f,
    };

    /// <summary>Re-scales the max HP of every living turret / nexus to the phase, keeping each one's HP percentage.</summary>
    /// <param name="tier">The new match phase.</param>
    public static void ApplyPhaseHealth(MobaShopTier tier)
    {
        var multiplier = StructureHealthMultiplier(tier);
        foreach (var (monsters, baseHealth) in new[] { (TurretsByMap.Values, TurretHealth), (NexusesByMap.Values, NexusHealth) })
        {
            foreach (var structure in monsters.SelectMany(list => list.ToArray()).Where(m => m.IsAlive))
            {
                var max = structure.Attributes[Stats.MaximumHealth];
                var ratio = max > 0 ? structure.Attributes[Stats.CurrentHealth] / max : 1f;
                SetAbsolute(structure, Stats.MaximumHealth, baseHealth * multiplier);
                structure.Attributes[Stats.CurrentHealth] = Math.Max(1f, structure.Attributes[Stats.MaximumHealth] * ratio);
            }
        }
    }

    /// <summary>Whether turrets are currently spawned on the map.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns><see langword="true"/> if turrets exist.</returns>
    public static bool HasTurrets(ushort mapId) => TurretsByMap.TryGetValue(mapId, out var list) && list.Count > 0;

    /// <summary>Whether nexuses are currently spawned on the map.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns><see langword="true"/> if nexuses exist.</returns>
    public static bool HasNexuses(ushort mapId) => NexusesByMap.TryGetValue(mapId, out var list) && list.Count > 0;

    /// <summary>Spawns one lane turret per team on the map.</summary>
    /// <param name="map">The map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The number of turrets spawned.</returns>
    public static async ValueTask<int> SpawnTurretsAsync(GameMap map, IGameContext gameContext)
    {
        var list = TurretsByMap.GetOrAdd(map.MapId, _ => new List<Monster>());
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
        if (!TurretsByMap.TryRemove(map.MapId, out var list))
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

        var enemyTeam = team == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;
        turret.Died += (_, _) => _ = SafeGrantTeamExpAsync(map, enemyTeam, MobaLevels.TurretKillExp, "turret");

        await map.AddAsync(turret).ConfigureAwait(false);
        turret.OnSpawn();
        intelligence.Start();
        return turret;
    }

    private static async Task SafeGrantTeamExpAsync(GameMap map, MobaTeam team, long amount, string reason)
    {
        try
        {
            await MobaExperience.GrantToTeamAsync(map, team, amount, reason).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    private static void ForceTurretStats(Monster turret)
    {
        SetAbsolute(turret, Stats.MaximumHealth, TurretHealth * StructureHealthMultiplier(MobaMatchPhase.Current));
        SetAbsolute(turret, Stats.MinimumPhysBaseDmg, TurretMinDamage);
        SetAbsolute(turret, Stats.MaximumPhysBaseDmg, TurretMaxDamage);
        SetAbsolute(turret, Stats.DefenseBase, TurretDefense);
        SetAbsolute(turret, Stats.AttackRatePvm, TurretAttackRate);
    }

    /// <summary>
    /// Spawns one nexus per team behind its base. When a nexus dies the match ends
    /// (<see cref="MobaMatchEnder"/>) - its team is the loser.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The number of nexuses spawned.</returns>
    public static async ValueTask<int> SpawnNexusesAsync(GameMap map, IGameContext gameContext)
    {
        var list = NexusesByMap.GetOrAdd(map.MapId, _ => new List<Monster>());
        var count = 0;

        foreach (var (team, position) in new[] { (MobaTeam.Blue, BlueNexusPos), (MobaTeam.Red, RedNexusPos) })
        {
            var nexus = await SpawnNexusAsync(map, gameContext, team, position).ConfigureAwait(false);
            if (nexus is not null)
            {
                list.Add(nexus);
                count++;
            }
        }

        return count;
    }

    /// <summary>Removes and disposes every nexus spawned on the map.</summary>
    /// <param name="map">The map.</param>
    /// <returns>The number of nexuses removed.</returns>
    public static async ValueTask<int> RemoveNexusesAsync(GameMap map)
    {
        if (!NexusesByMap.TryRemove(map.MapId, out var list))
        {
            return 0;
        }

        var removed = 0;
        foreach (var nexus in list)
        {
            MobaStructures.Unmark(nexus);
            MobaTeams.Clear(nexus);
            try
            {
                await map.RemoveAsync(nexus).ConfigureAwait(false);
                nexus.Dispose();
                removed++;
            }
            catch
            {
                // already gone
            }
        }

        return removed;
    }

    private static async ValueTask<Monster?> SpawnNexusAsync(GameMap map, IGameContext gameContext, MobaTeam team, (byte X, byte Y) position)
    {
        var baseDefinition = gameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == NexusMonsterNumber);
        if (baseDefinition is null)
        {
            return null;
        }

        var definition = baseDefinition.Clone(gameContext.Configuration);
        definition.AttackRange = 0;
        definition.ViewRange = 0;
        definition.MoveRange = 0;
        definition.MoveDelay = TimeSpan.FromSeconds(60);

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
            MaximumHealthOverride = (int)NexusHealth,
        };

        var intelligence = new MobaStructureIntelligence(team, MobaStructureType.Nexus, attacks: false);
        var nexus = new Monster(
            area,
            definition,
            map,
            gameContext.DropGenerator,
            intelligence,
            gameContext.PlugInManager,
            gameContext.PathFinderPool);

        nexus.Initialize();
        SetAbsolute(nexus, Stats.MaximumHealth, NexusHealth * StructureHealthMultiplier(MobaMatchPhase.Current));
        SetAbsolute(nexus, Stats.DefenseBase, NexusDefense);

        // Losing team = this nexus's team.
        nexus.Died += (_, _) => _ = SafeEndMatchAsync(map, gameContext, team);

        await map.AddAsync(nexus).ConfigureAwait(false);
        nexus.OnSpawn();
        intelligence.Start();
        return nexus;
    }

    private static async Task SafeEndMatchAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam)
    {
        try
        {
            await MobaMatchEnder.EndMatchAsync(map, gameContext, losingTeam).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    private static void SetAbsolute(Monster structure, AttributeDefinition stat, float value)
    {
        var current = structure.Attributes[stat];
        structure.Attributes.AddElement(new SimpleElement(value - current, AggregateType.AddRaw), stat);
    }
}
