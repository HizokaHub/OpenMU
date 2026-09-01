// <copyright file="MobaWaveSpawner.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Builds and spawns MOBA lane waves. Shared by the one-shot <c>/mobawave</c> command
/// and the periodic <c>/mobawaves</c> spawner (Fase 2, see GAMEDESIGN.md). All values
/// here are placeholders until the real per-class creep table / per-map lane data land.
/// </summary>
public static class MobaWaveSpawner
{
    /// <summary>
    /// Flat combat stats forced onto every wave creep of both teams, per instance (via
    /// the monster's attribute holder, never the shared config), so a blue Spider and a
    /// red Goblin fight on exactly equal terms - only the sprite differs.
    /// </summary>
    // Lowered from 3000 so a creep-vs-creep front line clears in ~20s instead of a
    // minute: with a slow front line, reinforcements pile up and the narrow lane jams.
    public const float CreepHealth = 1000f;

    /// <summary>
    /// Hard cap on living creeps per team on the map. Only a safety valve against the
    /// runaway pile-up that froze the server (each creep runs its own AI timer + range
    /// scans); a healthy lane with waves flowing sits well under it. Past this the
    /// periodic spawner skips that team's wave until the jam clears.
    /// </summary>
    public const int MaxLiveCreepsPerTeam = 54;

    private const float CreepMinDamage = 60f;
    private const float CreepMaxDamage = 85f;
    private const float CreepDefense = 20f;
    private const float CreepAttackRate = 150f;
    private const float CreepDefenseRate = 30f;

    /// <summary>Horizontal spacing (tiles) between creeps in a rank and between their parallel tracks.</summary>
    private const int RankSpacingX = 2;

    /// <summary>Distance (tiles) between successive ranks, measured back from the lane start.</summary>
    private const int RankGapY = 2;

    /// <summary>
    /// Wave composition per team, front rank first. The two teams use visibly different
    /// small S6 mobs so you can tell whose creeps are whose in a melee.
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

    /// <summary>
    /// Ordered mid-lane waypoints for the BLUE team (south-bound), down column x=116
    /// inside the carved mid-lane corridor (x108-124 forced walkable in Terrain201.att).
    /// The RED team walks the same points reversed.
    /// </summary>
    private static readonly Point[] BlueLaneWaypoints =
    {
        new(116, 60),
        new(116, 110),
        new(116, 160),
        new(116, 205),
    };

    /// <summary>
    /// Spawns one lane wave for <paramref name="team"/> on <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The map to spawn on (the MOBA arena).</param>
    /// <param name="gameContext">The game context (drop generator / plug-ins / path finder pool / config).</param>
    /// <param name="team">The team the wave belongs to.</param>
    /// <returns>The number of creeps spawned.</returns>
    public static async ValueTask<int> SpawnWaveAsync(GameMap map, IGameContext gameContext, MobaTeam team)
    {
        // Don't pour more creeps onto a jammed lane.
        var liveOwnCreeps = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<Monster>()
            .Count(mo => mo.IsAlive && !MobaStructures.IsStructure(mo) && MobaTeams.GetTeam(mo) == team);
        if (liveOwnCreeps >= MaxLiveCreepsPerTeam)
        {
            return 0;
        }

        var composition = team == MobaTeam.Red ? RedWaveComposition : BlueWaveComposition;
        var lane = team == MobaTeam.Red ? BlueLaneWaypoints.Reverse().ToArray() : BlueLaneWaypoints;
        var spawn = lane[0];
        var behindStep = spawn.Y < lane[^1].Y ? -RankGapY : RankGapY;

        var rank = 0;
        var total = 0;
        foreach (var (number, count) in composition)
        {
            var baseDefinition = gameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == number);
            if (baseDefinition is null)
            {
                continue;
            }

            var definition = baseDefinition.Clone(gameContext.Configuration);
            var rankY = (byte)Math.Clamp(spawn.Y + (rank * behindStep), 0, 255);
            var lineWidth = (count - 1) * RankSpacingX;

            for (var i = 0; i < count; i++)
            {
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
                    gameContext.DropGenerator,
                    intelligence,
                    gameContext.PlugInManager,
                    gameContext.PathFinderPool);

                monster.Initialize();
                ForceCreepStats(monster);
                monster.Died += (sender, death) => _ = OnCreepKilledAsync(map, sender as Monster, death);
                await map.AddAsync(monster).ConfigureAwait(false);
                monster.OnSpawn();

                // Start the AI now so the creep marches / fights even with no player watching.
                intelligence.Start();
                total++;
            }

            rank++;
        }

        return total;
    }

    /// <summary>
    /// Grants EXP when a lane creep dies: the last-hitter gets the full value, every
    /// other champion of the killing team within range gets the proximity value.
    /// </summary>
    private static async Task OnCreepKilledAsync(GameMap map, Monster? creep, DeathInformation death)
    {
        try
        {
            var deadTeam = MobaTeams.GetTeam(creep);
            if (deadTeam == MobaTeam.None)
            {
                return;
            }

            var beneficiaryTeam = deadTeam == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;
            var deathPosition = creep?.Position ?? default;
            var lastHitter = map.GetObject(death.KillerId) as Player;

            var champions = map.GetAttackablesInRange(deathPosition, MobaLevels.ShareRadius)
                .OfType<Player>()
                .Where(p => p.IsMobaClone && MobaTeams.GetTeam(p) == beneficiaryTeam)
                .ToList();

            var lastHitterRewarded = false;
            foreach (var champion in champions)
            {
                var isLastHit = ReferenceEquals(champion, lastHitter);
                lastHitterRewarded |= isLastHit;
                await MobaExperience.GrantAsync(
                    champion,
                    isLastHit ? MobaLevels.CreepLastHitExp : MobaLevels.CreepProximityExp,
                    isLastHit ? "creep" : "creep-nearby").ConfigureAwait(false);
            }

            // Ranged last hit from just outside the proximity radius still gets the full value.
            if (!lastHitterRewarded && lastHitter is { IsMobaClone: true } && MobaTeams.GetTeam(lastHitter) == beneficiaryTeam)
            {
                await MobaExperience.GrantAsync(lastHitter, MobaLevels.CreepLastHitExp, "creep").ConfigureAwait(false);
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>
    /// Removes and disposes every living lane creep on the map (team-tagged monsters
    /// that are not structures). Used on match end.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <returns>The number of creeps removed.</returns>
    public static async ValueTask<int> DespawnAllCreepsAsync(GameMap map)
    {
        var creeps = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<Monster>()
            .Where(m => MobaTeams.GetTeam(m) != MobaTeam.None && !MobaStructures.IsStructure(m))
            .ToList();

        var removed = 0;
        foreach (var creep in creeps)
        {
            MobaTeams.Clear(creep);
            try
            {
                await map.RemoveAsync(creep).ConfigureAwait(false);
                creep.Dispose();
                removed++;
            }
            catch
            {
                // already gone
            }
        }

        return removed;
    }

    /// <summary>
    /// Forces the flat creep combat stats onto one spawned monster instance, so both
    /// teams' creeps fight identically no matter the base mob. Per-instance attribute
    /// element (AddRaw that cancels the base and sets the target); never the shared config.
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
