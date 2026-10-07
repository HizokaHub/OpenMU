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
    /// Creep life multiplier at a champion level (2026-10-04): champion hits grow ~15x (level scale) and more with the build, so a
    /// flat 1,000 HP creep died to air by level 20+. Follows the damage scale with a build factor (1x at level 1, 25x at 30).
    /// </summary>
    /// <param name="level">The highest champion level of the match.</param>
    /// <returns>The multiplier of <see cref="CreepHealth"/>.</returns>
    public static float CreepHealthMultiplierAt(int level)
        => (float)(MobaProgression.DamageScale(level) * (1.0 + ((Math.Clamp(level, 1, 30) - 1) / 29.0)));

    /// <summary>Creep damage multiplier at a champion level: half the champion life curve, so late creeps still matter to a 50,000 HP champion.</summary>
    /// <param name="level">The highest champion level of the match.</param>
    /// <returns>The multiplier of the base creep damage (at least 1).</returns>
    public static float CreepDamageMultiplierAt(int level)
        => Math.Max(1f, 0.5f * MobaProgression.HealthAt(level) / MobaProgression.HealthAt(1));

    /// <summary>
    /// Hard cap on living creeps per team on the map. Only a safety valve against the
    /// runaway pile-up that froze the server (each creep runs its own AI timer + range
    /// scans); a healthy lane with waves flowing sits well under it. Past this the
    /// periodic spawner skips that team's wave until the jam clears.
    /// </summary>
    public const int MaxLiveCreepsPerTeam = 150;

    /// <summary>Hard cap on living creeps of a team per lane, so one jammed lane never starves the others of their wave.</summary>
    public const int MaxLiveCreepsPerLane = 50;

    private static readonly IDropGenerator CreepDropGenerator = new MobaCreepDropGenerator();

    /// <summary>The unit direction of the lane segment starting at waypoint <paramref name="index"/>.</summary>
    private static (double X, double Y) SegmentDirection(IReadOnlyList<Point> lane, int index)
    {
        for (var i = Math.Clamp(index, 0, lane.Count - 2); i < lane.Count - 1; i++)
        {
            double dx = lane[i + 1].X - lane[i].X, dy = lane[i + 1].Y - lane[i].Y;
            var length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length > 0)
            {
                return (dx / length, dy / length);
            }
        }

        return (0, 1);
    }

    /// <summary>The walkable cell nearest to a position (creeps spread across a lane must not start inside a wall).</summary>
    private static Point SnapToWalkable(GameMapTerrain terrain, double x, double y)
    {
        int cx = (int)Math.Clamp(Math.Round(x), 0, 255), cy = (int)Math.Clamp(Math.Round(y), 0, 255);
        if (terrain.WalkMap[cx, cy])
        {
            return new Point((byte)cx, (byte)cy);
        }

        for (var radius = 1; radius <= 6; radius++)
        {
            for (var dy = -radius; dy <= radius; dy++)
            {
                for (var dx = -radius; dx <= radius; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (nx is >= 0 and <= 255 && ny is >= 0 and <= 255 && Math.Max(Math.Abs(dx), Math.Abs(dy)) == radius && terrain.WalkMap[nx, ny])
                    {
                        return new Point((byte)nx, (byte)ny);
                    }
                }
            }
        }

        return new Point((byte)cx, (byte)cy);
    }

    private const float CreepMinDamage = 60f;
    private const float CreepMaxDamage = 85f;
    private const float CreepDefense = 20f;
    private const float CreepAttackRate = 150f;
    private static readonly TimeSpan CreepAttackDelay = TimeSpan.FromMilliseconds(1700);
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
    /// The ordered lane waypoints a unit of <paramref name="team"/> follows, from its own
    /// creep spawn to the enemy creep spawn (lanes come from <see cref="MobaLayout"/>).
    /// </summary>
    /// <param name="team">The team.</param>
    /// <param name="lane">The lane index (0 top, 1 mid, 2 bot).</param>
    /// <returns>The waypoints, start first.</returns>
    public static IReadOnlyList<Point> LaneWaypointsFor(MobaTeam team, int lane = MobaLayout.MidLane)
        => MobaLayout.WaypointsFor(team, lane);

    /// <summary>
    /// Spawns one lane wave for <paramref name="team"/> on <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The map to spawn on (the MOBA arena).</param>
    /// <param name="gameContext">The game context (drop generator / plug-ins / path finder pool / config).</param>
    /// <param name="team">The team the wave belongs to.</param>
    /// <returns>The number of creeps spawned.</returns>
    public static async ValueTask<int> SpawnWaveAsync(GameMap map, IGameContext gameContext, MobaTeam team)
    {
        var total = 0;
        for (var lane = 0; lane < MobaLayout.LaneCount; lane++)
        {
            total += await SpawnLaneWaveAsync(map, gameContext, team, lane).ConfigureAwait(false);
        }

        return total;
    }

    /// <summary>Spawns one wave of <paramref name="team"/> in one lane.</summary>
    /// <param name="map">The map to spawn on.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="team">The team the wave belongs to.</param>
    /// <param name="laneIndex">The lane index.</param>
    /// <returns>The number of creeps spawned.</returns>
    public static async ValueTask<int> SpawnLaneWaveAsync(GameMap map, IGameContext gameContext, MobaTeam team, int laneIndex)
    {
        // Don't pour more creeps onto a jammed lane.
        var liveOwn = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<Monster>()
            .Where(mo => mo.IsAlive && !MobaStructures.IsStructure(mo) && MobaTeams.GetTeam(mo) == team)
            .ToList();
        if (liveOwn.Count >= MaxLiveCreepsPerTeam
            || liveOwn.Count(mo => MobaLayout.NearestLane(mo.Position.X, mo.Position.Y) == laneIndex) >= MaxLiveCreepsPerLane)
        {
            return 0;
        }

        var leaderLevel = MobaMatchPhase.LeaderLevel;
        var creepHealthMul = CreepHealthMultiplierAt(leaderLevel);
        var creepDamageMul = CreepDamageMultiplierAt(leaderLevel);
        var composition = team == MobaTeam.Red ? RedWaveComposition : BlueWaveComposition;
        var lane = MobaLayout.WaypointsFor(team, laneIndex);
        var terrain = map.Terrain;
        var spawn = lane[0];
        var (dirX, dirY) = SegmentDirection(lane, 0);

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

            // The four S6 mobs differ in attack delay (Spider 1800, Worm 1600, Goblin 1800, Rabbit 1500 ms), which would make one
            // team's creeps hit harder than the other's; both teams share the same cadence.
            definition.AttackDelay = CreepAttackDelay;
            var lineWidth = (count - 1) * RankSpacingX;

            for (var i = 0; i < count; i++)
            {
                // Ranks line up behind the spawn along the lane, creeps of a rank side by side across it.
                var offset = (i * RankSpacingX) - (lineWidth / 2.0);
                var startPoint = SnapToWalkable(
                    terrain,
                    spawn.X - (dirX * rank * RankGapY) - (dirY * offset),
                    spawn.Y - (dirY * rank * RankGapY) + (dirX * offset));
                var creepWaypoints = lane
                    .Select((w, k) =>
                    {
                        var (sx, sy) = SegmentDirection(lane, Math.Min(k, lane.Count - 2));
                        return SnapToWalkable(terrain, w.X - (sy * offset), w.Y + (sx * offset));
                    })
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
                    MaximumHealthOverride = (int)(CreepHealth * creepHealthMul),
                };

                var intelligence = new MobaLaneCreepIntelligence(creepWaypoints, team);
                var monster = new Monster(
                    area,
                    definition,
                    map,
                    CreepDropGenerator,
                    intelligence,
                    gameContext.PlugInManager,
                    gameContext.PathFinderPool);

                monster.Initialize();
                ForceCreepStats(monster, creepHealthMul, creepDamageMul);
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
                await MobaGold.GrantAsync(
                    champion,
                    isLastHit ? MobaGold.CreepLastHitGold : MobaGold.CreepProximityGold,
                    isLastHit ? "creep" : "creep-nearby").ConfigureAwait(false);
            }

            // Ranged last hit from just outside the proximity radius still gets the full value.
            if (!lastHitterRewarded && lastHitter is { IsMobaClone: true } && MobaTeams.GetTeam(lastHitter) == beneficiaryTeam)
            {
                await MobaExperience.GrantAsync(lastHitter, MobaLevels.CreepLastHitExp, "creep").ConfigureAwait(false);
                await MobaGold.GrantAsync(lastHitter, MobaGold.CreepLastHitGold, "creep").ConfigureAwait(false);
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
    private static void ForceCreepStats(Monster monster, float healthMul, float damageMul)
    {
        // MaximumHealth must match the MaximumHealthOverride we start Health at, or the
        // health-percent the client bar shows is current / base-mob-max (~60) and stays
        // pinned at 100% until the creep is nearly dead.
        SetAbsolute(monster, Stats.MaximumHealth, CreepHealth * healthMul);
        SetAbsolute(monster, Stats.MinimumPhysBaseDmg, CreepMinDamage * damageMul);
        SetAbsolute(monster, Stats.MaximumPhysBaseDmg, CreepMaxDamage * damageMul);
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
