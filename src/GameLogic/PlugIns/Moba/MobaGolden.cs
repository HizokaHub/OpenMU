// <copyright file="MobaGolden.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The invasion of the golden monsters (2026-10-24, designed with the user): at minute 22 of the match the golden dragons fly over the map for a minute
/// (a visual only), then the whole jungle is extinguished and its places are taken by golden monsters, stronger than the jungle ones: goblins where the
/// first camps were, up to the Great Golden Dragons where Kundun and Selupan stood (one in each half). Golden goblins also patrol the lanes, stand in the
/// bases (the "city": the circle around the nexus which holds its first three turrets) and, in clusters, around the jungle spots. Young golden dragons
/// are scattered at random over the map and grow for three minutes into Great Golden Dragons. They attack everything (champions, minions, summons,
/// structures) except each other; two goblins are enough to bring a whole turret down. The invasion lasts to the end of the match: the jungle does not
/// come back. Killing a Great Golden Dragon pays its team (EXP, gold, items by damage, loot, a blessing and, for two minutes, a golden monster in each of
/// its waves).
/// </summary>
public static class MobaGolden
{
    /// <summary>The golden monsters, from the weakest to the Great Golden Dragon.</summary>
    public enum Tier
    {
        /// <summary>Goblins and the like: where the first jungle camps were, in the lanes and in the bases.</summary>
        Small = 1,

        /// <summary>Titans, Derkons, Lizard Kings... and the growing dragons while they are young.</summary>
        Medium = 2,

        /// <summary>The golden knights, golems and devils: where the mini-bosses were.</summary>
        Large = 3,

        /// <summary>The Great Golden Dragon: where Kundun and Selupan were.</summary>
        Great = 4,
    }

    /// <summary>Monster number of the golden mob which joins the waves of the team that killed a Great Golden Dragon (it is not part of the invasion).</summary>
    public const short ReinforcementNumber = 499;

    /// <summary>Strength of the reinforcement over a lane creep (life and damage).</summary>
    public const float ReinforcementPower = 4f;

    /// <summary>Seconds the golden dragons fly over the map before the invaders appear.</summary>
    public const int FlyoverSeconds = 60;

    /// <summary>Seconds a young golden dragon takes to grow into a Great Golden Dragon.</summary>
    public const int GrowSeconds = 180;

    /// <summary>Young golden dragons scattered over the map when the invasion begins.</summary>
    public const int YoungDragonCount = 6;

    /// <summary>Share of the size (client scale) of a Great Golden Dragon which a newborn has.</summary>
    public const double NewbornSize = 0.35;

    /// <summary>Tiles around a jungle spot over which the extra goblins of the spot are spread.</summary>
    public const int ClusterRadius = 5;

    /// <summary>Share of the life of a champion which a hit of Kundun or Selupan takes at a factor of 1 (their skills use 1.0 to 2.0): about four to five hits.</summary>
    public const double EpicBossHitShare = 0.18;

    /// <summary>How long the waves of the winning team carry the reinforcement.</summary>
    public static readonly TimeSpan ReinforcementTime = TimeSpan.FromMinutes(2);

    /// <summary>How long the blessing of the winning team lasts.</summary>
    public static readonly TimeSpan BlessingTime = TimeSpan.FromMinutes(3);

    /// <summary>The golden goblin, which patrols the lanes and fills the clusters.</summary>
    public static readonly Spec Goblin = new("Golden Goblin", 78, Tier.Small, "Golden Goblin");

    /// <summary>Where the dragon of the blue half stands (where Kundun stood).</summary>
    public static readonly Point BlueDragonSpot = new(59, 114);

    /// <summary>Where the dragon of the red half stands (where Selupan stood).</summary>
    public static readonly Point RedDragonSpot = new(175, 140);

    private static readonly Spec[] SmallSpecs =
    {
        Goblin,
        new("Golden Budge Dragon", 43, Tier.Small, "Golden Budge Dragon"),
        new("Golden Soldier", 54, Tier.Small, "Golden Soldier"),
        new("Golden Rabbit", 502, Tier.Small, "Golden Goblin"),
    };

    private static readonly Spec[] MediumSpecs =
    {
        new("Golden Titan", 53, Tier.Medium, "Golden Titan"),
        new("Golden Derkon", 79, Tier.Medium, "Golden Dragon"),
        new("Golden Lizard King", 80, Tier.Medium, "Golden Lizard King"),
        new("Golden Vepar", 81, Tier.Medium, "Golden Vepar"),
        new("Golden Tantallos", 82, Tier.Medium, "Golden Tantallos"),
        new("Golden Wheel", 83, Tier.Medium, "Golden Wheel"),
    };

    private static readonly Spec[] LargeSpecs =
    {
        new("Golden Dark Knight", 493, Tier.Large, "Golden Titan", "Tantallos"),
        new("Golden Devil", 494, Tier.Large, "Golden Titan", "Tantallos"),
        new("Golden Stone Golem", 495, Tier.Large, "Golden Titan", "Tantallos"),
        new("Golden Crust", 496, Tier.Large, "Golden Titan", "Tantallos"),
        new("Golden Satyros", 497, Tier.Large, "Golden Titan", "Tantallos"),
        new("Golden Twin Tail", 498, Tier.Large, "Golden Titan", "Tantallos"),
        new("Golden Napin", 500, Tier.Large, "Golden Titan", "Tantallos"),
    };

    private static readonly Spec GreatDragon = new("Great Golden Dragon", 501, Tier.Great, "Golden Titan", "Tantallos");

    private static readonly Spec ReinforcementSpec = new("Golden Iron Knight", ReinforcementNumber, Tier.Large, "Golden Titan", "Tantallos");

    private static readonly IDropGenerator NoDrops = new NoDropGenerator();

    private static readonly ConditionalWeakTable<Monster, GoldenInfo> Registry = new();

    private static readonly ConditionalWeakTable<Monster, object> BossShares = new();

    private static readonly Dictionary<GameMap, State> States = new();

    /// <summary>Gets or sets when, after the start of the match, the invasion begins.</summary>
    public static TimeSpan StartAfter { get; set; } = TimeSpan.FromMinutes(22);

    /// <summary>What the game knows about a golden monster.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="Number">The monster number (the client draws the model of that number).</param>
    /// <param name="Tier">Its tier.</param>
    /// <param name="Designations">Designations of the server configuration to clone it from (the first which exists).</param>
    public sealed record Spec(string Name, short Number, Tier Tier, params string[] Designations);

    /// <summary>Makes a jungle boss (Kundun, Selupan) hit champions for a share of their life like the golden monsters do (the fight scales with the level of the champions).</summary>
    /// <param name="boss">The boss.</param>
    /// <param name="share">The share of the life of a champion which a hit takes.</param>
    public static void MarkBoss(Monster boss, double share) => BossShares.AddOrUpdate(boss, share);

    /// <summary>Whether the object is a golden monster of the invasion (they have no team, but every team is their enemy).</summary>
    /// <param name="candidate">The object.</param>
    /// <returns><see langword="true"/> for the invaders.</returns>
    public static bool IsGolden(object? candidate) => candidate is Monster m && Registry.TryGetValue(m, out _);

    /// <summary>The tier a golden monster fights as now (a growing dragon is a medium monster until it is grown).</summary>
    /// <param name="monster">The monster.</param>
    /// <returns>The tier (small for anything which is not a golden monster).</returns>
    public static Tier TierOf(Monster monster) => Registry.TryGetValue(monster, out var info) ? info.EffectiveTier : Tier.Small;

    /// <summary>Whether the invasion of the map has begun (the jungle must not come back then).</summary>
    /// <param name="map">The map.</param>
    /// <returns><see langword="true"/> from the minute of the invasion on.</returns>
    public static bool HasStarted(GameMap map)
    {
        lock (States)
        {
            return States.ContainsKey(map);
        }
    }

    /// <summary>Whether the waves of a team carry the golden reinforcement now.</summary>
    /// <param name="map">The map.</param>
    /// <param name="team">The team.</param>
    /// <returns><see langword="true"/> while it lasts.</returns>
    public static bool IsReinforced(GameMap map, MobaTeam team)
    {
        lock (States)
        {
            return States.TryGetValue(map, out var state) && state.ReinforcedUntil.TryGetValue(team, out var until) && DateTime.UtcNow < until;
        }
    }

    /// <summary>Gets the living golden monsters of a map (for the bots and the logs).</summary>
    /// <param name="map">The map.</param>
    /// <returns>The monsters.</returns>
    public static IReadOnlyList<Monster> LiveMonsters(GameMap map)
    {
        lock (States)
        {
            return States.TryGetValue(map, out var state) ? state.Monsters.Where(m => m.IsAlive).ToArray() : Array.Empty<Monster>();
        }
    }

    /// <summary>The growing dragons of the map: their object id and their size in percent of the size of a grown one (the clients scale the model).</summary>
    /// <param name="map">The map.</param>
    /// <returns>The sizes.</returns>
    public static IReadOnlyList<(ushort Key, byte Percent)> GrowthSizes(GameMap map)
    {
        var result = new List<(ushort, byte)>();
        foreach (var monster in LiveMonsters(map))
        {
            if (Registry.TryGetValue(monster, out var info) && info.GrowStartUtc is not null)
            {
                result.Add((monster.Id, (byte)Math.Round(100 * (NewbornSize + ((1 - NewbornSize) * info.Growth)))));
            }
        }

        return result;
    }

    /// <summary>The cities of the map: the circle around each nexus which holds its first three turrets, and whether golden monsters are in it now (the clients set the scenery on fire).</summary>
    /// <param name="map">The map.</param>
    /// <returns>The cities.</returns>
    public static IReadOnlyList<(MobaTeam Team, Point Center, byte Radius, bool Burning)> Cities(GameMap map)
    {
        if (!HasStarted(map))
        {
            return Array.Empty<(MobaTeam, Point, byte, bool)>();
        }

        var live = LiveMonsters(map);
        var result = new List<(MobaTeam, Point, byte, bool)>();
        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            var center = MobaLayout.NexusOf(team);
            var radius = CityRadiusOf(team);
            result.Add((team, center, (byte)Math.Min(radius, 255), live.Any(m => m.Position.EuclideanDistanceTo(center) <= radius)));
        }

        return result;
    }

    /// <summary>The radius of the city of a team: the distance from its nexus to the farthest of the base turrets of its three lanes.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The radius in tiles.</returns>
    public static int CityRadiusOf(MobaTeam team)
    {
        var nexus = MobaLayout.NexusOf(team);
        var radius = 0.0;
        foreach (var lane in MobaLayout.Towers.Where(t => t.Team == team && t.Tier == 2).GroupBy(t => t.Lane))
        {
            radius = Math.Max(radius, lane.Min(t => t.Position.EuclideanDistanceTo(nexus)));
        }

        return (int)Math.Ceiling(radius > 0 ? radius + 2 : 30);
    }

    /// <summary>The share of the maximum life of a champion which one hit of a golden monster of the tier takes (the Great Golden Dragon kills in four hits).</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The share.</returns>
    public static double HitShareOf(Tier tier) => tier switch
    {
        Tier.Great => 0.25,
        Tier.Large => 0.15,
        Tier.Medium => 0.10,
        _ => 0.06,
    };

    /// <summary>The share of the maximum life of a turret one hit of a golden monster of the tier takes (a nexus takes 40 % of it): two goblins bring a turret down in about half a minute.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The share.</returns>
    public static double StructureShareOf(Tier tier) => tier switch
    {
        Tier.Great => 0.12,
        Tier.Large => 0.08,
        Tier.Medium => 0.06,
        _ => 0.045,
    };

    /// <summary>The share of its own life which a hit of a turret takes from a golden monster of the tier (so that the goblins outlast the turret they attack).</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The share.</returns>
    public static double TurretShareOf(Tier tier) => tier switch
    {
        Tier.Great => 0.004,
        Tier.Large => 0.008,
        Tier.Medium => 0.015,
        _ => 0.025,
    };

    /// <summary>The life of a golden monster of the tier before the level scaling shared with the jungle (Kundun has 200,000).</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The base life.</returns>
    public static float BaseHealthOf(Tier tier) => tier switch
    {
        Tier.Great => 450000f,
        Tier.Large => 36000f,
        Tier.Medium => 9000f,
        _ => 3500f,
    };

    /// <summary>Seconds between two attacks of a golden monster of the tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The delay.</returns>
    public static double AttackSecondsOf(Tier tier) => tier switch
    {
        Tier.Great => 2.2,
        Tier.Large => 1.8,
        Tier.Medium => 1.6,
        _ => 1.4,
    };

    /// <summary>Attack range in tiles of the tier.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The range.</returns>
    public static int RangeOf(Tier tier) => tier switch
    {
        Tier.Great => 6,
        Tier.Large => 3,
        _ => 2,
    };

    /// <summary>EXP (table value, scaled to the champion level) and gold of the last hit, and the kill points of the Elf's creature.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The rewards.</returns>
    public static (int Exp, int Gold, int SummonPoints) RewardOf(Tier tier) => tier switch
    {
        Tier.Great => (1500, 2200, 20),
        Tier.Large => (700, 1000, 10),
        Tier.Medium => (300, 450, 6),
        _ => (140, 220, 4),
    };

    /// <summary>Called by the damage calculation: the damage a golden monster (or a boss) deals to a defender, or a turret deals to a golden monster.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="defender">The defender.</param>
    /// <param name="factor">The damage factor of the attack (cleaves use less than 1).</param>
    /// <param name="damage">The damage when a rule applies.</param>
    /// <returns><see langword="true"/> when a rule applies and <paramref name="damage"/> was set.</returns>
    public static bool TryGetHitDamage(Monster attacker, IAttackable defender, double factor, out int damage)
    {
        damage = 0;

        // A turret against a golden monster: a small share of the life of the invader.
        if (defender is Monster goldenDefender && Registry.TryGetValue(goldenDefender, out var defenderInfo) && MobaStructures.IsStructure(attacker))
        {
            damage = (int)Math.Max(1, defender.Attributes[Stats.MaximumHealth] * TurretShareOf(defenderInfo.EffectiveTier));
            return true;
        }

        if (!Registry.TryGetValue(attacker, out var info))
        {
            if (BossShares.TryGetValue(attacker, out var share) && share is double bossShare && defender is Player)
            {
                var bossRaw = defender.Attributes[Stats.MaximumHealth] * bossShare * (0.92 + (Random.Shared.NextDouble() * 0.16));
                var bossDecrement = defender.Attributes[Stats.DamageReceiveDecrement];
                if (bossDecrement > 0)
                {
                    bossRaw *= bossDecrement;
                }

                damage = (int)Math.Clamp(bossRaw * factor, 1, int.MaxValue);
                return true;
            }

            return false;
        }

        var max = defender.Attributes[Stats.MaximumHealth];
        double raw;
        if (defender is Player)
        {
            // Champions: a share of their life, softened by their damage reduction (the items still count).
            raw = max * info.HitShare * (0.92 + (Random.Shared.NextDouble() * 0.16));
            var decrement = defender.Attributes[Stats.DamageReceiveDecrement];
            if (decrement > 0)
            {
                raw *= decrement;
            }
        }
        else if (defender is Monster monster && MobaStructures.IsStructure(monster))
        {
            raw = max * info.StructureShare * (MobaStructures.GetStructureType(monster) == MobaStructureType.Nexus ? 0.4 : 1.0);
        }
        else if (defender is Monster { SummonedBy: not null })
        {
            raw = max * 0.6;
        }
        else
        {
            // Lane minions and anything else: dead in one hit.
            raw = max;
        }

        damage = (int)Math.Clamp(raw * factor, 1, int.MaxValue);
        return true;
    }

    /// <summary>Gets the definition of a golden monster by its number (also used for the reinforcement of the waves).</summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="number">The monster number.</param>
    /// <returns>The definition, or <see langword="null"/> if the number is not a golden monster or nothing to clone it from exists.</returns>
    public static MonsterDefinition? DefinitionFor(IGameContext gameContext, short number)
    {
        var spec = AllSpecs().FirstOrDefault(s => s.Number == number);
        return spec is null ? null : CloneDefinition(gameContext, spec);
    }

    /// <summary>Checks whether the invasion must begin.</summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="map">The arena map.</param>
    /// <param name="arena">The arena of the match.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext, GameMap map, MobaArena arena)
    {
        if (arena.IsPreMatch
            || arena.MatchStartUtc == DateTime.MinValue
            || arena.Elapsed < StartAfter
            || !MobaFormats.HasJungle(arena.Format)
            || HasStarted(map))
        {
            return;
        }

        await StartAsync(map, gameContext).ConfigureAwait(false);
    }

    /// <summary>Begins the invasion: the announcement and the fly-over now, the invaders a minute later.</summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="flyoverSeconds">Seconds until the invaders appear (<see cref="FlyoverSeconds"/> in a real match).</param>
    /// <returns><see langword="false"/> if it had already begun.</returns>
    public static async ValueTask<bool> StartAsync(GameMap map, IGameContext gameContext, int flyoverSeconds = FlyoverSeconds)
    {
        State state;
        lock (States)
        {
            if (States.ContainsKey(map))
            {
                return false;
            }

            state = new State();
            States[map] = state;
        }

        var champions = await ChampionsOfAsync(map, gameContext).ConfigureAwait(false);
        foreach (var champion in champions)
        {
            await champion.ShowBlueMessageAsync("[MOBA] ¡Los dragones dorados sobrevuelan el reino! La jungla arderá y solo quedarán los invasores dorados.").ConfigureAwait(false);
            await champion.InvokeViewPlugInAsync<IMobaBannerPlugIn>(p => p.ShowBannerAsync("¡LOS DRAGONES DORADOS SOBREVUELAN EL REINO!", 6000)).ConfigureAwait(false);
            await champion.InvokeViewPlugInAsync<IMobaGoldenFlyoverPlugIn>(p => p.ShowFlyoverAsync(flyoverSeconds * 1000)).ConfigureAwait(false);
        }

        _ = RunAsync(map, gameContext, state, flyoverSeconds);
        return true;
    }

    /// <summary>Ends the invasion of the map: every golden monster is removed and the state forgotten (match end, reset).</summary>
    /// <param name="map">The map.</param>
    /// <returns>A task.</returns>
    public static async ValueTask StopAsync(GameMap map)
    {
        State? state;
        lock (States)
        {
            if (!States.Remove(map, out state))
            {
                return;
            }
        }

        await state.Cancel.CancelAsync().ConfigureAwait(false);
        foreach (var monster in state.Monsters.ToArray())
        {
            try
            {
                await map.RemoveAsync(monster).ConfigureAwait(false);
                monster.Dispose();
            }
            catch
            {
                // already gone
            }
        }

        state.Monsters.Clear();
    }

    /// <summary>Gives a champion the blessing of the dragon: +12 % damage and 8 % less damage taken.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="duration">How long it lasts.</param>
    /// <param name="cancel">Cancels it early (the match ended).</param>
    public static void Bless(Player champion, TimeSpan duration, CancellationToken cancel)
    {
        if (champion.Attributes is not { } attributes)
        {
            return;
        }

        var damage = new SimpleElement(1.12f, AggregateType.Multiplicate);
        var shield = new SimpleElement(0.92f, AggregateType.Multiplicate);
        attributes.AddElement(damage, Stats.AttackDamageIncrease);
        attributes.AddElement(shield, Stats.DamageReceiveDecrement);
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(duration, cancel).ConfigureAwait(false);
            }
            catch
            {
                // the match ended
            }

            attributes.RemoveElement(damage, Stats.AttackDamageIncrease);
            attributes.RemoveElement(shield, Stats.DamageReceiveDecrement);
        });
    }

    /// <summary>The token which is cancelled when the invasion of the map ends (for the effects which last).</summary>
    /// <param name="map">The map.</param>
    /// <returns>The token.</returns>
    public static CancellationToken TokenOf(GameMap map)
    {
        lock (States)
        {
            return States.TryGetValue(map, out var state) ? state.Cancel.Token : CancellationToken.None;
        }
    }

    private static IEnumerable<Spec> AllSpecs()
        => SmallSpecs.Concat(MediumSpecs).Concat(LargeSpecs).Append(GreatDragon).Append(ReinforcementSpec);

    private static async Task<List<Player>> ChampionsOfAsync(GameMap map, IGameContext gameContext)
        => (await gameContext.GetPlayersAsync().ConfigureAwait(false)).Where(p => p.IsMobaClone && ReferenceEquals(p.CurrentMap, map)).ToList();

    private static MonsterDefinition? CloneDefinition(IGameContext gameContext, Spec spec)
    {
        var monsters = gameContext.Configuration.Monsters;
        MonsterDefinition? baseDefinition = null;
        foreach (var designation in spec.Designations)
        {
            baseDefinition = monsters.FirstOrDefault(m => string.Equals(m.Designation, designation, StringComparison.OrdinalIgnoreCase));
            if (baseDefinition is not null)
            {
                break;
            }
        }

        if (baseDefinition is null)
        {
            return null;
        }

        var definition = baseDefinition.Clone(gameContext.Configuration);
        definition.Number = spec.Number;
        definition.Designation = spec.Name;
        definition.AttackRange = (byte)RangeOf(spec.Tier);
        definition.AttackDelay = TimeSpan.FromSeconds(AttackSecondsOf(spec.Tier));
        return definition;
    }

    private static async Task RunAsync(GameMap map, IGameContext gameContext, State state, int flyoverSeconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(flyoverSeconds), state.Cancel.Token).ConfigureAwait(false);

            // The jungle burns: it is gone for the rest of the match.
            var removed = await MobaJungle.ExtinguishAsync(map).ConfigureAwait(false);
            var spawned = await SpawnInvadersAsync(map, gameContext, state).ConfigureAwait(false);
            foreach (var champion in await ChampionsOfAsync(map, gameContext).ConfigureAwait(false))
            {
                await champion.ShowBlueMessageAsync($"[MOBA] Llegan los invasores dorados ({spawned}). Hasta que caigan, la jungla no da nada.").ConfigureAwait(false);
                await champion.InvokeViewPlugInAsync<IMobaBannerPlugIn>(p => p.ShowBannerAsync("¡LOS INVASORES DORADOS HAN LLEGADO!", 5000)).ConfigureAwait(false);
            }

            Log(gameContext, $"[MOBA-GOLDEN] invasion: {removed} jungle monsters burned, {spawned} golden monsters spawned.");

            // They grow with the match like the jungle bosses do, and the young dragons grow up.
            while (!state.Cancel.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), state.Cancel.Token).ConfigureAwait(false);
                var level = MobaJungle.BossLevel(MobaArena.Of(map));
                foreach (var monster in state.Monsters.ToArray().Where(m => m.IsAlive))
                {
                    if (Registry.TryGetValue(monster, out var info))
                    {
                        if (info.Refresh())
                        {
                            monster.Definition.AttackRange = (byte)RangeOf(info.EffectiveTier);
                        }

                        ApplyStats(monster, info, level, healToFull: false);
                    }
                }
            }
        }
        catch
        {
            // the match ended
        }
    }

    private static void Log(IGameContext gameContext, string message)
        => gameContext.LoggerFactory.CreateLogger(typeof(MobaGolden)).LogInformation("{Message}", message);

    private static async ValueTask<int> SpawnInvadersAsync(GameMap map, IGameContext gameContext, State state)
    {
        var count = 0;
        var arena = MobaArena.Of(map);
        var level = MobaJungle.BossLevel(arena);

        async ValueTask Add(Spec spec, Point position, Direction direction, bool guard, IReadOnlyList<Point>? patrol, bool young = false)
        {
            if (await SpawnAsync(map, gameContext, state, spec, position, direction, level, guard, patrol, young).ConfigureAwait(false))
            {
                count++;
            }
        }

        // 1. Where the jungle camps were: the golden monster of the same rank, and a cluster of goblins spread around the spot.
        var spotIndex = 0;
        foreach (var spot in MobaJungle.Spots)
        {
            spotIndex++;
            var memberIndex = 0;
            foreach (var member in MobaJungle.MembersOf(spot))
            {
                if (member.Kind == MobaJungle.Kind.Kundun)
                {
                    continue; // the Great Golden Dragons are placed apart
                }

                var spec = PickSpec(TierOf(member.Kind), spotIndex + memberIndex++);
                var slot = member.Slot;
                await Add(spec, new Point(slot.X, slot.Y), slot.Direction, true, null).ConfigureAwait(false);
            }

            for (var i = 0; i < 3; i++)
            {
                var angle = (Math.PI * 2 * i / 3) + (spotIndex * 0.7);
                var radius = ClusterRadius - 1 + (i % 2);
                await Add(Goblin, MobaLayout.NearestWalkable(spot.X + (Math.Cos(angle) * radius), spot.Y + (Math.Sin(angle) * radius)), Direction.South, true, null).ConfigureAwait(false);
            }
        }

        // 2. One Great Golden Dragon in each half.
        foreach (var spot in new[] { BlueDragonSpot, RedDragonSpot })
        {
            await Add(GreatDragon, spot, Direction.South, true, null).ConfigureAwait(false);
        }

        // 3. Young dragons at random places of the map, far from the bases; they grow for three minutes.
        var rng = new Random();
        var placed = new List<Point>();
        for (var attempt = 0; attempt < 400 && placed.Count < YoungDragonCount; attempt++)
        {
            var candidate = MobaLayout.NearestWalkable(rng.Next(24, 232), rng.Next(40, 216));
            if (candidate.EuclideanDistanceTo(MobaLayout.NexusOf(MobaTeam.Blue)) < 50
                || candidate.EuclideanDistanceTo(MobaLayout.NexusOf(MobaTeam.Red)) < 50
                || placed.Any(p => p.EuclideanDistanceTo(candidate) < 25))
            {
                continue;
            }

            placed.Add(candidate);
            await Add(GreatDragon, candidate, Direction.South, true, null, young: true).ConfigureAwait(false);
        }

        // 4. Goblins which patrol the lanes towards the enemy base, in groups spread over a radius.
        var middleOnly = MobaFormats.MiddleLaneOnly(arena.Format);
        for (var lane = 0; lane < MobaLayout.LaneCount; lane++)
        {
            if (middleOnly && lane != MobaLayout.MidLane)
            {
                continue;
            }

            var towardsRed = MobaLayout.WaypointsFor(MobaTeam.Blue, lane);
            var towardsBlue = MobaLayout.WaypointsFor(MobaTeam.Red, lane);
            var length = MobaLayout.LengthOf(towardsRed);
            for (var group = 0; group < 5; group++)
            {
                var fraction = 0.12 + (0.19 * group);
                var forRed = group % 2 == 0;
                var route = forRed ? towardsRed : towardsBlue;
                var arc = length * fraction;
                var (cx, cy, _, _) = MobaLayout.PointAtArc(towardsRed, arc);
                var ahead = route.Where(w => forRed ? MobaLayout.ArcOf(towardsRed, w.X, w.Y) > arc : MobaLayout.ArcOf(towardsRed, w.X, w.Y) < arc).ToList();
                if (ahead.Count == 0)
                {
                    ahead.Add(route[^1]);
                }

                for (var i = 0; i < 4; i++)
                {
                    var angle = (Math.PI / 2 * i) + group;
                    var start = MobaLayout.NearestWalkable(cx + (Math.Cos(angle) * 2.5), cy + (Math.Sin(angle) * 2.5));
                    await Add(Goblin, start, Direction.South, false, ahead).ConfigureAwait(false);
                }
            }
        }

        // 5. The cities: golden monsters spread at random over the circle around each nexus which holds its first three turrets.
        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            var nexus = MobaLayout.NexusOf(team);
            var cityRadius = CityRadiusOf(team);
            for (var i = 0; i < 14; i++)
            {
                var angle = rng.NextDouble() * Math.PI * 2;
                var distance = 8 + (rng.NextDouble() * Math.Max(1, cityRadius - 10));
                var start = MobaLayout.NearestWalkable(nexus.X + (Math.Cos(angle) * distance), nexus.Y + (Math.Sin(angle) * distance));
                await Add(i % 5 == 0 ? PickSpec(Tier.Medium, i + (int)team) : SmallSpecs[i % SmallSpecs.Length], start, Direction.South, true, null).ConfigureAwait(false);
            }
        }

        return count;
    }

    private static Tier TierOf(MobaJungle.Kind kind) => kind switch
    {
        MobaJungle.Kind.Mutant or MobaJungle.Kind.BloodyWolf => Tier.Small,
        MobaJungle.Kind.Tantallos or MobaJungle.Kind.BloodyOrc => Tier.Medium,
        _ => Tier.Large,
    };

    private static Spec PickSpec(Tier tier, int index)
    {
        var specs = tier switch
        {
            Tier.Small => SmallSpecs,
            Tier.Medium => MediumSpecs,
            _ => LargeSpecs,
        };
        return specs[Math.Abs(index) % specs.Length];
    }

    private static async ValueTask<bool> SpawnAsync(GameMap map, IGameContext gameContext, State state, Spec spec, Point position, Direction direction, int level, bool guard, IReadOnlyList<Point>? patrol, bool young)
    {
        var definition = CloneDefinition(gameContext, spec);
        if (definition is null)
        {
            return false;
        }

        var info = new GoldenInfo(spec.Tier, spec, young);
        definition.AttackRange = (byte)RangeOf(info.EffectiveTier);
        var health = info.BaseHealth * MobaWaveSpawner.CreepHealthMultiplierAt(level);
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
            Direction = direction,
            MaximumHealthOverride = (int)health,
        };

        var intelligence = new MobaGoldenIntelligence(position, guard, patrol);
        var monster = new Monster(area, definition, map, NoDrops, intelligence, gameContext.PlugInManager, gameContext.PathFinderPool);
        Registry.AddOrUpdate(monster, info);
        var damage = new System.Collections.Concurrent.ConcurrentDictionary<Player, double>();
        monster.DamageReceived += (attacker, amount) =>
        {
            if (MobaSummons.CreditedChampion(attacker) is { IsMobaClone: true } champion && amount > 0)
            {
                damage.AddOrUpdate(champion, amount, (_, current) => current + amount);
            }
        };

        monster.Initialize();
        ApplyStats(monster, info, level, healToFull: true);
        monster.Died += (_, death) => _ = OnDiedAsync(map, gameContext, state, monster, info, death, damage);
        await map.AddAsync(monster).ConfigureAwait(false);
        monster.OnSpawn();
        state.Monsters.Add(monster);
        intelligence.Arm();
        return true;
    }

    private static void ApplyStats(Monster monster, GoldenInfo info, int level, bool healToFull)
    {
        var attributes = monster.Attributes;
        var fraction = attributes[Stats.MaximumHealth] > 0 ? attributes[Stats.CurrentHealth] / attributes[Stats.MaximumHealth] : 1f;
        var health = info.BaseHealth * MobaWaveSpawner.CreepHealthMultiplierAt(level);
        var damageMultiplier = MobaWaveSpawner.CreepDamageMultiplierAt(level);
        SetAbsolute(monster, Stats.MaximumHealth, health);
        attributes[Stats.CurrentHealth] = healToFull ? health : health * fraction;
        SetAbsolute(monster, Stats.MinimumPhysBaseDmg, 60f * damageMultiplier);
        SetAbsolute(monster, Stats.MaximumPhysBaseDmg, 85f * damageMultiplier);
        SetAbsolute(monster, Stats.DefenseBase, 20f);
        SetAbsolute(monster, Stats.AttackRatePvm, 600f);
        SetAbsolute(monster, Stats.DefenseRatePvm, 30f);
    }

    private static void SetAbsolute(Monster monster, AttributeDefinition stat, float value)
    {
        var current = monster.Attributes[stat];
        monster.Attributes.AddElement(new SimpleElement(value - current, AggregateType.AddRaw), stat);
    }

    private static async Task OnDiedAsync(GameMap map, IGameContext gameContext, State state, Monster monster, GoldenInfo info, DeathInformation death, IReadOnlyDictionary<Player, double> damage)
    {
        try
        {
            state.Monsters.Remove(monster);
            if (state.Cancel.IsCancellationRequested)
            {
                return;
            }

            var tier = info.EffectiveTier;
            var (exp, gold, points) = RewardOf(tier);
            await MobaSummons.NoteKillAsync(map, death, points).ConfigureAwait(false);
            if (MobaSummons.CreditedChampion(map.GetObject(death.KillerId)) is not { IsMobaClone: true } killer)
            {
                return;
            }

            killer.MobaState.GoldenKills++;
            await MobaExperience.GrantAsync(killer, MobaJungle.ScaledExp(exp, killer.MobaLevel), "golden").ConfigureAwait(false);
            await MobaGold.GrantAsync(killer, gold, "golden").ConfigureAwait(false);
            var team = MobaTeams.GetTeam(killer);
            if (tier >= Tier.Large && team != MobaTeam.None)
            {
                var mates = (await gameContext.GetPlayersAsync().ConfigureAwait(false))
                    .Concat(MobaBotPlayer.All)
                    .Distinct()
                    .Where(p => p.IsMobaClone && p.IsAlive && !ReferenceEquals(p, killer) && ReferenceEquals(p.CurrentMap, map) && MobaTeams.GetTeam(p) == team);
                foreach (var mate in mates)
                {
                    await MobaExperience.GrantAsync(mate, (long)(MobaJungle.ScaledExp(exp, mate.MobaLevel) * MobaJungle.EpicTeamShare), "golden-team").ConfigureAwait(false);
                    await MobaGold.GrantAsync(mate, (int)(gold * MobaJungle.EpicTeamShare), "golden-team").ConfigureAwait(false);
                }
            }

            await MobaGoldenLoot.DropForKillAsync(map, tier, monster, killer, damage).ConfigureAwait(false);
            switch (tier)
            {
                case Tier.Great:
                    await MobaJungleDrops.DropSplitItemsAsync(map, (11, 12), monster, death, damage, "golden-dragon").ConfigureAwait(false);
                    await RewardDragonKillAsync(map, gameContext, state, killer, team).ConfigureAwait(false);
                    break;
                case Tier.Large:
                    await MobaJungleDrops.DropSplitItemsAsync(map, (10, 11), monster, death, damage, "golden-large").ConfigureAwait(false);
                    break;
                case Tier.Medium:
                    if (Random.Shared.NextDouble() < 0.30)
                    {
                        await MobaJungleDrops.DropSingleItemAsync(map, killer, monster, Random.Shared.Next(8, 11)).ConfigureAwait(false);
                    }

                    break;
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>The team of the champion which killed a Great Golden Dragon gets the blessing and the reinforcement of its waves.</summary>
    private static async ValueTask RewardDragonKillAsync(GameMap map, IGameContext gameContext, State state, Player killer, MobaTeam team)
    {
        if (team == MobaTeam.None)
        {
            return;
        }

        lock (States)
        {
            state.ReinforcedUntil[team] = DateTime.UtcNow + ReinforcementTime;
        }

        var members = (await gameContext.GetPlayersAsync().ConfigureAwait(false))
            .Concat(MobaBotPlayer.All)
            .Distinct()
            .Where(p => p.IsMobaClone && ReferenceEquals(p.CurrentMap, map) && MobaTeams.GetTeam(p) == team)
            .ToList();
        foreach (var member in members)
        {
            Bless(member, BlessingTime, state.Cancel.Token);
            await member.ShowBlueMessageAsync($"[MOBA] ¡{killer.Name} derrotó a un Gran Dragón Dorado! Bendición dorada durante {BlessingTime.TotalMinutes:0} min y un monstruo dorado en cada oleada durante {ReinforcementTime.TotalMinutes:0} min.").ConfigureAwait(false);
            await member.InvokeViewPlugInAsync<IMobaBannerPlugIn>(p => p.ShowBannerAsync("¡BENDICIÓN DEL DRAGÓN DORADO!", 5000)).ConfigureAwait(false);
        }

        Log(gameContext, $"[MOBA-GOLDEN] {killer.Name} killed a Great Golden Dragon: blessing for {members.Count} champion(s) of {team}.");
    }

    /// <summary>The state of one golden monster: its tier and, for the young dragons, how grown it is.</summary>
    private sealed class GoldenInfo
    {
        private Tier _effective;

        public GoldenInfo(Tier tier, Spec spec, bool young)
        {
            this.Tier = tier;
            this.Spec = spec;
            this.GrowStartUtc = young ? DateTime.UtcNow : null;
            this._effective = young ? Tier.Medium : tier;
        }

        public Tier Tier { get; }

        public Spec Spec { get; }

        public DateTime? GrowStartUtc { get; }

        /// <summary>Gets how grown a young dragon is, 0 (newborn) to 1 (grown); 1 for everybody else.</summary>
        public double Growth => this.GrowStartUtc is { } start ? Math.Clamp((DateTime.UtcNow - start).TotalSeconds / GrowSeconds, 0, 1) : 1;

        public Tier EffectiveTier => this._effective;

        public float BaseHealth => this.GrowStartUtc is null
            ? BaseHealthOf(this.Tier)
            : (float)(BaseHealthOf(Tier.Medium) + ((BaseHealthOf(Tier.Great) - BaseHealthOf(Tier.Medium)) * this.Growth));

        public double HitShare => this.GrowStartUtc is null
            ? HitShareOf(this.Tier)
            : HitShareOf(Tier.Medium) + ((HitShareOf(Tier.Great) - HitShareOf(Tier.Medium)) * this.Growth);

        public double StructureShare => this.GrowStartUtc is null
            ? StructureShareOf(this.Tier)
            : StructureShareOf(Tier.Medium) + ((StructureShareOf(Tier.Great) - StructureShareOf(Tier.Medium)) * this.Growth);

        /// <summary>Updates the effective tier of a growing dragon.</summary>
        /// <returns><see langword="true"/> if the tier changed (it is grown now).</returns>
        public bool Refresh()
        {
            var tier = this.Growth >= 1 ? this.Tier : Tier.Medium;
            if (tier == this._effective)
            {
                return false;
            }

            this._effective = tier;
            return true;
        }
    }

    private sealed class State
    {
        public CancellationTokenSource Cancel { get; } = new();

        public List<Monster> Monsters { get; } = new();

        public Dictionary<MobaTeam, DateTime> ReinforcedUntil { get; } = new();
    }

    private sealed class NoDropGenerator : IDropGenerator
    {
        public ValueTask<(IEnumerable<Item> Items, uint? Money)> GenerateItemDropsAsync(MonsterDefinition monster, int gainedExperience, Player player)
            => ValueTask.FromResult<(IEnumerable<Item> Items, uint? Money)>((Enumerable.Empty<Item>(), null));

        public Item? GenerateItemDrop(DropItemGroup group) => null;

        public (Item? Item, uint? Money, ItemDropEffect DropEffect) GenerateItemDrop(IEnumerable<DropItemGroup> groups) => (null, null, default);
    }
}
