// <copyright file="MobaPerf.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Diagnostics;
using System.Threading;

/// <summary>
/// What a window of <see cref="MobaPerf"/> measured, converted to readable units.
/// </summary>
/// <param name="Ticks">The creep AI ticks which ran.</param>
/// <param name="TickAvgUs">The average wall-clock time of a tick, in microseconds (includes async waits).</param>
/// <param name="TickMaxUs">The longest tick, in microseconds.</param>
/// <param name="Scans">The target scans which ran (synchronous work only, so a fair CPU measure).</param>
/// <param name="ScanAvgUs">The average scan, in microseconds.</param>
/// <param name="ScanMaxUs">The longest scan, in microseconds.</param>
/// <param name="Overruns">The timer callbacks which were dropped because the previous tick of that creep was still running.</param>
/// <param name="LatencySamples">How many tick-to-tick intervals were measured.</param>
/// <param name="LatencyAvgMs">The average delay of a tick over its 200 ms period, in milliseconds.</param>
/// <param name="LatencyMaxMs">The longest delay of a tick over its period, in milliseconds.</param>
public readonly record struct MobaPerfSample(
    long Ticks,
    double TickAvgUs,
    double TickMaxUs,
    long Scans,
    double ScanAvgUs,
    double ScanMaxUs,
    long Overruns,
    long LatencySamples,
    double LatencyAvgMs,
    double LatencyMaxMs);

/// <summary>
/// Process-wide, always-on counters of the lane creep AI (tick time, scan time, dropped ticks and tick delay), used by the
/// <c>/mobastress</c> test. Allocation free: a handful of interlocked operations per tick.
/// </summary>
/// <remarks>
/// <see cref="TakeInterval"/> reads and resets the counters, so it must have a single consumer (the loop of
/// <see cref="MobaStress"/>); <see cref="MobaTelemetry"/> must not call it.
/// </remarks>
public static class MobaPerf
{
    /// <summary>The period of the creep AI timer; a tick which starts later than this after the previous one is "late".</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(200);

    private static long ticks;
    private static long tickSumTs;
    private static long tickMaxTs;
    private static long scans;
    private static long scanSumTs;
    private static long scanMaxTs;
    private static long overruns;
    private static long latSamples;
    private static long latSumTs;
    private static long latMaxTs;

    /// <summary>Notes one finished AI tick.</summary>
    /// <param name="elapsedTs">The elapsed <see cref="Stopwatch"/> timestamp units.</param>
    public static void NoteTick(long elapsedTs)
    {
        Interlocked.Increment(ref ticks);
        Interlocked.Add(ref tickSumTs, elapsedTs);
        InterlockedMax(ref tickMaxTs, elapsedTs);
    }

    /// <summary>Notes one finished target scan.</summary>
    /// <param name="elapsedTs">The elapsed <see cref="Stopwatch"/> timestamp units.</param>
    public static void NoteScan(long elapsedTs)
    {
        Interlocked.Increment(ref scans);
        Interlocked.Add(ref scanSumTs, elapsedTs);
        InterlockedMax(ref scanMaxTs, elapsedTs);
    }

    /// <summary>Notes a timer callback which was dropped because the previous tick had not finished.</summary>
    public static void NoteOverrun() => Interlocked.Increment(ref overruns);

    /// <summary>Notes the real interval between the starts of two consecutive ticks of one creep.</summary>
    /// <param name="intervalTs">The interval in <see cref="Stopwatch"/> timestamp units; the nominal period is subtracted and negative values count as no delay.</param>
    public static void NoteLatency(long intervalTs)
    {
        var late = Math.Max(0, intervalTs - (long)(TickInterval.TotalSeconds * Stopwatch.Frequency));
        Interlocked.Increment(ref latSamples);
        Interlocked.Add(ref latSumTs, late);
        InterlockedMax(ref latMaxTs, late);
    }

    /// <summary>Reads the counters and resets them. Single consumer only.</summary>
    /// <returns>What was measured since the previous call.</returns>
    public static MobaPerfSample TakeInterval()
    {
        var t = Interlocked.Exchange(ref ticks, 0);
        var ts = Interlocked.Exchange(ref tickSumTs, 0);
        var tm = Interlocked.Exchange(ref tickMaxTs, 0);
        var s = Interlocked.Exchange(ref scans, 0);
        var ss = Interlocked.Exchange(ref scanSumTs, 0);
        var sm = Interlocked.Exchange(ref scanMaxTs, 0);
        var o = Interlocked.Exchange(ref overruns, 0);
        var l = Interlocked.Exchange(ref latSamples, 0);
        var ls = Interlocked.Exchange(ref latSumTs, 0);
        var lm = Interlocked.Exchange(ref latMaxTs, 0);

        return new MobaPerfSample(
            t,
            t > 0 ? ToUs(ts) / t : 0,
            ToUs(tm),
            s,
            s > 0 ? ToUs(ss) / s : 0,
            ToUs(sm),
            o,
            l,
            l > 0 ? ToUs(ls) / l / 1000.0 : 0,
            ToUs(lm) / 1000.0);
    }

    /// <summary>Resets every counter.</summary>
    public static void Reset() => _ = TakeInterval();

    private static double ToUs(long timestamps) => timestamps * 1_000_000.0 / Stopwatch.Frequency;

    private static void InterlockedMax(ref long target, long value)
    {
        var current = Volatile.Read(ref target);
        while (value > current)
        {
            var seen = Interlocked.CompareExchange(ref target, value, current);
            if (seen == current)
            {
                return;
            }

            current = seen;
        }
    }
}
