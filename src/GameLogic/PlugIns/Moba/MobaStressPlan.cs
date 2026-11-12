// <copyright file="MobaStressPlan.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Globalization;

/// <summary>
/// What <c>/mobastress</c> was asked to do.
/// </summary>
public enum MobaStressVerb
{
    /// <summary>Start a test.</summary>
    Start,

    /// <summary>Stop the running test.</summary>
    Stop,

    /// <summary>Describe the running test.</summary>
    Status,

    /// <summary>Show the usage.</summary>
    Help,

    /// <summary>The text could not be understood; see <see cref="MobaStressArgs.Error"/>.</summary>
    Invalid,
}

/// <summary>
/// The parsed arguments of <c>/mobastress</c>.
/// </summary>
/// <param name="Verb">What to do.</param>
/// <param name="Count">The creeps to keep alive (both teams together).</param>
/// <param name="Seconds">How long the test runs.</param>
/// <param name="AoeTargets">How many enemies each creep also hits in a burst every few seconds (0 = none).</param>
/// <param name="Error">Why the text is invalid (Spanish), when <see cref="Verb"/> is <see cref="MobaStressVerb.Invalid"/>.</param>
/// <param name="Warning">A caution to show before starting (Spanish), or <see langword="null"/>.</param>
public sealed record MobaStressArgs(MobaStressVerb Verb, int Count, int Seconds, int AoeTargets, string? Error, string? Warning);

/// <summary>
/// The state of the world which decides whether a stress test may run (see <see cref="MobaStressPlan.BlockReason"/>).
/// </summary>
/// <param name="PreMatch">The arena is in its pre-match countdown.</param>
/// <param name="WavesRunning">The periodic lane waves are running.</param>
/// <param name="JungleRunning">The jungle camps are running.</param>
/// <param name="RegistryUsesArena">A queued match owns the arena.</param>
/// <param name="BotsOnMap">Bots on the map.</param>
/// <param name="OtherTeamedChampions">Champions of other players which belong to a team.</param>
/// <param name="LiveNonStressCreeps">Live team creeps which are not stress test creeps.</param>
/// <param name="IsStressRunning">A stress test is already running.</param>
public readonly record struct MobaStressWorld(
    bool PreMatch,
    bool WavesRunning,
    bool JungleRunning,
    bool RegistryUsesArena,
    int BotsOnMap,
    int OtherTeamedChampions,
    int LiveNonStressCreeps,
    bool IsStressRunning);

/// <summary>
/// The totals of a finished test, built by <see cref="MobaStressAccumulator"/>.
/// </summary>
/// <param name="Samples">How many 5 s samples were taken.</param>
/// <param name="Seconds">The seconds covered by the samples.</param>
/// <param name="TicksPerSecMin">The lowest creep ticks per second of a sample.</param>
/// <param name="TicksPerSecAvg">The average creep ticks per second.</param>
/// <param name="TicksPerSecMax">The highest creep ticks per second of a sample.</param>
/// <param name="TickAvgUs">The average tick time in microseconds.</param>
/// <param name="TickMaxUs">The longest tick in microseconds.</param>
/// <param name="ScansPerSecAvg">The average scans per second.</param>
/// <param name="ScanAvgUs">The average scan in microseconds.</param>
/// <param name="ScanMaxUs">The longest scan in microseconds.</param>
/// <param name="TotalTicks">All ticks which ran.</param>
/// <param name="TotalOverruns">All timer callbacks dropped because the creep was still busy.</param>
/// <param name="LatencyAvgMs">The average tick delay in milliseconds.</param>
/// <param name="LatencyMaxMs">The longest tick delay in milliseconds.</param>
/// <param name="CpuAvgPct">The average CPU use of the process (100 = every core).</param>
/// <param name="CpuMaxPct">The highest CPU use of a sample.</param>
/// <param name="AllocMbPerSecAvg">The average allocation rate in MB per second.</param>
/// <param name="Gen2Total">Generation 2 collections during the test.</param>
/// <param name="Target">The creeps the test was asked to keep alive (0 = unknown).</param>
/// <param name="AliveAvg">The average live creeps over the samples.</param>
/// <param name="AliveMin">The fewest live creeps of a sample.</param>
/// <param name="FullLoad">The samples were taken at full load (otherwise they include the fill-up or never reached the target).</param>
/// <param name="ObserversMin">The fewest players seeing the map during a sample (-1 = unknown).</param>
public sealed record MobaStressSummary(
    int Samples,
    double Seconds,
    double TicksPerSecMin,
    double TicksPerSecAvg,
    double TicksPerSecMax,
    double TickAvgUs,
    double TickMaxUs,
    double ScansPerSecAvg,
    double ScanAvgUs,
    double ScanMaxUs,
    long TotalTicks,
    long TotalOverruns,
    double LatencyAvgMs,
    double LatencyMaxMs,
    double CpuAvgPct,
    double CpuMaxPct,
    double AllocMbPerSecAvg,
    int Gen2Total,
    int Target = 0,
    double AliveAvg = 0,
    double AliveMin = 0,
    bool FullLoad = true,
    int ObserversMin = -1);

/// <summary>
/// Collects the samples of a test into a <see cref="MobaStressSummary"/> (weighted by how many ticks / scans each sample had).
/// </summary>
public sealed class MobaStressAccumulator
{
    private int samples;
    private double seconds;
    private double tpsMin = double.MaxValue;
    private double tpsMax;
    private long ticks;
    private double tickSumUs;
    private double tickMaxUs;
    private long scans;
    private double scanSumUs;
    private double scanMaxUs;
    private long overruns;
    private long latSamples;
    private double latSumMs;
    private double latMaxMs;
    private double cpuSum;
    private double cpuMax;
    private double allocSum;
    private int gen2;
    private double aliveSum;
    private int aliveMin = int.MaxValue;
    private int observersMin = int.MaxValue;

    /// <summary>Gets a value indicating whether at least one sample was added.</summary>
    public bool HasSamples => this.samples > 0;

    /// <summary>Adds one sample.</summary>
    /// <param name="perf">The creep AI counters of the window.</param>
    /// <param name="windowSeconds">The length of the window.</param>
    /// <param name="cpuPercent">The CPU use of the process in the window (100 = every core).</param>
    /// <param name="allocMbPerSec">The allocation rate in MB per second.</param>
    /// <param name="gen2Delta">Generation 2 collections in the window.</param>
    /// <param name="alive">The live creeps at the end of the window.</param>
    /// <param name="observers">The players who see the map (-1 = unknown).</param>
    public void Add(MobaPerfSample perf, double windowSeconds, double cpuPercent, double allocMbPerSec, int gen2Delta, int alive = 0, int observers = -1)
    {
        if (windowSeconds <= 0)
        {
            return;
        }

        this.samples++;
        this.seconds += windowSeconds;
        var tps = perf.Ticks / windowSeconds;
        this.tpsMin = Math.Min(this.tpsMin, tps);
        this.tpsMax = Math.Max(this.tpsMax, tps);
        this.ticks += perf.Ticks;
        this.tickSumUs += perf.TickAvgUs * perf.Ticks;
        this.tickMaxUs = Math.Max(this.tickMaxUs, perf.TickMaxUs);
        this.scans += perf.Scans;
        this.scanSumUs += perf.ScanAvgUs * perf.Scans;
        this.scanMaxUs = Math.Max(this.scanMaxUs, perf.ScanMaxUs);
        this.overruns += perf.Overruns;
        this.latSamples += perf.LatencySamples;
        this.latSumMs += perf.LatencyAvgMs * perf.LatencySamples;
        this.latMaxMs = Math.Max(this.latMaxMs, perf.LatencyMaxMs);
        this.cpuSum += cpuPercent;
        this.cpuMax = Math.Max(this.cpuMax, cpuPercent);
        this.allocSum += allocMbPerSec;
        this.gen2 += gen2Delta;
        this.aliveSum += alive;
        this.aliveMin = Math.Min(this.aliveMin, alive);
        if (observers >= 0)
        {
            this.observersMin = Math.Min(this.observersMin, observers);
        }
    }

    /// <summary>Builds the totals.</summary>
    /// <param name="target">The creeps the test was asked to keep alive (0 = unknown).</param>
    /// <param name="fullLoad">Whether the samples were taken at full load.</param>
    /// <returns>The summary (all zeros when nothing was added).</returns>
    public MobaStressSummary Build(int target = 0, bool fullLoad = true) => new(
        this.samples,
        this.seconds,
        this.samples > 0 ? this.tpsMin : 0,
        this.seconds > 0 ? this.ticks / this.seconds : 0,
        this.tpsMax,
        this.ticks > 0 ? this.tickSumUs / this.ticks : 0,
        this.tickMaxUs,
        this.seconds > 0 ? this.scans / this.seconds : 0,
        this.scans > 0 ? this.scanSumUs / this.scans : 0,
        this.scanMaxUs,
        this.ticks,
        this.overruns,
        this.latSamples > 0 ? this.latSumMs / this.latSamples : 0,
        this.latMaxMs,
        this.samples > 0 ? this.cpuSum / this.samples : 0,
        this.cpuMax,
        this.samples > 0 ? this.allocSum / this.samples : 0,
        this.gen2,
        target,
        this.samples > 0 ? this.aliveSum / this.samples : 0,
        this.samples > 0 ? this.aliveMin : 0,
        fullLoad,
        this.observersMin == int.MaxValue ? -1 : this.observersMin);
}

/// <summary>
/// The pure decisions of the <c>/mobastress</c> test: reading the command, how many creeps to top up and whether the arena may
/// be used. Free of game types so it can be tested directly.
/// </summary>
public static class MobaStressPlan
{
    /// <summary>The seconds a test lasts when none is given.</summary>
    public const int DefaultSeconds = 120;

    /// <summary>The longest test.</summary>
    public const int MaxSeconds = 600;

    /// <summary>The shortest test.</summary>
    public const int MinSeconds = 5;

    /// <summary>The fewest creeps.</summary>
    public const int MinCount = 10;

    /// <summary>The most creeps.</summary>
    public const int MaxCount = 400;

    /// <summary>The characters the client can hold at once (<c>MAX_CHARACTERS_CLIENT</c>), creeps, towers, nexus and heroes together.</summary>
    public const int ClientCharacterCap = 400;

    /// <summary>Above this many creeps the command warns about the client cap.</summary>
    public const int WarnAbove = 350;

    /// <summary>The most enemies a creep hits in one burst of the aoe option.</summary>
    public const int MaxAoeTargets = 8;

    /// <summary>The most creeps one team gets spawned in one top-up pass (so a big number does not arrive in one instant).</summary>
    public const int MaxSpawnPerTeamPerPass = 30;

    /// <summary>The share of the target from which the samples count as full load.</summary>
    public const double FullLoadShare = 0.95;

    /// <summary>The average live share of the target below which the verdict says the load was incomplete.</summary>
    public const double MinAliveShare = 0.9;

    /// <summary>The players a real 3v3 has seeing the whole map.</summary>
    public const int RealObservers = 6;

    /// <summary>
    /// Reads the text of the command.
    /// </summary>
    /// <param name="commandLine">The text as the chat gives it, for example <c>/mobastress 200 90 aoe=3</c>.</param>
    /// <returns>The arguments.</returns>
    public static MobaStressArgs Parse(string commandLine)
    {
        var tokens = (commandLine ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (tokens.Count > 0 && tokens[0].StartsWith('/'))
        {
            tokens.RemoveAt(0);
        }

        if (tokens.Count == 0)
        {
            return Of(MobaStressVerb.Help);
        }

        var first = tokens[0].ToLowerInvariant();
        switch (first)
        {
            case "ayuda" or "help" or "?":
                return Of(MobaStressVerb.Help);
            case "stop" or "parar" or "detener":
                return Of(MobaStressVerb.Stop);
            case "estado" or "status":
                return Of(MobaStressVerb.Status);
        }

        int? count = null;
        int? seconds = null;
        var aoe = 0;
        foreach (var token in tokens)
        {
            var lower = token.ToLowerInvariant();
            if (lower.StartsWith("aoe=", StringComparison.Ordinal))
            {
                if (!int.TryParse(lower.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out aoe) || aoe > MaxAoeTargets)
                {
                    return Invalid($"aoe debe ser un número de 0 a {MaxAoeTargets} (recibí '{token}').");
                }

                continue;
            }

            if (!int.TryParse(lower, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
            {
                return Invalid($"No entiendo '{token}'. Uso: /mobastress <criaturas {MinCount}-{MaxCount}> [segundos {MinSeconds}-{MaxSeconds}] [aoe=K] | stop | estado");
            }

            if (count is null)
            {
                count = number;
            }
            else if (seconds is null)
            {
                seconds = number;
            }
            else
            {
                return Invalid($"Sobra el argumento '{token}'. Uso: /mobastress <criaturas> [segundos] [aoe=K]");
            }
        }

        if (count is null)
        {
            return Invalid($"Falta la cantidad de criaturas ({MinCount}-{MaxCount}).");
        }

        if (count < MinCount || count > MaxCount)
        {
            return Invalid($"La cantidad de criaturas debe estar entre {MinCount} y {MaxCount} (recibí {count}).");
        }

        var secs = seconds ?? DefaultSeconds;
        if (secs < MinSeconds || secs > MaxSeconds)
        {
            return Invalid($"Los segundos deben estar entre {MinSeconds} y {MaxSeconds} (recibí {secs}).");
        }

        string? warning = count > WarnAbove
            ? $"{count} criaturas se acercan al tope de {ClientCharacterCap} personajes del cliente (criaturas, torretas, nexo y héroes juntos): puede haber objetos que no se dibujen."
            : null;
        return new MobaStressArgs(MobaStressVerb.Start, count.Value, secs, aoe, null, warning);
    }

    /// <summary>Splits the wanted total between the teams; an odd total gives the extra creep to red.</summary>
    /// <param name="n">The total.</param>
    /// <returns>The blue and red targets.</returns>
    public static (int Blue, int Red) TeamTargets(int n) => (n / 2, n - (n / 2));

    /// <summary>How many creeps a team gets spawned in this pass.</summary>
    /// <param name="target">The wanted live creeps.</param>
    /// <param name="alive">The live creeps now.</param>
    /// <param name="maxPerPass">The cap per pass.</param>
    /// <returns>A number from 0 to <paramref name="maxPerPass"/>.</returns>
    public static int ToSpawn(int target, int alive, int maxPerPass = MaxSpawnPerTeamPerPass)
        => Math.Clamp(target - alive, 0, maxPerPass);

    /// <summary>
    /// Says why the test may not run now.
    /// </summary>
    /// <param name="w">The state of the world.</param>
    /// <returns><see langword="null"/> if it can run; otherwise a Spanish reason.</returns>
    public static string? BlockReason(in MobaStressWorld w)
    {
        if (w.IsStressRunning)
        {
            return "Ya hay una prueba de estrés en marcha: /mobastress stop";
        }

        if (w.BotsOnMap > 0)
        {
            return "Hay una partida en curso (bots en el mapa): /mobafight stop o /mobacmd parar primero.";
        }

        if (w.RegistryUsesArena)
        {
            return "Una partida de la cola está usando la arena.";
        }

        if (w.WavesRunning)
        {
            return "Las oleadas están corriendo: /mobafight stop o /mobacmd parar primero.";
        }

        if (w.JungleRunning)
        {
            return "La jungla está corriendo: /mobafight stop primero.";
        }

        if (w.PreMatch)
        {
            return "La arena está en la cuenta atrás de una partida.";
        }

        if (w.OtherTeamedChampions > 0)
        {
            return "Hay campeones de otros jugadores con equipo en el mapa.";
        }

        if (w.LiveNonStressCreeps > 0)
        {
            return $"Hay {w.LiveNonStressCreeps} criaturas de una partida en el mapa: /mobafight stop primero.";
        }

        return null;
    }

    /// <summary>
    /// Turns the totals of a test into a short traffic light in Spanish. The limits are a first guess to be calibrated with the first
    /// real measurement.
    /// </summary>
    /// <param name="s">The totals.</param>
    /// <param name="clientCap">The character cap of the client.</param>
    /// <returns>The verdict text.</returns>
    public static string Verdict(MobaStressSummary s, int clientCap = ClientCharacterCap)
    {
        if (s.Samples == 0)
        {
            return "SIN DATOS: la prueba terminó antes de tomar una muestra; dura más de 10 s para tener números.";
        }

        var overrunShare = s.TotalTicks > 0 ? 100.0 * s.TotalOverruns / s.TotalTicks : 0;
        var gen2PerMinute = s.Seconds > 0 ? s.Gen2Total * 60.0 / s.Seconds : 0;

        string light;
        if (overrunShare < 0.5 && s.LatencyAvgMs < 25 && s.LatencyMaxMs < 250 && s.CpuAvgPct < 70 && gen2PerMinute <= 1)
        {
            light = "VERDE (sin lag)";
        }
        else if (overrunShare < 3 && s.LatencyAvgMs < 100 && s.LatencyMaxMs < 1000)
        {
            light = "AMARILLO (justo)";
        }
        else
        {
            light = "ROJO (con lag)";
        }

        var incomplete = s.Target > 0 && (!s.FullLoad || s.AliveAvg < s.Target * MinAliveShare);
        var prefix = incomplete
            ? string.Create(CultureInfo.InvariantCulture, $"CARGA INCOMPLETA (vivas prom {s.AliveAvg:0} de {s.Target}{(s.FullLoad ? string.Empty : ", no se llegó a plena carga o solo hay muestras del llenado")}); el semáforo es optimista. ")
            : string.Empty;
        var observers = s.ObserversMin is >= 0 and < RealObservers
            ? string.Create(CultureInfo.InvariantCulture, $" Con {s.ObserversMin} observador(es) la red mide menos que un 3v3 real ({RealObservers} jugadores con visión total): la difusión real será mayor.")
            : string.Empty;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{prefix}{light}: ticks omitidos {overrunShare:0.00} %, retraso de tick prom {s.LatencyAvgMs:0.0} ms / máx {s.LatencyMaxMs:0} ms, CPU prom {s.CpuAvgPct:0} %, gen2 {gen2PerMinute:0.0}/min. "
            + $"Esto mide SOLO el servidor; el FPS del cliente sale en MuError.log (medidor del cliente) y por encima de {clientCap} personajes el cliente deja de dibujar objetos.{observers} "
            + $"El goteo constante no cubre el pico de una oleada que llega de golpe ni el coste por muerte (EXP y oro).");
    }

    /// <summary>Says whether the live creeps are close enough to the target for the samples to count as full load.</summary>
    /// <param name="alive">The live creeps.</param>
    /// <param name="target">The wanted live creeps.</param>
    /// <returns><see langword="true"/> from 95 % of the target.</returns>
    public static bool IsFullLoad(int alive, int target) => alive >= Math.Ceiling(target * FullLoadShare);

    private static MobaStressArgs Of(MobaStressVerb verb) => new(verb, 0, 0, 0, null, null);

    private static MobaStressArgs Invalid(string error) => new(MobaStressVerb.Invalid, 0, 0, 0, error, null);
}
