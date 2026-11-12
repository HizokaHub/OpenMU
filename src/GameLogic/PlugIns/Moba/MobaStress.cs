// <copyright file="MobaStress.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The runner of the <c>/mobastress</c> test: fills a 50x50 window of the arena with the real lane creep AI of both teams, keeps
/// the wanted number alive for the given time and logs what the server did (see <see cref="MobaPerf"/>). One test at a time.
/// </summary>
public static class MobaStress
{
    private const string NetworkMeterName = "MUnique.OpenMU.Network.Connection";

    private const string NetworkInstrumentName = "OutgoingBytes";

    private static readonly ConditionalWeakTable<Monster, object> Marks = new();

    private static readonly object Marker = new();

    private static Session? current;

    /// <summary>Gets a value indicating whether a test is running.</summary>
    public static bool IsRunning => Volatile.Read(ref current) is not null;

    /// <summary>Marks a monster as a creep of the stress test (no EXP, gold, drops or panel counts).</summary>
    /// <param name="monster">The monster.</param>
    public static void Mark(Monster monster) => Marks.AddOrUpdate(monster, Marker);

    /// <summary>Gets whether a monster is a creep of the stress test.</summary>
    /// <param name="monster">The monster.</param>
    /// <returns><see langword="true"/> if it is.</returns>
    public static bool IsStress(object? monster) => monster is Monster m && Marks.TryGetValue(m, out _);

    /// <summary>Reads the state of the world which decides whether a test may run.</summary>
    /// <param name="gm">The game master who asks.</param>
    /// <param name="map">The arena map.</param>
    /// <returns>The state.</returns>
    public static async ValueTask<MobaStressWorld> BuildWorldAsync(Player gm, GameMap map)
    {
        var players = await gm.GameContext.GetPlayersAsync().ConfigureAwait(false);
        var other = players.Count(p => !ReferenceEquals(p, gm)
            && ReferenceEquals(p.CurrentMap, map)
            && p is not MobaBotPlayer
            && MobaTeams.GetTeam(p) != MobaTeam.None);
        var creeps = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<Monster>()
            .Count(m => m.IsAlive && !MobaStructures.IsStructure(m) && !IsStress(m) && MobaTeams.GetTeam(m) != MobaTeam.None);
        return new MobaStressWorld(
            MobaArena.Of(map).IsPreMatch,
            MobaWavePeriodicSpawner.IsRunning(map),
            MobaJungle.IsRunning(map),
            MobaMatchRegistry.UsesArena(map),
            MobaBotPlayer.All.Count(b => ReferenceEquals(b.CurrentMap, map)),
            other,
            creeps,
            IsRunning);
    }

    /// <summary>Starts a test.</summary>
    /// <param name="gm">The game master (he is taken out of the teams and moved beside the arena).</param>
    /// <param name="map">The arena map.</param>
    /// <param name="args">The parsed arguments (<see cref="MobaStressVerb.Start"/>).</param>
    /// <returns>The lines to show to the game master.</returns>
    public static async ValueTask<IReadOnlyList<string>> StartAsync(Player gm, GameMap map, MobaStressArgs args)
    {
        var lines = new List<string>();
        if (args.Warning is { } warning)
        {
            lines.Add(warning);
        }

        var grid = map.Terrain.AIgrid;
        var blocked = MobaStressArena.FootprintMask(StructureCenters(map), 2);
        var hazard = MobaStressArena.FootprintMask(
            map.GetAttackablesInRange(new Point(128, 128), 400)
                .OfType<Monster>()
                .Where(m => m.IsAlive && MobaStructures.GetStructureType(m) == MobaStructureType.Turret)
                .Select(m => ((int)m.Position.X, (int)m.Position.Y)),
            8);
        var pick = MobaStressArena.Pick(grid, blocked, hazard);

        if (!pick.MeetsThreshold)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"La mejor ventana libre de estructuras solo tiene {pick.WalkableFraction * 100:0.0} % de suelo caminable (se quería {MobaStressArena.MinWalkableFraction * 100:0} %); se usa igual."));
        }

        if (pick.Blocked > 0)
        {
            lines.Add($"No hay ventana de {pick.Size}x{pick.Size} sin estructuras ({pick.Blocked} tiles bloqueados dentro): algunas criaturas pueden trabarse.");
        }

        if (pick.Hazard > 0)
        {
            lines.Add("Hay torretas vivas cerca de la arena: dispararán a las criaturas y los resultados saldrán sesgados. Quita las estructuras o repite.");
        }

        var logger = gm.GameContext.LoggerFactory.CreateLogger("MobaStress");
        var session = new Session(map, gm.GameContext, args, pick, logger);
        if (Interlocked.CompareExchange(ref current, session, null) is not null)
        {
            lines.Add("Ya hay una prueba en marcha: /mobastress stop");
            return lines;
        }

        try
        {
            MobaTeams.Clear(gm);
            var (ex, ey) = MobaStressArena.EdgeSpot(grid, pick);
            try
            {
                await gm.MoveAsync(new Point((byte)ex, (byte)ey)).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                logger.LogWarning(e, "[MOBA-STRESS] no pude mover al GM junto a la arena");
            }

            session.Gm = gm;
            session.Task = Task.Run(() => session.RunAsync());
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Prueba iniciada: {args.Count} criaturas ({MobaStressPlan.TeamTargets(args.Count).Blue} azules y {MobaStressPlan.TeamTargets(args.Count).Red} rojas), {args.Seconds} s, aoe={args.AoeTargets}, ventana {pick.Size}x{pick.Size} en ({pick.X0},{pick.Y0}) con {pick.WalkableFraction * 100:0.0} % de suelo. Los números salen en el log del servidor ([MOBA-STRESS])."));
        }
        catch
        {
            // Nothing runs yet: free the slot so a later /mobastress is not refused for ever.
            Interlocked.CompareExchange(ref current, null, session);
            throw;
        }

        return lines;
    }

    /// <summary>Stops the running test and waits for it to clean up.</summary>
    /// <returns>The line to show.</returns>
    public static async ValueTask<string> StopAsync()
    {
        var session = Volatile.Read(ref current);
        if (session is null)
        {
            return "No hay ninguna prueba en marcha.";
        }

        session.StopReason = "detenida por el GM";
        session.Cancel();
        try
        {
            if (session.Task is { } task)
            {
                await task.WaitAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            return "La prueba se está deteniendo; mira el log del servidor.";
        }

        return "Prueba detenida y criaturas retiradas. El resumen está en el log del servidor ([MOBA-STRESS] RESUMEN).";
    }

    /// <summary>Describes the running test.</summary>
    /// <returns>The line to show.</returns>
    public static string Describe()
    {
        var session = Volatile.Read(ref current);
        return session is null ? "No hay ninguna prueba en marcha." : session.Describe();
    }

    private static IEnumerable<(int X, int Y)> StructureCenters(GameMap map)
    {
        foreach (var tower in MobaLayout.Towers)
        {
            yield return (tower.Position.X, tower.Position.Y);
        }

        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            var nexus = MobaLayout.NexusOf(team);
            yield return (nexus.X, nexus.Y);
            var shop = MobaLayout.ShopOf(team);
            yield return (shop.X, shop.Y);
        }

        foreach (var inhibitor in MobaLayout.Inhibitors)
        {
            yield return (inhibitor.Position.X, inhibitor.Position.Y);
        }

        foreach (var (x, y) in MobaFountains.Positions)
        {
            yield return (x, y);
        }

        foreach (var (x, y) in MobaFountains.TotemPositions)
        {
            yield return (x, y);
        }

        foreach (var structure in map.GetAttackablesInRange(new Point(128, 128), 400).OfType<Monster>().Where(m => MobaStructures.IsStructure(m)))
        {
            yield return (structure.Position.X, structure.Position.Y);
        }
    }

    private sealed class Session
    {
        private readonly GameMap map;
        private readonly IGameContext gameContext;
        private readonly MobaStressArgs args;
        private readonly MobaStressArenaPick arena;
        private readonly ILogger logger;
        private readonly CancellationTokenSource cts = new();
        private readonly List<Monster> blue = new();
        private readonly List<Monster> red = new();
        private readonly Dictionary<short, MonsterDefinition> definitions = new();
        private readonly IReadOnlyList<(int X, int Y)> blueSlots;
        private readonly IReadOnlyList<(int X, int Y)> redSlots;
        private readonly MobaStressAccumulator accumulator = new();
        private readonly MobaStressAccumulator fillUp = new();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Process process = Process.GetCurrentProcess();
        private readonly object probe = new();
        private int blueCounter;
        private int redCounter;
        private int aoeTick;
        private long aoeOmitted;
        private long noRoom;
        private long spawnedTotal;
        private long netBytes;
        private long netWrites;

        // Written by the run loop only; read by the status command from another thread (the lists themselves are not thread safe).
        private int aliveBlue;
        private int aliveRed;
        private double fullLoadAt = -1;

        // State of the previous sample, for the deltas.
        private double lastSampleSeconds;
        private TimeSpan lastCpu;
        private int lastGc0;
        private int lastGc1;
        private int lastGc2;
        private long lastAllocated;
        private long lastNetBytes;
        private long lastNetWrites;

        public Session(GameMap map, IGameContext gameContext, MobaStressArgs args, MobaStressArenaPick arena, ILogger logger)
        {
            this.map = map;
            this.gameContext = gameContext;
            this.args = args;
            this.arena = arena;
            this.logger = logger;
            this.blueSlots = MobaStressArena.SpawnSlots(map.Terrain.AIgrid, arena, westSide: true);
            this.redSlots = MobaStressArena.SpawnSlots(map.Terrain.AIgrid, arena, westSide: false);
        }

        public Player? Gm { get; set; }

        public Task? Task { get; set; }

        public string StopReason { get; set; } = "completada";

        public void Cancel()
        {
            try
            {
                this.cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already finished
            }
        }

        public string Describe()
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"Prueba en marcha: t={this.clock.Elapsed.TotalSeconds:0}/{this.args.Seconds} s, vivos {Volatile.Read(ref this.aliveBlue)}/{Volatile.Read(ref this.aliveRed)} (objetivo {MobaStressPlan.TeamTargets(this.args.Count).Blue}/{MobaStressPlan.TeamTargets(this.args.Count).Red}), aoe={this.args.AoeTargets}.");
        }

        public async Task RunAsync()
        {
            var (targetBlue, targetRed) = MobaStressPlan.TeamTargets(this.args.Count);
            MeterListener? listener = null;
            var seconds = 0;
            try
            {
                this.StartCounters();
                listener = this.StartNetworkListener();
                MobaPerf.Reset();
                this.lastSampleSeconds = 0;
                this.clock.Restart();
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
                await this.ReplenishAsync(targetBlue, targetRed).ConfigureAwait(false);
                while (await timer.WaitForNextTickAsync(this.cts.Token).ConfigureAwait(false))
                {
                    seconds++;
                    await this.SafeAsync(() => this.args.AoeTargets > 0 ? this.AoeStepAsync() : ValueTask.CompletedTask, "aoe").ConfigureAwait(false);

                    if (seconds % 2 == 0)
                    {
                        // Look for a real match first, so no new test creeps are added next to one.
                        if (await this.CheckWorldAsync().ConfigureAwait(false))
                        {
                            break;
                        }

                        await this.SafeAsync(() => this.ReplenishAsync(targetBlue, targetRed), "reposicion").ConfigureAwait(false);
                    }

                    if (seconds % 5 == 0)
                    {
                        await this.SafeAsync(() => this.LogLineAsync(targetBlue, targetRed), "linea").ConfigureAwait(false);
                    }

                    if (this.clock.Elapsed.TotalSeconds >= this.args.Seconds)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // stopped by the game master
            }
            catch (Exception e)
            {
                this.StopReason = "error: " + e.Message;
                this.logger.LogError(e, "[MOBA-STRESS] la prueba falló");
            }
            finally
            {
                await this.FinishAsync().ConfigureAwait(false);
                listener?.Dispose();
            }
        }

        private void StartCounters()
        {
            this.process.Refresh();
            this.lastCpu = this.process.TotalProcessorTime;
            this.lastGc0 = GC.CollectionCount(0);
            this.lastGc1 = GC.CollectionCount(1);
            this.lastGc2 = GC.CollectionCount(2);
            this.lastAllocated = GC.GetTotalAllocatedBytes(false);
        }

        private MeterListener StartNetworkListener()
        {
            var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == NetworkMeterName && instrument.Name == NetworkInstrumentName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
            {
                Interlocked.Add(ref this.netBytes, measurement);
                Interlocked.Increment(ref this.netWrites);
            });
            listener.Start();
            return listener;
        }

        private async ValueTask SafeAsync(Func<ValueTask> step, string name)
        {
            try
            {
                await step().ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                this.logger.LogWarning(e, "[MOBA-STRESS] fallo en el paso {Step}", name);
            }
        }

        /// <summary>Stops the test when a real match shows up; returns whether it stopped.</summary>
        private async ValueTask<bool> CheckWorldAsync()
        {
            if (this.Gm is not { } gm)
            {
                return false;
            }

            var world = await BuildWorldAsync(gm, this.map).ConfigureAwait(false);
            var reason = MobaStressPlan.BlockReason(world with { IsStressRunning = false });
            if (reason is null)
            {
                return false;
            }

            this.StopReason = "auto-stop: " + reason;
            return true;
        }

        private void PurgeDead()
        {
            this.blue.RemoveAll(m => !m.IsAlive);
            this.red.RemoveAll(m => !m.IsAlive);
            this.PublishCounts();
        }

        private void PublishCounts()
        {
            Volatile.Write(ref this.aliveBlue, this.blue.Count);
            Volatile.Write(ref this.aliveRed, this.red.Count);
        }

        private async ValueTask ReplenishAsync(int targetBlue, int targetRed)
        {
            this.PurgeDead();
            var taken = new HashSet<(int, int)>();
            await this.SpawnForTeamAsync(MobaTeam.Blue, this.blue, MobaStressPlan.ToSpawn(targetBlue, this.blue.Count), this.blueSlots, taken).ConfigureAwait(false);
            await this.SpawnForTeamAsync(MobaTeam.Red, this.red, MobaStressPlan.ToSpawn(targetRed, this.red.Count), this.redSlots, taken).ConfigureAwait(false);
            this.PublishCounts();
        }

        private async ValueTask SpawnForTeamAsync(MobaTeam team, List<Monster> list, int count, IReadOnlyList<(int X, int Y)> slots, HashSet<(int, int)> taken)
        {
            if (count <= 0)
            {
                return;
            }

            var fromWest = team == MobaTeam.Blue;
            var grid = this.map.Terrain.AIgrid;
            var ranks = MobaWaveSpawner.BasicRanks(team, 1);
            var spawned = 0;
            foreach (var slot in slots)
            {
                if (spawned >= count)
                {
                    break;
                }

                var tile = new Point((byte)slot.X, (byte)slot.Y);
                if (taken.Contains(slot) || !MobaOccupancyGrid.IsFree(this.map, tile, this.probe) || MobaOccupancyGrid.IsStructureTile(this.map, tile))
                {
                    continue;
                }

                var counter = team == MobaTeam.Blue ? this.blueCounter++ : this.redCounter++;
                var number = ranks[counter % ranks.Count].Number;
                if (!this.definitions.TryGetValue(number, out var definition))
                {
                    definition = MobaWaveSpawner.StressDefinitionFor(this.gameContext, number)!;
                    if (definition is null)
                    {
                        continue;
                    }

                    this.definitions[number] = definition;
                }

                // Two waypoints (own tile, goal) make the "lane" the creep's own row, as in a real match; with only the goal the
                // AI measures the lane distance to that point and the chase leash would fire in every fight.
                var goal = MobaStressArena.Goal(grid, this.arena, fromWest, slot.Y);
                var waypoints = new[] { tile, new Point((byte)goal.X, (byte)goal.Y) };
                var creep = await MobaWaveSpawner.SpawnStressCreepAsync(this.map, this.gameContext, team, number, tile, waypoints, definition).ConfigureAwait(false);
                if (creep is null)
                {
                    continue;
                }

                taken.Add(slot);
                list.Add(creep);
                spawned++;
                this.spawnedTotal++;
            }

            if (spawned < count)
            {
                this.noRoom += count - spawned;
            }
        }

        /// <summary>Once a second a third of the creeps also hit up to K enemies beside them, to approximate attack animation traffic.</summary>
        private async ValueTask AoeStepAsync()
        {
            this.aoeTick++;
            var budget = Stopwatch.StartNew();
            var all = this.blue.Concat(this.red).ToList();
            for (var i = 0; i < all.Count; i++)
            {
                var creep = all[i];
                if (!creep.IsAlive || (creep.Id + this.aoeTick) % 3 != 0)
                {
                    continue;
                }

                if (budget.ElapsedMilliseconds > 800)
                {
                    this.aoeOmitted += all.Count - i;
                    return;
                }

                var enemies = this.map.GetAttackablesInRange(creep.Position, 2)
                    .Where(a => a.IsActive() && !MobaStructures.IsStructure(a) && MobaTeams.AreEnemies(creep, a))
                    .Take(this.args.AoeTargets)
                    .ToList();
                foreach (var enemy in enemies)
                {
                    await creep.AttackAsync(enemy).ConfigureAwait(false);
                }
            }
        }

        private async ValueTask LogLineAsync(int targetBlue, int targetRed)
        {
            this.PurgeDead();
            var now = this.clock.Elapsed.TotalSeconds;
            var window = now - this.lastSampleSeconds;
            if (window <= 0)
            {
                return;
            }

            this.lastSampleSeconds = now;
            var perf = MobaPerf.TakeInterval();

            this.process.Refresh();
            var cpuNow = this.process.TotalProcessorTime;
            var cpuPct = (cpuNow - this.lastCpu).TotalSeconds / (window * Environment.ProcessorCount) * 100.0;
            this.lastCpu = cpuNow;

            var g0 = GC.CollectionCount(0);
            var g1 = GC.CollectionCount(1);
            var g2 = GC.CollectionCount(2);
            var allocated = GC.GetTotalAllocatedBytes(false);
            var allocMb = (allocated - this.lastAllocated) / 1048576.0 / window;
            var d0 = g0 - this.lastGc0;
            var d1 = g1 - this.lastGc1;
            var d2 = g2 - this.lastGc2;
            this.lastGc0 = g0;
            this.lastGc1 = g1;
            this.lastGc2 = g2;
            this.lastAllocated = allocated;

            var bytes = Interlocked.Read(ref this.netBytes);
            var writes = Interlocked.Read(ref this.netWrites);
            var kbPerSec = (bytes - this.lastNetBytes) / 1024.0 / window;
            var writesPerSec = (writes - this.lastNetWrites) / window;
            this.lastNetBytes = bytes;
            this.lastNetWrites = writes;

            var players = await this.gameContext.GetPlayersAsync().ConfigureAwait(false);
            var observers = players.Where(p => ReferenceEquals(p.CurrentMap, this.map) && p is not MobaBotPlayer).ToList();

            // The samples taken while the arena is still filling up (or never full) go to a separate pile, so the warm-up does not
            // dilute the averages of the real load.
            // The window in which the target is reached still includes the fill-up, so it counts from the next one.
            var alive = this.blue.Count + this.red.Count;
            var filling = this.fullLoadAt < 0;
            (filling ? this.fillUp : this.accumulator).Add(perf, window, cpuPct, allocMb, d2, alive, observers.Count);
            if (this.fullLoadAt < 0 && MobaStressPlan.IsFullLoad(alive, targetBlue + targetRed))
            {
                this.fullLoadAt = now;
            }

            this.logger.LogInformation(
                "{Line}",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"[MOBA-STRESS] t={now:0}s{(filling ? " (llenando)" : string.Empty)} vivos={this.blue.Count}/{this.red.Count} (obj {targetBlue}/{targetRed}) ticks/s={perf.Ticks / window:0} tick_us avg={perf.TickAvgUs:0} max={perf.TickMaxUs:0} "
                    + $"escaneos/s={perf.Scans / window:0} esc_us avg={perf.ScanAvgUs:0} max={perf.ScanMaxUs:0} overruns={perf.Overruns} lat_ms avg={perf.LatencyAvgMs:0.0} max={perf.LatencyMaxMs:0} "
                    + $"pool_cola={ThreadPool.PendingWorkItemCount} hilos={ThreadPool.ThreadCount} gc={d0}/{d1}/{d2} alloc_MB/s={allocMb:0.0} "
                    + $"cpu={cpuPct:0}% ({cpuPct / 100.0 * Environment.ProcessorCount:0.0} nucleos de {Environment.ProcessorCount}) observadores={observers.Count}(clones {observers.Count(p => p.IsMobaClone)}) "
                    + $"red_KB/s={kbPerSec:0} escrituras/s={writesPerSec:0} sin_sitio={this.noRoom} aoe_omitidos={this.aoeOmitted}"));
        }

        private async ValueTask FinishAsync()
        {
            try
            {
                if (this.clock.Elapsed.TotalSeconds - this.lastSampleSeconds >= 1)
                {
                    await this.SafeAsync(() => this.LogLineAsync(this.args.Count / 2, this.args.Count - (this.args.Count / 2)), "linea final").ConfigureAwait(false);
                }

                var removed = 0;
                foreach (var creep in this.blue.Concat(this.red).ToList())
                {
                    MobaTeams.Clear(creep);
                    try
                    {
                        await this.map.RemoveAsync(creep).ConfigureAwait(false);
                        creep.Dispose();
                        removed++;
                    }
                    catch
                    {
                        // already gone
                    }
                }

                this.blue.Clear();
                this.red.Clear();

                var residual = MobaOccupancyGrid.CountCreepTiles(this.map);
                // Without a sample at full load, fall back to the fill-up samples and let the verdict say the load was incomplete.
                var reachedFull = this.accumulator.HasSamples;
                var summary = (reachedFull ? this.accumulator : this.fillUp).Build(this.args.Count, reachedFull);
                var verdict = MobaStressPlan.Verdict(summary);
                var text = string.Create(
                    CultureInfo.InvariantCulture,
                    $"[MOBA-STRESS] RESUMEN n={this.args.Count} aoe={this.args.AoeTargets} {summary.Seconds:0}s ({this.StopReason}) creadas={this.spawnedTotal} retiradas={removed} tiles_residuales={residual} plena_carga_a={(this.fullLoadAt >= 0 ? this.fullLoadAt.ToString("0", CultureInfo.InvariantCulture) + "s" : "nunca")} vivas prom={summary.AliveAvg:0} min={summary.AliveMin:0} observadores_min={summary.ObserversMin} "
                    + $"ticks/s min={summary.TicksPerSecMin:0} prom={summary.TicksPerSecAvg:0} max={summary.TicksPerSecMax:0} tick_us avg={summary.TickAvgUs:0} max={summary.TickMaxUs:0} "
                    + $"escaneos/s={summary.ScansPerSecAvg:0} esc_us avg={summary.ScanAvgUs:0} max={summary.ScanMaxUs:0} overruns={summary.TotalOverruns}/{summary.TotalTicks} "
                    + $"lat_ms avg={summary.LatencyAvgMs:0.0} max={summary.LatencyMaxMs:0} cpu prom={summary.CpuAvgPct:0}% max={summary.CpuMaxPct:0}% alloc_MB/s={summary.AllocMbPerSecAvg:0.0} gen2={summary.Gen2Total} "
                    + $"sin_sitio={this.noRoom} aoe_omitidos={this.aoeOmitted} VEREDICTO: {verdict}");
                this.logger.LogInformation("{Line}", text);

                if (this.Gm is { } gm)
                {
                    try
                    {
                        await gm.ShowBlueMessageAsync($"[STRESS] Fin ({this.StopReason}). {verdict}").ConfigureAwait(false);
                    }
                    catch
                    {
                        // the game master may have left
                    }
                }
            }
            catch (Exception e)
            {
                this.logger.LogError(e, "[MOBA-STRESS] error al terminar la prueba");
            }
            finally
            {
                Interlocked.CompareExchange(ref current, null, this);
                this.cts.Dispose();
            }
        }
    }
}
