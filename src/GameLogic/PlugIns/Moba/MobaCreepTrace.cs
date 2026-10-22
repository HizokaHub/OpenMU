// <copyright file="MobaCreepTrace.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using System.Text;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Diagnostic trace of one lane creep: where it was every AI tick and which decision moved it. Meant to find creeps which look
/// as if they advanced and retreated while really fighting: when the recent moves of a creep keep reversing without getting
/// anywhere, <see cref="Check"/> returns a <c>[MOBA-CREEP-JITTER]</c> report with the trail of positions and decisions, the
/// target switches and the state of the AI. The decision counters of all creeps are summed in <see cref="TakeSummary"/> for the
/// periodic <c>[MOBA-CREEP-STATS]</c> line.
/// </summary>
public sealed class MobaCreepTrace
{
    /// <summary>The seconds of history the detector looks at.</summary>
    public const double WindowSeconds = 6;

    /// <summary>The reversals of heading within the window from which a creep is reported.</summary>
    public const int ReversalsToReport = 3;

    /// <summary>The path length (tiles) within the window below which nothing is reported.</summary>
    public const double MinimumPathTiles = 4;

    private const int MaxTrailMoves = 16;

    /// <summary>Gets or sets the logger the reports go to (set by the telemetry, which has one).</summary>
    public static Microsoft.Extensions.Logging.ILogger? Logger { get; set; }

    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);
    private static readonly ConcurrentDictionary<string, long> Decisions = new();
    private static long jitterReports;
    private static long creepTicks;

    private readonly Queue<Sample> _samples = new();
    private DateTime _lastCheckUtc;
    private DateTime _lastReportUtc;

    /// <summary>One AI tick of the creep.</summary>
    /// <param name="At">When.</param>
    /// <param name="Pos">Where the creep was.</param>
    /// <param name="Decision">What the previous tick decided (the cause of the move).</param>
    /// <param name="Target">The target at that moment.</param>
    public readonly record struct Sample(DateTime At, Point Pos, string Decision, string Target);

    /// <summary>
    /// Counts one decision for the summary of all creeps.
    /// </summary>
    /// <param name="team">The creep's team.</param>
    /// <param name="decision">The decision.</param>
    public static void Count(MobaTeam team, string decision)
    {
        Decisions.AddOrUpdate($"{team}.{decision}", 1, (_, current) => current + 1);
        Interlocked.Increment(ref creepTicks);
    }

    /// <summary>
    /// Takes (and resets) the summary of the decisions of all creeps since the last call.
    /// </summary>
    /// <returns>The text, or <see langword="null"/> if no creep ticked.</returns>
    public static string? TakeSummary()
    {
        var ticks = Interlocked.Exchange(ref creepTicks, 0);
        var reports = Interlocked.Exchange(ref jitterReports, 0);
        if (ticks == 0)
        {
            return null;
        }

        var parts = new List<string>();
        foreach (var key in Decisions.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            if (Decisions.TryRemove(key, out var value))
            {
                parts.Add($"{key}={value}");
            }
        }

        return $"creep ticks={ticks} jitterReports={reports} | {string.Join(' ', parts)}";
    }

    /// <summary>
    /// Records the state of the creep at the start of an AI tick.
    /// </summary>
    /// <param name="sample">The sample.</param>
    public void Record(Sample sample)
    {
        this._samples.Enqueue(sample);
        while (this._samples.Count > 0 && (sample.At - this._samples.Peek().At).TotalSeconds > WindowSeconds)
        {
            this._samples.Dequeue();
        }
    }

    /// <summary>
    /// Looks at the recent moves of the creep and, if it kept changing heading without getting anywhere, builds the report.
    /// Checked once a second and reported at most every few seconds per creep.
    /// </summary>
    /// <param name="now">The current time.</param>
    /// <param name="header">What the report starts with (creep, team, position, AI state).</param>
    /// <returns>The report, or <see langword="null"/> if the creep moves normally.</returns>
    public string? Check(DateTime now, Func<string> header)
    {
        if (now - this._lastCheckUtc < CheckEvery || now - this._lastReportUtc < ReportEvery)
        {
            return null;
        }

        this._lastCheckUtc = now;
        var moves = this.Moves();
        if (moves.Count < ReversalsToReport + 1)
        {
            return null;
        }

        var path = 0.0;
        var reversals = 0;
        for (var i = 0; i < moves.Count; i++)
        {
            path += Length(moves[i].Dx, moves[i].Dy);
            if (i > 0 && (moves[i].Dx * moves[i - 1].Dx) + (moves[i].Dy * moves[i - 1].Dy) < 0)
            {
                reversals++;
            }
        }

        if (reversals < ReversalsToReport || path < MinimumPathTiles)
        {
            return null;
        }

        this._lastReportUtc = now;
        Interlocked.Increment(ref jitterReports);
        var first = this._samples.First().Pos;
        var last = this._samples.Last().Pos;
        var net = Length(last.X - first.X, last.Y - first.Y);
        var switches = this._samples.Zip(this._samples.Skip(1), (a, b) => a.Target != b.Target).Count(changed => changed);
        var histogram = string.Join(' ', this._samples.GroupBy(s => s.Decision).OrderByDescending(g => g.Count()).Select(g => $"{g.Key}={g.Count()}"));
        return $"{header()} | window={WindowSeconds:0}s moves={moves.Count} path={path:0.0} net={net:0.0} reversals={reversals} targetSwitches={switches} decisions=[{histogram}] | trail: {this.Trail(now, moves)}";
    }

    private static double Length(double dx, double dy) => Math.Sqrt((dx * dx) + (dy * dy));

    private List<(int Dx, int Dy, Sample To)> Moves()
    {
        var moves = new List<(int, int, Sample)>();
        Sample? previous = null;
        foreach (var sample in this._samples)
        {
            if (previous is { } p && p.Pos != sample.Pos)
            {
                moves.Add((sample.Pos.X - p.Pos.X, sample.Pos.Y - p.Pos.Y, sample));
            }

            previous = sample;
        }

        return moves;
    }

    private string Trail(DateTime now, List<(int Dx, int Dy, Sample To)> moves)
    {
        var text = new StringBuilder();
        foreach (var (_, _, to) in moves.Skip(Math.Max(0, moves.Count - MaxTrailMoves)))
        {
            text.Append($"-{(now - to.At).TotalSeconds:0.0}s ({to.Pos.X},{to.Pos.Y}) {to.Decision}->{to.Target}; ");
        }

        return text.ToString().TrimEnd();
    }
}
