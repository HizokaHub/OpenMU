// <copyright file="MobaJungle.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The neutral jungle of the MOBA arena (redefined 2026-10-17 on the current map, coordinates drawn by the user for the blue half; the red half is
/// the blue one turned 180 degrees, x -> 236 - x, y -> 255 - y). Every spot holds one or more groups of wild monsters standing side by side, small ones in
/// front (<see cref="MobaJungleFormation"/>). They never wander; when a champion hits one, the whole spot defends and chases up to 10 tiles, then
/// walks back and recovers 10 % of its life per second (<see cref="MobaJungleIntelligence"/>). A group respawns only once all its monsters are dead.
/// The Selupan event (eggs, spiders and the boss) lives in <see cref="MobaSelupanEvent"/>.
/// </summary>
public static class MobaJungle
{
    /// <summary>The kinds of jungle monsters.</summary>
    public enum Kind
    {
        /// <summary>Mutant (Tarkan).</summary>
        Mutant,

        /// <summary>Bloody Wolf (Tarkan).</summary>
        BloodyWolf,

        /// <summary>Tantallos (Tarkan).</summary>
        Tantallos,

        /// <summary>Bloody Orc (Aida).</summary>
        BloodyOrc,

        /// <summary>Bloody Death Rider (Aida).</summary>
        BloodyDeathRider,

        /// <summary>Medusa (Swamp of Quiet).</summary>
        Medusa,

        /// <summary>Dark Phoenix, the "fenix" (Icarus).</summary>
        DarkPhoenix,

        /// <summary>The God of Darkness imported from the season 21 client.</summary>
        GodOfDarkness,

        /// <summary>Kundun (the "Illusion of Kundun 7" of Kalima 7).</summary>
        Kundun,

        /// <summary>Spider egg of the Selupan event, type 1.</summary>
        SpiderEgg1,

        /// <summary>Spider egg, type 2.</summary>
        SpiderEgg2,

        /// <summary>Spider egg, type 3.</summary>
        SpiderEgg3,

        /// <summary>Spider hatched from an egg.</summary>
        Spider,

        /// <summary>Selupan, boss of the Raklion event.</summary>
        Selupan,
    }

    /// <summary>Designation of the imported boss (model of the IGC season 21 client, Monster332).</summary>
    public const string GodOfDarknessName = "God of Darkness";

    /// <summary>Monster number the client maps to the God of Darkness model (<c>MONSTER_GOD_OF_DARKNESS</c> in _enum.h).</summary>
    public const short GodOfDarknessNumber = 700;

    /// <summary>Monster number of Medusa (client <c>MONSTER_MEDUSA</c>); the server configuration has no Medusa, it is cloned from a Tantallos.</summary>
    public const short MedusaNumber = 561;

    /// <summary>Fraction of the EXP and gold of an epic monster (boss) every other living champion of the killing team gets.</summary>
    public const double EpicTeamShare = 0.4;

    /// <summary>What the game knows about a kind of monster.</summary>
    /// <param name="Name">The designation of the monster of the configuration it is cloned from (or, with a number, the display name).</param>
    /// <param name="Number">Number of the monster the definition is cloned from; 0 = find it by <paramref name="Name"/>.</param>
    /// <param name="ClonedFrom">When the kind is not in the configuration: the designation of the monster to clone (it gets <paramref name="Number"/> and <paramref name="Name"/>).</param>
    /// <param name="BaseHealth">Life before the level scaling shared with the lane creeps.</param>
    /// <param name="DamageFactor">Damage factor over the creep damage.</param>
    /// <param name="Exp">EXP for the last hit.</param>
    /// <param name="Gold">Gold for the last hit.</param>
    /// <param name="Size">Size class for the formation (1 smallest, in front).</param>
    /// <param name="Epic">Whether the kill also pays the whole team (<see cref="EpicTeamShare"/>).</param>
    /// <param name="AttackRange">Attack range override in tiles (0 = keep).</param>
    /// <param name="Respawn">Seconds the group takes to respawn once all its monsters are dead.</param>
    public sealed record KindInfo(string Name, short Number, string? ClonedFrom, float BaseHealth, float DamageFactor, int Exp, int Gold, int Size, bool Epic, int AttackRange, int Respawn);

    /// <summary>One group of the monsters of a spot, with its own respawn.</summary>
    /// <param name="Members">What it holds.</param>
    public sealed record Group(params (Kind Kind, int Count)[] Members)
    {
        /// <summary>Gets the seconds the group takes to respawn: the slowest of its members.</summary>
        public int RespawnSeconds => this.Members.Max(m => Info[m.Kind].Respawn);
    }

    /// <summary>One spot of the jungle.</summary>
    /// <param name="Name">A name for the logs.</param>
    /// <param name="X">The x coordinate (drawn by the user).</param>
    /// <param name="Y">The y coordinate.</param>
    /// <param name="Groups">The groups standing there (the whole spot is laid out as one formation).</param>
    public sealed record Spot(string Name, int X, int Y, params Group[] Groups);

    private static readonly Group Rank1 = new((Kind.Mutant, 3));

    private static readonly Group Rank2 = new((Kind.BloodyWolf, 3), (Kind.Mutant, 2));

    private static readonly Group Rank3 = new((Kind.BloodyWolf, 2), (Kind.Tantallos, 3));

    private static readonly Group Orcs2 = new((Kind.BloodyOrc, 2));

    private static readonly Group Orcs3 = new((Kind.BloodyOrc, 3));

    private static readonly Group Orcs2Rider = new((Kind.BloodyOrc, 2), (Kind.BloodyDeathRider, 1));

    private static readonly Group Orcs3Rider = new((Kind.BloodyOrc, 3), (Kind.BloodyDeathRider, 1));

    /// <summary>Gets the kinds of monsters.</summary>
    public static IReadOnlyDictionary<Kind, KindInfo> Info { get; } = new Dictionary<Kind, KindInfo>
    {
        [Kind.Mutant] = new("Mutant", 0, null, 2500f, 1.0f, 25, 50, 2, false, 0, 60),
        [Kind.BloodyWolf] = new("Bloody Wolf", 0, null, 4000f, 1.4f, 35, 70, 1, false, 0, 90),
        [Kind.Tantallos] = new("Tantallos", 0, null, 6500f, 1.9f, 60, 120, 3, false, 0, 120),
        [Kind.BloodyOrc] = new("Bloody Orc", 0, null, 4500f, 1.5f, 40, 80, 2, false, 0, 75),
        [Kind.BloodyDeathRider] = new("Bloody Death Rider", 0, null, 7500f, 2.0f, 70, 140, 3, false, 0, 110),
        [Kind.Medusa] = new("Medusa", MedusaNumber, "Tantallos", 16000f, 2.4f, 250, 450, 4, false, 5, 240),
        [Kind.DarkPhoenix] = new("Dark Phoenix", 0, null, 16000f, 2.4f, 250, 450, 4, false, 6, 240),
        [Kind.GodOfDarkness] = new(GodOfDarknessName, GodOfDarknessNumber, "Tantallos", 20000f, 2.6f, 400, 700, 4, true, 3, 300),
        [Kind.Kundun] = new("Illusion of Kundun 7", 0, null, 32000f, 3.0f, 700, 1100, 4, true, 8, 360),
        [Kind.SpiderEgg1] = new("Spider Eggs 1", 0, null, 1500f, 0f, 15, 30, 1, false, 0, 300),
        [Kind.SpiderEgg2] = new("Spider Eggs 2", 0, null, 2500f, 0f, 25, 50, 1, false, 0, 300),
        [Kind.SpiderEgg3] = new("Spider Eggs 3", 0, null, 4000f, 0f, 40, 80, 1, false, 0, 300),
        [Kind.Spider] = new("Hell Spider", 0, null, 2200f, 1.1f, 20, 40, 1, false, 0, 0),
        [Kind.Selupan] = new("Selupan", 0, null, 45000f, 3.4f, 1000, 1500, 4, true, 6, 0),
    };

    /// <summary>
    /// Gets the spots of the blue half, as drawn by the user (the "mobs 1/2/3" are the ranks <see cref="Rank1"/>, <see cref="Rank2"/>, <see cref="Rank3"/>).
    /// Kundun is only in the blue half: the red half has the Selupan event instead (<see cref="MobaSelupanEvent"/>).
    /// </summary>
    public static IReadOnlyList<Spot> BlueSpots { get; } = new Spot[]
    {
        new("B-98,95", 98, 95, Rank2),
        new("B-142,109", 142, 109, Rank3),
        new("B-169,102", 169, 102, Rank2),
        new("B-177,85", 177, 85, Rank1),
        new("B-150,70 (camp + God of Darkness)", 150, 70, Rank2, new Group((Kind.GodOfDarkness, 1))),
        new("B-59,114 (Kundun)", 59, 114, new Group((Kind.Kundun, 1))),
        new("B-62,65 (Medusa)", 62, 65, new Group((Kind.Medusa, 1))),
        new("B-65,85", 65, 85, Orcs2Rider),
        new("B-24,107 (Dark Phoenix)", 24, 107, new Group((Kind.DarkPhoenix, 1))),
        new("B-94,109", 94, 109, Orcs3),
        new("B-103,116", 103, 116, Orcs2),
        new("B-165,87", 165, 87, Orcs3Rider),
        new("B-178,102", 178, 102, Orcs2),
        new("B-200,101", 200, 101, Orcs2),
        new("B-203,116", 203, 116, Orcs2),
    };

    /// <summary>Gets the spots of the whole jungle: the blue ones and their 180 degree turn for the red half (Kundun is not copied).</summary>
    public static IReadOnlyList<Spot> Spots { get; } = BlueSpots
        .Concat(BlueSpots
            .Where(s => !s.Groups.Any(g => g.Members.Any(m => m.Kind == Kind.Kundun)))
            .Select(s => new Spot("R" + s.Name[1..], 236 - s.X, 255 - s.Y, s.Groups)))
        .ToArray();

    private static readonly IDropGenerator NoDrops = new NoDropGenerator();

    private static readonly Dictionary<ushort, JungleState> States = new();

    private static readonly SemaphoreSlim EnsureLock = new(1, 1);

    private static readonly Dictionary<Spot, IReadOnlyList<MobaJungleFormation.Slot>> Layouts = new();

    /// <summary>Gets the position of every monster of a spot, in the order of its groups and members.</summary>
    /// <param name="spot">The spot.</param>
    /// <returns>The kinds and slots.</returns>
    public static IReadOnlyList<(Kind Kind, int Group, MobaJungleFormation.Slot Slot)> MembersOf(Spot spot)
    {
        var kinds = spot.Groups
            .SelectMany((g, gi) => g.Members.SelectMany(m => Enumerable.Repeat((m.Kind, Group: gi), m.Count)))
            .ToList();
        IReadOnlyList<MobaJungleFormation.Slot> slots;
        lock (Layouts)
        {
            if (!Layouts.TryGetValue(spot, out slots!))
            {
                slots = MobaJungleFormation.Layout(spot.X, spot.Y, kinds.Select(k => Info[k.Kind].Size).ToList());
                Layouts[spot] = slots;
            }
        }

        return kinds.Select((k, i) => (k.Kind, k.Group, slots[i])).ToList();
    }

    /// <summary>Spawns the whole jungle (replacing the one already there) and the Selupan event.</summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The number of monsters spawned.</returns>
    public static async ValueTask<int> StartAsync(GameMap map, IGameContext gameContext)
    {
        await StopAsync(map).ConfigureAwait(false);
        var state = new JungleState();
        States[map.MapId] = state;
        var total = 0;
        foreach (var spot in Spots)
        {
            for (var g = 0; g < spot.Groups.Length; g++)
            {
                total += await SpawnGroupAsync(map, gameContext, state, spot, g).ConfigureAwait(false);
            }
        }

        total += await MobaSelupanEvent.StartAsync(map, gameContext).ConfigureAwait(false);
        return total;
    }

    /// <summary>Starts the jungle when it is not running yet (called when a champion enters the arena, like the structures).</summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>The number of monsters spawned.</returns>
    public static async ValueTask<int> EnsureStartedAsync(IGameContext gameContext)
    {
        var map = await gameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false);
        if (map is null)
        {
            return 0;
        }

        await EnsureLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return States.ContainsKey(map.MapId) ? 0 : await StartAsync(map, gameContext).ConfigureAwait(false);
        }
        finally
        {
            EnsureLock.Release();
        }
    }

    /// <summary>Whether the jungle of the map is running.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns><see langword="true"/> if it is.</returns>
    public static bool IsRunning(ushort mapId) => States.ContainsKey(mapId);

    /// <summary>Removes every jungle monster of the map and cancels the pending respawns.</summary>
    /// <param name="map">The arena map.</param>
    /// <returns>The number of monsters removed.</returns>
    public static async ValueTask<int> StopAsync(GameMap map)
    {
        await MobaSelupanEvent.StopAsync(map).ConfigureAwait(false);
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

    /// <summary>Gets the cancellation token of the running jungle of a map (cancelled when it stops), or <see cref="CancellationToken.None"/>.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns>The token.</returns>
    internal static CancellationToken TokenOf(ushort mapId) => States.TryGetValue(mapId, out var state) ? state.Cancel.Token : CancellationToken.None;

    /// <summary>Spawns one monster of the jungle with the stats of its kind at the current level of the match and registers it for the cleanup.</summary>
    /// <param name="map">The map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="kind">The kind.</param>
    /// <param name="slot">Where it stands and looks.</param>
    /// <param name="group">The group (spot) it belongs to.</param>
    /// <param name="home">The spot it guards.</param>
    /// <param name="onDied">What happens when it dies (after the rewards).</param>
    /// <param name="rewards">Whether killing it pays EXP and gold (not for summoned minions).</param>
    /// <returns>The monster, or <see langword="null"/> when the configuration does not have the kind.</returns>
    internal static async ValueTask<Monster?> SpawnMonsterAsync(GameMap map, IGameContext gameContext, Kind kind, MobaJungleFormation.Slot slot, MobaJungleGroup group, Point home, Func<Monster, DeathInformation, Task>? onDied, bool rewards = true)
    {
        var info = Info[kind];
        var definition = CloneDefinition(gameContext, info);
        if (definition is null)
        {
            return null;
        }

        var level = MobaMatchPhase.LeaderLevel;
        var health = info.BaseHealth * MobaWaveSpawner.CreepHealthMultiplierAt(level);
        var damageMultiplier = MobaWaveSpawner.CreepDamageMultiplierAt(level) * info.DamageFactor;
        var passive = info.DamageFactor <= 0f;
        var area = new MonsterSpawnArea
        {
            GameMap = map.Definition,
            MonsterDefinition = definition,
            SpawnTrigger = SpawnTrigger.OnceAtEventStart,
            Quantity = 1,
            X1 = slot.X,
            X2 = slot.X,
            Y1 = slot.Y,
            Y2 = slot.Y,
            Direction = slot.Direction,
            MaximumHealthOverride = (int)health,
        };

        var intelligence = new MobaJungleIntelligence(group, home, kind, passive) { GameContext = gameContext };
        var monster = new Monster(area, definition, map, NoDrops, intelligence, gameContext.PlugInManager, gameContext.PathFinderPool);
        monster.Initialize();
        SetAbsolute(monster, Stats.MaximumHealth, health);
        SetAbsolute(monster, Stats.MinimumPhysBaseDmg, 60f * damageMultiplier);
        SetAbsolute(monster, Stats.MaximumPhysBaseDmg, 85f * damageMultiplier);
        SetAbsolute(monster, Stats.DefenseBase, 20f);
        SetAbsolute(monster, Stats.AttackRatePvm, 150f);
        SetAbsolute(monster, Stats.DefenseRatePvm, 30f);
        monster.Died += (_, death) => _ = OnDiedAsync(map, gameContext, monster, kind, death, onDied, rewards);
        await map.AddAsync(monster).ConfigureAwait(false);
        monster.OnSpawn();
        if (States.TryGetValue(map.MapId, out var state))
        {
            state.Monsters.Add(monster);
        }

        intelligence.Arm();
        return monster;
    }

    private static MonsterDefinition? CloneDefinition(IGameContext gameContext, KindInfo info)
    {
        var monsters = gameContext.Configuration.Monsters;
        var designation = info.ClonedFrom ?? info.Name;
        var baseDefinition = monsters.FirstOrDefault(m => string.Equals(m.Designation, designation, StringComparison.OrdinalIgnoreCase));
        if (baseDefinition is null)
        {
            return null;
        }

        var definition = baseDefinition.Clone(gameContext.Configuration);
        if (info.ClonedFrom is not null)
        {
            definition.Number = info.Number;
            definition.Designation = info.Name;
        }

        if (info.AttackRange > 0)
        {
            definition.AttackRange = (byte)info.AttackRange;
        }

        return definition;
    }

    private static async ValueTask<int> SpawnGroupAsync(GameMap map, IGameContext gameContext, JungleState state, Spot spot, int groupIndex)
    {
        var group = new MobaJungleGroup();
        var alive = 0;
        foreach (var member in MembersOf(spot).Where(m => m.Group == groupIndex))
        {
            var slot = member.Slot;
            var monster = await SpawnMonsterAsync(
                map,
                gameContext,
                member.Kind,
                slot,
                group,
                new Point(slot.X, slot.Y),
                (dead, _) => OnMemberDiedAsync(map, gameContext, state, spot, groupIndex, group, dead)).ConfigureAwait(false);
            if (monster is not null)
            {
                alive++;
            }
        }

        state.Alive[(spot, groupIndex)] = alive;
        return alive;
    }

    private static async Task OnMemberDiedAsync(GameMap map, IGameContext gameContext, JungleState state, Spot spot, int groupIndex, MobaJungleGroup group, Monster dead)
    {
        try
        {
            state.Monsters.Remove(dead);
            if (state.Cancel.IsCancellationRequested)
            {
                return;
            }

            // A spot only comes back once ALL its monsters are dead.
            var key = (spot, groupIndex);
            if (state.Alive.TryGetValue(key, out var left) && --left <= 0)
            {
                state.Alive[key] = 0;
                await Task.Delay(TimeSpan.FromSeconds(spot.Groups[groupIndex].RespawnSeconds), state.Cancel.Token).ConfigureAwait(false);
                await SpawnGroupAsync(map, gameContext, state, spot, groupIndex).ConfigureAwait(false);
            }
            else
            {
                state.Alive[key] = left;
            }
        }
        catch
        {
            // cancelled or best effort
        }
    }

    private static async Task OnDiedAsync(GameMap map, IGameContext gameContext, Monster monster, Kind kind, DeathInformation death, Func<Monster, DeathInformation, Task>? onDied, bool rewards)
    {
        try
        {
            if (rewards)
            {
                await RewardAsync(map, gameContext, kind, death).ConfigureAwait(false);
            }

            if (onDied is not null)
            {
                await onDied(monster, death).ConfigureAwait(false);
            }
        }
        catch
        {
            // cancelled or best effort
        }
    }

    /// <summary>Pays the EXP and the gold of a kill to the champion that last-hit the monster and, for epic monsters, a share to its whole team.</summary>
    /// <param name="map">The map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="kind">The kind of monster that died.</param>
    /// <param name="death">How it died.</param>
    /// <returns>A task.</returns>
    internal static async ValueTask RewardAsync(GameMap map, IGameContext gameContext, Kind kind, DeathInformation death)
    {
        if (map.GetObject(death.KillerId) is not Player { IsMobaClone: true } killer)
        {
            return;
        }

        var info = Info[kind];
        await MobaExperience.GrantAsync(killer, info.Exp, "jungle").ConfigureAwait(false);
        await MobaGold.GrantAsync(killer, info.Gold, "jungle").ConfigureAwait(false);
        if (!info.Epic)
        {
            return;
        }

        var team = MobaTeams.GetTeam(killer);
        var mates = (await gameContext.GetPlayersAsync().ConfigureAwait(false))
            .Concat(MobaBotPlayer.All)
            .Distinct()
            .Where(p => p.IsMobaClone && p.IsAlive && !ReferenceEquals(p, killer) && team != MobaTeam.None && MobaTeams.GetTeam(p) == team)
            .ToList();
        foreach (var mate in mates)
        {
            await MobaExperience.GrantAsync(mate, (long)(info.Exp * EpicTeamShare), "jungle-team").ConfigureAwait(false);
            await MobaGold.GrantAsync(mate, (int)(info.Gold * EpicTeamShare), "jungle-team").ConfigureAwait(false);
        }

        killer.Logger.LogInformation("[MOBA-JUNGLE] {Kind} killed by {Champ}: {Exp} EXP / {Gold} gold, share for {Mates} teammate(s).", kind, killer.Name, info.Exp, info.Gold, mates.Count);
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

        public Dictionary<(Spot, int), int> Alive { get; } = new();
    }

    private sealed class NoDropGenerator : IDropGenerator
    {
        public ValueTask<(IEnumerable<Item> Items, uint? Money)> GenerateItemDropsAsync(MonsterDefinition monster, int gainedExperience, Player player)
            => ValueTask.FromResult<(IEnumerable<Item> Items, uint? Money)>((Enumerable.Empty<Item>(), null));

        public Item? GenerateItemDrop(DropItemGroup group) => null;

        public (Item? Item, uint? Money, ItemDropEffect DropEffect) GenerateItemDrop(IEnumerable<DropItemGroup> groups) => (null, null, default);
    }
}
