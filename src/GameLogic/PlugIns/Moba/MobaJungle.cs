// <copyright file="MobaJungle.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The neutral jungle camps of the MOBA arena (2026-10-06, coordinates drawn by the user): camps of rank 1-3 (weakest to strongest)
/// of wild monsters that attack any champion and give EXP and gold to the champion that last-hits them. They respawn per camp.
/// </summary>
public static class MobaJungle
{
    /// <summary>One camp: a position and its rank (1 weakest - 3 strongest).</summary>
    /// <param name="X">The x coordinate.</param>
    /// <param name="Y">The y coordinate.</param>
    /// <param name="Rank">The rank.</param>
    public sealed record Camp(int X, int Y, int Rank);

    /// <summary>The camps, as drawn by the user (west half of the map; the east half is still to be drawn).</summary>
    public static readonly IReadOnlyList<Camp> Camps = new Camp[]
    {
        new(58, 64, 1), new(53, 85, 2), new(56, 103, 3), new(93, 50, 1), new(87, 65, 1), new(95, 77, 2), new(95, 97, 3),
        new(59, 115, 3), new(45, 159, 3), new(36, 153, 2), new(64, 174, 1), new(70, 185, 2), new(76, 171, 3),
        new(80, 138, 4),
    };

    /// <summary>Seconds a camp waits to respawn once all its monsters are dead, per rank.</summary>
    public static readonly int[] RespawnSeconds = { 0, 60, 90, 120, 300 };

    /// <summary>Base life per rank (before the level scaling shared with the lane creeps).</summary>
    private static readonly float[] BaseHealth = { 0, 2500f, 4000f, 6500f, 20000f };

    /// <summary>Damage factor per rank over the creep damage.</summary>
    private static readonly float[] DamageFactor = { 0, 1.0f, 1.4f, 1.9f, 2.6f };

    /// <summary>Experience for the last hit of one monster, per rank.</summary>
    private static readonly int[] ExpPerMonster = { 0, 20, 35, 60, 150 };

    /// <summary>Gold for the last hit of one monster, per rank.</summary>
    private static readonly int[] GoldPerMonster = { 0, 40, 70, 120, 300 };

    private static readonly Dictionary<int, (string Name, int Count)[]> Composition = new()
    {
        [1] = new[] { ("Mutant", 3) },
        [2] = new[] { ("Bloody Wolf", 3), ("Mutant", 2) },
        [3] = new[] { ("Bloody Wolf", 2), ("Tantallos", 3) },
        [4] = new[] { (GodOfDarknessName, 1) },
    };

    /// <summary>Designation of the imported test boss (model of the IGC season 21 client, Monster332).</summary>
    public const string GodOfDarknessName = "God of Darkness";

    /// <summary>Monster number the client maps to the God of Darkness model (<c>MONSTER_GOD_OF_DARKNESS</c> in _enum.h).</summary>
    public const short GodOfDarknessNumber = 700;

    private static readonly IDropGenerator NoDrops = new NoDropGenerator();

    private static readonly Dictionary<ushort, JungleState> States = new();

    /// <summary>Spawns every camp on the map (replacing the ones already there).</summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The number of monsters spawned.</returns>
    public static async ValueTask<int> StartAsync(GameMap map, IGameContext gameContext)
    {
        await StopAsync(map).ConfigureAwait(false);
        var state = new JungleState();
        States[map.MapId] = state;
        var total = 0;
        foreach (var camp in Camps)
        {
            total += await SpawnCampAsync(map, gameContext, state, camp).ConfigureAwait(false);
        }

        return total;
    }

    /// <summary>Removes every jungle monster of the map and cancels the pending respawns.</summary>
    /// <param name="map">The arena map.</param>
    /// <returns>The number of monsters removed.</returns>
    public static async ValueTask<int> StopAsync(GameMap map)
    {
        if (!States.Remove(map.MapId, out var state))
        {
            return 0;
        }

        await state.Cancel.CancelAsync().ConfigureAwait(false);
        var removed = 0;
        foreach (var monster in state.Monsters.ToArray())
        {
            try
            {
                await map.RemoveAsync(monster).ConfigureAwait(false);
                monster.Dispose();
                removed++;
            }
            catch
            {
                // already gone
            }
        }

        state.Monsters.Clear();
        return removed;
    }

    private static async ValueTask<int> SpawnCampAsync(GameMap map, IGameContext gameContext, JungleState state, Camp camp)
    {
        var level = MobaMatchPhase.LeaderLevel;
        var healthMultiplier = MobaWaveSpawner.CreepHealthMultiplierAt(level);
        var damageMultiplier = MobaWaveSpawner.CreepDamageMultiplierAt(level) * DamageFactor[camp.Rank];
        var health = BaseHealth[camp.Rank] * healthMultiplier;
        var members = new List<Monster>();
        var index = 0;
        var wanted = Composition[camp.Rank].Sum(c => c.Count);
        foreach (var (name, count) in Composition[camp.Rank])
        {
            var isImported = name == GodOfDarknessName;
            var baseDefinition = gameContext.Configuration.Monsters.FirstOrDefault(m => string.Equals(m.Designation, isImported ? "Tantallos" : name, StringComparison.OrdinalIgnoreCase));
            if (baseDefinition is null)
            {
                continue;
            }

            for (var i = 0; i < count; i++, index++)
            {
                var angle = index * (2 * Math.PI / wanted);
                var spot = MobaLayout.NearestWalkable(camp.X + (Math.Cos(angle) * 2), camp.Y + (Math.Sin(angle) * 2));
                var definition = baseDefinition.Clone(gameContext.Configuration);
                if (isImported)
                {
                    definition.Number = GodOfDarknessNumber;
                    definition.Designation = name;
                }

                var area = new MonsterSpawnArea
                {
                    GameMap = map.Definition,
                    MonsterDefinition = definition,
                    SpawnTrigger = SpawnTrigger.OnceAtEventStart,
                    Quantity = 1,
                    X1 = spot.X,
                    X2 = spot.X,
                    Y1 = spot.Y,
                    Y2 = spot.Y,
                    MaximumHealthOverride = (int)health,
                };

                var monster = new Monster(area, definition, map, NoDrops, new BasicMonsterIntelligence(), gameContext.PlugInManager, gameContext.PathFinderPool);
                monster.Initialize();
                SetAbsolute(monster, Stats.MaximumHealth, health);
                SetAbsolute(monster, Stats.MinimumPhysBaseDmg, 60f * damageMultiplier);
                SetAbsolute(monster, Stats.MaximumPhysBaseDmg, 85f * damageMultiplier);
                SetAbsolute(monster, Stats.DefenseBase, 20f);
                SetAbsolute(monster, Stats.AttackRatePvm, 150f);
                SetAbsolute(monster, Stats.DefenseRatePvm, 30f);
                var rank = camp.Rank;
                monster.Died += (_, death) => _ = OnDiedAsync(map, gameContext, state, camp, monster, death, rank);
                await map.AddAsync(monster).ConfigureAwait(false);
                monster.OnSpawn();
                state.Monsters.Add(monster);
                members.Add(monster);
            }
        }

        state.Alive[camp] = members.Count;
        return members.Count;
    }

    private static async Task OnDiedAsync(GameMap map, IGameContext gameContext, JungleState state, Camp camp, Monster monster, DeathInformation death, int rank)
    {
        try
        {
            if (map.GetObject(death.KillerId) is Player { IsMobaClone: true } killer)
            {
                await MobaExperience.GrantAsync(killer, ExpPerMonster[rank], "jungle").ConfigureAwait(false);
                await MobaGold.GrantAsync(killer, GoldPerMonster[rank], "jungle").ConfigureAwait(false);
            }

            state.Monsters.Remove(monster);
            if (state.Cancel.IsCancellationRequested)
            {
                return;
            }

            if (state.Alive.TryGetValue(camp, out var left) && --left <= 0)
            {
                state.Alive[camp] = 0;
                await Task.Delay(TimeSpan.FromSeconds(RespawnSeconds[rank]), state.Cancel.Token).ConfigureAwait(false);
                await SpawnCampAsync(map, gameContext, state, camp).ConfigureAwait(false);
            }
            else
            {
                state.Alive[camp] = left;
            }
        }
        catch
        {
            // cancelled or best effort
        }
    }

    private static void SetAbsolute(Monster monster, AttributeDefinition stat, float value)
    {
        var current = monster.Attributes[stat];
        monster.Attributes.AddElement(new SimpleElement(value - current, AggregateType.AddRaw), stat);
    }

    private sealed class JungleState
    {
        public CancellationTokenSource Cancel { get; } = new();

        public List<Monster> Monsters { get; } = new();

        public Dictionary<Camp, int> Alive { get; } = new();
    }

    private sealed class NoDropGenerator : IDropGenerator
    {
        public ValueTask<(IEnumerable<Item> Items, uint? Money)> GenerateItemDropsAsync(MonsterDefinition monster, int gainedExperience, Player player)
            => ValueTask.FromResult<(IEnumerable<Item> Items, uint? Money)>((Enumerable.Empty<Item>(), null));

        public Item? GenerateItemDrop(DropItemGroup group) => null;

        public (Item? Item, uint? Money, ItemDropEffect DropEffect) GenerateItemDrop(IEnumerable<DropItemGroup> groups) => (null, null, default);
    }
}
