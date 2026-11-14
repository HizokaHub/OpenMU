// <copyright file="MobaDuel.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.Pathfinding;

/// <summary>What a duel run should measure.</summary>
/// <param name="Pairs">The pairs of classes (a mirror is a pair of the same class).</param>
/// <param name="Duels">The duels of every pair.</param>
/// <param name="Plain">Whether to run the pass without items.</param>
/// <param name="FullT3">Whether to run the pass with the full tier 3 build.</param>
/// <param name="Level">The champion level of both duelists.</param>
/// <param name="Parallel">How many duels run at once (each in its own corner of the arena).</param>
public sealed record MobaDuelRequest(IReadOnlyList<(byte A, byte B)> Pairs, int Duels, bool Plain, bool FullT3, int Level, int Parallel);

/// <summary>
/// 1v1 duels between the classes (2026-11-13), without a client: two bots with the real bot AI (rotation of skills, combo, buffs, walking in and
/// kiting; see <c>MobaBotPlayer.Duel.cs</c>) fight at level 30 with every skill at rank 5, in a quiet corner of the arena. Every cross of the classes
/// is repeated and the wins, the length and the life left are written to <c>duelos_*.json</c> (see <c>tools/informe_duelos.py</c>). The goal of the
/// balance is 4-6 wins of 10 in every cross. Started with <c>/mobaduel</c>.
/// </summary>
public static class MobaDuel
{
    /// <summary>The seven classes of the review (the third job of every family).</summary>
    public static readonly byte[] MainClasses = { 3, 6, 11, 12, 16, 20, 24 };

    private const int RectWidth = 14;
    private const int RectHeight = 12;
    private const int SeparationTiles = 6;

    /// <summary>
    /// How far from the center of a corner no monster may stand (a duelist that kites backs off several tiles: in the first full matrix a Stone Golem
    /// of a jungle camp 19 tiles away killed a Grand Master).
    /// </summary>
    private const int MonsterFreeRadius = 24;
    private const double TimeoutSeconds = 120;
    private const int CarrierLife = 20;

    private static readonly ConcurrentDictionary<MobaBotPlayer, ConcurrentDictionary<int, long>> Damage = new();

    /// <summary>The damage a duelist took from something that is not its opponent (a jungle monster, a minion, a tower), per duelist.</summary>
    private static readonly ConcurrentDictionary<MobaBotPlayer, long> Foreign = new();
    private static int _running;
    private static int _cancel;
    private static int _nameCounter;

    /// <summary>Gets a value indicating whether a duel run is going on.</summary>
    public static bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <summary>Asks the run to stop after the duels in progress.</summary>
    public static void Cancel() => Interlocked.Exchange(ref _cancel, 1);

    /// <summary>All the pairs of the given classes (every class against every other, and every class against itself).</summary>
    /// <param name="classes">The classes.</param>
    /// <returns>The pairs.</returns>
    public static IReadOnlyList<(byte A, byte B)> AllPairs(IReadOnlyList<byte> classes)
    {
        var pairs = new List<(byte, byte)>();
        for (var i = 0; i < classes.Count; i++)
        {
            for (var j = i; j < classes.Count; j++)
            {
                pairs.Add((classes[i], classes[j]));
            }
        }

        return pairs;
    }

    /// <summary>Runs the duels and writes one JSON report per pass.</summary>
    /// <param name="gm">The game master in the arena (gives the game context).</param>
    /// <param name="request">What to measure.</param>
    /// <returns>The paths of the reports.</returns>
    public static async ValueTask<IReadOnlyList<string>> RunAsync(Player gm, MobaDuelRequest request)
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
        {
            throw new InvalidOperationException("Ya hay una tanda de duelos corriendo.");
        }

        Interlocked.Exchange(ref _cancel, 0);
        var map = gm.CurrentMap;
        var logger = gm.Logger;
        var reports = new List<string>();
        try
        {
            if (!gm.IsMobaClone || map is null)
            {
                throw new InvalidOperationException("Solo dentro de una partida MOBA.");
            }

            if (MobaSkillProbe.IsRunning)
            {
                throw new InvalidOperationException("Hay una prueba de skills corriendo.");
            }

            await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);
            MobaTelemetry.ProbeObserver = OnHit;

            var slots = FindSlots(map, request.Parallel);
            logger.LogInformation("[MOBA-DUEL] {Slots} rincones libres para {Wanted} duelos a la vez.", slots.Count, request.Parallel);
            if (slots.Count == 0)
            {
                throw new InvalidOperationException("No encontré ningún rincón libre de la arena para los duelos.");
            }

            var passes = new List<bool>();
            if (request.Plain)
            {
                passes.Add(false);
            }

            if (request.FullT3)
            {
                passes.Add(true);
            }

            foreach (var t3 in passes)
            {
                var path = await RunPassAsync(gm, map, request, slots, t3).ConfigureAwait(false);
                reports.Add(path);
                if (Volatile.Read(ref _cancel) == 1)
                {
                    break;
                }
            }

            return reports;
        }
        finally
        {
            MobaTelemetry.ProbeObserver = null;
            Damage.Clear();
            if (map is not null)
            {
                await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);
            }

            Interlocked.Exchange(ref _running, 0);
        }
    }

    private sealed record Job(int Index, (byte A, byte B) Pair, int Number);

    private sealed record DuelRow(
        string Winner,
        double Seconds,
        double ALeft,
        double BLeft,
        Dictionary<int, long> ADamage,
        Dictionary<int, long> BDamage,
        int APieces,
        int BPieces,
        long AForeign = 0,
        long BForeign = 0);

    private static void OnHit(MobaProbeHit hit)
    {
        if (hit.Attacker is MobaBotPlayer { DuelMode: true } bot)
        {
            var perSkill = Damage.GetOrAdd(bot, _ => new ConcurrentDictionary<int, long>());
            perSkill.AddOrUpdate(hit.Skill?.Number ?? 0, hit.Hp + hit.Sd, (_, old) => old + hit.Hp + hit.Sd);
        }
        else if (hit.Victim is MobaBotPlayer { DuelMode: true } victim)
        {
            // Damage from anything that is not the opponent (a jungle monster, a minion, a tower): the duel is contaminated and the report discards it.
            Foreign.AddOrUpdate(victim, hit.Hp + hit.Sd, (_, old) => old + hit.Hp + hit.Sd);
        }
    }

    private static async ValueTask<string> RunPassAsync(Player gm, GameMap map, MobaDuelRequest request, IReadOnlyList<Point> slots, bool t3)
    {
        var passName = t3 ? "t3" : "sin_items";
        var logger = gm.Logger;
        var started = DateTime.UtcNow;
        var queue = new ConcurrentQueue<Job>();
        var results = new ConcurrentDictionary<(byte, byte), ConcurrentBag<(int Number, DuelRow Row)>>();
        var total = 0;
        foreach (var pair in request.Pairs)
        {
            for (var n = 0; n < request.Duels; n++)
            {
                queue.Enqueue(new Job(total++, pair, n));
            }
        }

        logger.LogInformation("[MOBA-DUEL] pasada {Pass}: {Total} duelos, {Slots} a la vez.", passName, total, Math.Min(slots.Count, request.Parallel));
        var done = 0;

        // The first duel runs alone: the lazy configuration of the shop and the clones is not thread safe.
        if (queue.TryDequeue(out var first))
        {
            await RunJobAsync(gm, map, slots[0], first, t3, request.Level, results).ConfigureAwait(false);
            done++;
        }

        var workers = Enumerable.Range(0, Math.Min(slots.Count, request.Parallel)).Select(i => Task.Run(async () =>
        {
            while (Volatile.Read(ref _cancel) == 0 && queue.TryDequeue(out var job))
            {
                try
                {
                    await RunJobAsync(gm, map, slots[i], job, t3, request.Level, results).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "[MOBA-DUEL] duelo {Index} falló.", job.Index);
                }

                var n = Interlocked.Increment(ref done);
                if (n % 10 == 0)
                {
                    logger.LogInformation("[MOBA-DUEL] pasada {Pass}: {Done}/{Total} duelos ({Seconds:F0} s).", passName, n, total, (DateTime.UtcNow - started).TotalSeconds);
                }
            }
        })).ToList();
        await Task.WhenAll(workers).ConfigureAwait(false);

        var names = gm.GameContext.Configuration.CharacterClasses.ToDictionary(c => c.Number, c => c.Name.ToString());
        var pairs = request.Pairs.Select(pair =>
        {
            var rows = results.TryGetValue(pair, out var bag) ? bag.OrderBy(r => r.Number).Select(r => r.Row).ToList() : new List<DuelRow>();
            return new Dictionary<string, object?>
            {
                ["a"] = pair.A,
                ["b"] = pair.B,
                ["aName"] = names.GetValueOrDefault(pair.A),
                ["bName"] = names.GetValueOrDefault(pair.B),
                ["duels"] = rows.Select(r => new Dictionary<string, object?>
                {
                    ["winner"] = r.Winner,
                    ["seconds"] = Math.Round(r.Seconds, 1),
                    ["aLeft"] = Math.Round(r.ALeft, 3),
                    ["bLeft"] = Math.Round(r.BLeft, 3),
                    ["aDamage"] = r.ADamage,
                    ["bDamage"] = r.BDamage,
                    ["aPieces"] = r.APieces,
                    ["bPieces"] = r.BPieces,
                    ["aForeign"] = r.AForeign,
                    ["bForeign"] = r.BForeign,
                }).ToList(),
            };
        }).ToList();

        var report = new Dictionary<string, object?>
        {
            ["started"] = started.ToString("o"),
            ["seconds"] = Math.Round((DateTime.UtcNow - started).TotalSeconds),
            ["pass"] = passName,
            ["level"] = request.Level,
            ["rank"] = MobaSkills.SkillLevelCap,
            ["duelsPerPair"] = request.Duels,
            ["timeoutSeconds"] = TimeoutSeconds,
            ["cancelled"] = Volatile.Read(ref _cancel) == 1,
            ["pairs"] = pairs,
        };
        var path = Path.Combine(AppContext.BaseDirectory, $"duelos_{passName}_{DateTime.Now:yyyyMMdd_HHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        logger.LogInformation("[MOBA-DUEL] report={Path}", path);
        return path;
    }

    private static async ValueTask RunJobAsync(Player gm, GameMap map, Point slot, Job job, bool t3, int level, ConcurrentDictionary<(byte, byte), ConcurrentBag<(int, DuelRow)>> results)
    {
        // Odd duels swap the sides (blue / north against red / south), so the position is not an advantage of one class.
        var swap = job.Number % 2 == 1;
        var (classA, classB) = job.Pair;
        var row = await DuelAsync(gm, swap ? classB : classA, swap ? classA : classB, slot, t3, level).ConfigureAwait(false);
        if (swap)
        {
            row = row with
            {
                Winner = row.Winner switch { "a" => "b", "b" => "a", var w => w },
                ALeft = row.BLeft,
                BLeft = row.ALeft,
                ADamage = row.BDamage,
                BDamage = row.ADamage,
                APieces = row.BPieces,
                BPieces = row.APieces,
                AForeign = row.BForeign,
                BForeign = row.AForeign,
            };
        }

        results.GetOrAdd(job.Pair, _ => new ConcurrentBag<(int, DuelRow)>()).Add((job.Number, row));
    }

    // One carrier per run of duels would grow its persistence context with every clone: it is replaced every few duels per slot.
    private static readonly ConcurrentDictionary<Point, (MobaBotPlayer Carrier, int Uses)> Carriers = new();

    private static MobaBotPlayer CarrierFor(Player gm, Point slot)
    {
        lock (Carriers)
        {
            if (!Carriers.TryGetValue(slot, out var entry) || entry.Uses >= CarrierLife)
            {
                // the old one is not disposed here: its bots are stopped, the context goes with the GC (disposing it while a stopping bot touches it is worse)
                entry = (new MobaBotPlayer(gm.GameContext, MobaTeam.Blue, isDummy: true), 0);
            }

            Carriers[slot] = (entry.Carrier, entry.Uses + 1);
            return entry.Carrier;
        }
    }

    private static async ValueTask<DuelRow> DuelAsync(Player gm, byte classA, byte classB, Point slot, bool t3, int level)
    {
        var carrier = CarrierFor(gm, slot);
        var id = Interlocked.Increment(ref _nameCounter) % 100000;
        var north = new Point(slot.X, (byte)(slot.Y - (SeparationTiles / 2)));
        var south = new Point(slot.X, (byte)(slot.Y + (SeparationTiles / 2)));
        MobaBotPlayer? a = null;
        MobaBotPlayer? b = null;
        try
        {
            a = await MobaSkillProbe.SpawnAsync(carrier, classA, $"dA{id}", MobaTeam.Blue, north, 1, dummy: false, duel: true).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"no se pudo crear la clase {classA}");
            b = await MobaSkillProbe.SpawnAsync(carrier, classB, $"dB{id}", MobaTeam.Red, south, 1, dummy: false, duel: true).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"no se pudo crear la clase {classB}");

            await a.PrepareDuelAsync(level, MobaSkills.SkillLevelCap).ConfigureAwait(false);
            await b.PrepareDuelAsync(level, MobaSkills.SkillLevelCap).ConfigureAwait(false);
            var piecesA = 0;
            var piecesB = 0;
            if (t3)
            {
                piecesA = await a.EquipFullT3Async().ConfigureAwait(false);
                piecesB = await b.EquipFullT3Async().ConfigureAwait(false);
            }

            Refill(a);
            Refill(b);
            Damage[a] = new ConcurrentDictionary<int, long>();
            Damage[b] = new ConcurrentDictionary<int, long>();
            Foreign.TryRemove(a, out _);
            Foreign.TryRemove(b, out _);

            var clock = Stopwatch.StartNew();
            a.DuelOpponent = b;
            b.DuelOpponent = a;
            while (a.IsAlive && b.IsAlive && clock.Elapsed.TotalSeconds < TimeoutSeconds && Volatile.Read(ref _cancel) == 0)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }

            a.DuelOpponent = null;
            b.DuelOpponent = null;
            var seconds = clock.Elapsed.TotalSeconds;
            var winner = a.IsAlive == b.IsAlive ? "draw" : a.IsAlive ? "a" : "b";
            return new DuelRow(
                winner,
                seconds,
                a.IsAlive ? Left(a) : 0,
                b.IsAlive ? Left(b) : 0,
                Damage.TryGetValue(a, out var da) ? da.ToDictionary(kv => kv.Key, kv => kv.Value) : new Dictionary<int, long>(),
                Damage.TryGetValue(b, out var db) ? db.ToDictionary(kv => kv.Key, kv => kv.Value) : new Dictionary<int, long>(),
                piecesA,
                piecesB,
                Foreign.TryGetValue(a, out var fa) ? fa : 0,
                Foreign.TryGetValue(b, out var fb) ? fb : 0);
        }
        finally
        {
            foreach (var bot in new[] { a, b })
            {
                if (bot is null)
                {
                    continue;
                }

                bot.DuelOpponent = null;
                Damage.TryRemove(bot, out _);
                Foreign.TryRemove(bot, out _);
                try
                {
                    await bot.StopAsync().ConfigureAwait(false);
                }
                catch
                {
                    // best effort
                }
            }
        }
    }

    private static double Left(Player p)
    {
        var a = p.Attributes!;
        return Math.Clamp((a[Stats.CurrentHealth] + a[Stats.CurrentShield]) / Math.Max(1f, a[Stats.MaximumHealth] + a[Stats.MaximumShield]), 0f, 1f);
    }

    private static void Refill(Player p)
    {
        if (p.Attributes is { } a)
        {
            a[Stats.CurrentHealth] = a[Stats.MaximumHealth];
            a[Stats.CurrentMana] = a[Stats.MaximumMana];
            a[Stats.CurrentShield] = a[Stats.MaximumShield];
        }
    }

    /// <summary>
    /// Finds quiet corners of the arena for the duels: a rectangle of walkable tiles with no monster (creeps, jungle, towers) around and away from the lanes.
    /// </summary>
    private static List<Point> FindSlots(GameMap map, int wanted)
    {
        var walk = map.Terrain.WalkMap;
        var found = new List<Point>();
        var candidates = new List<(Point Center, double LaneDistance)>();
        for (var x = RectWidth; x < 256 - RectWidth; x += 3)
        {
            for (var y = RectHeight; y < 256 - RectHeight; y += 3)
            {
                if (!RectWalkable(walk, x, y))
                {
                    continue;
                }

                var laneDistance = MobaLayout.Lanes.Min(l => MobaLayout.DistanceToPolyline(l.Waypoints, x, y));
                candidates.Add((new Point((byte)x, (byte)y), laneDistance));
            }
        }

        foreach (var (center, laneDistance) in candidates.OrderByDescending(c => c.LaneDistance))
        {
            if (found.Count >= wanted)
            {
                break;
            }

            if (found.Any(f => Math.Abs(f.X - center.X) < RectWidth + 2 && Math.Abs(f.Y - center.Y) < RectHeight + 2))
            {
                continue;
            }

            if (map.GetAttackablesInRange(center, MonsterFreeRadius).OfType<NPC.Monster>().Any() || laneDistance < 10)
            {
                continue;
            }

            found.Add(center);
        }

        return found;
    }

    private static bool RectWalkable(bool[,] walk, int cx, int cy)
    {
        for (var x = cx - (RectWidth / 2); x <= cx + (RectWidth / 2); x++)
        {
            for (var y = cy - (RectHeight / 2); y <= cy + (RectHeight / 2); y++)
            {
                if (!walk[x, y])
                {
                    return false;
                }
            }
        }

        return true;
    }
}
