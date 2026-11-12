// <copyright file="MobaStressTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.Diagnostics;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the pure parts of the <c>/mobastress</c> test: the window selector, the command parser, the top-up rule, the guard, the
/// verdict and the <see cref="MobaPerf"/> counters.
/// </summary>
[TestFixture]
public class MobaStressTests
{
    private const int Map = 256;

    private static byte[,] Open()
    {
        var grid = new byte[Map, Map];
        for (var x = 0; x < Map; x++)
        {
            for (var y = 0; y < Map; y++)
            {
                grid[x, y] = 1;
            }
        }

        return grid;
    }

    private static byte[,] Empty() => new byte[Map, Map];

    /// <summary>Makes a zone walkable, with the first <paramref name="blockedTiles"/> tiles (row by row) blocked.</summary>
    private static void Zone(byte[,] grid, int x0, int y0, int blockedTiles = 0)
    {
        var index = 0;
        for (var y = y0; y < y0 + MobaStressArena.Size; y++)
        {
            for (var x = x0; x < x0 + MobaStressArena.Size; x++)
            {
                grid[x, y] = (byte)(index++ < blockedTiles ? 0 : 1);
            }
        }
    }

    // ---- selector ----

    /// <summary>On a fully walkable map the window is the one nearest to the centre.</summary>
    [Test]
    public void PickOnOpenMapIsCentered()
    {
        var pick = MobaStressArena.Pick(Open());

        Assert.That(pick.X0, Is.EqualTo(103));
        Assert.That(pick.Y0, Is.EqualTo(103));
        Assert.That(pick.Walkable, Is.EqualTo(2500));
        Assert.That(pick.MeetsThreshold, Is.True);
        Assert.That(pick.Center.X, Is.EqualTo(127.5).Within(0.001));
    }

    /// <summary>A centre window with holes loses to an intact zone elsewhere.</summary>
    [Test]
    public void PickAvoidsHolesInTheCentre()
    {
        var grid = Empty();
        Zone(grid, 0, 0);
        Zone(grid, 103, 103);
        for (var y = 103; y < 153; y++)
        {
            for (var x = 103; x < 153; x++)
            {
                if ((x + y) % 5 == 0)
                {
                    grid[x, y] = 0;
                }
            }
        }

        var pick = MobaStressArena.Pick(grid);

        Assert.That(pick.X0, Is.EqualTo(0));
        Assert.That(pick.Y0, Is.EqualTo(0));
        Assert.That(pick.WalkableFraction, Is.EqualTo(1.0));
    }

    /// <summary>A tiny difference of walkable share only counts if it crosses a step of the quantum.</summary>
    [Test]
    public void PickQuantizesTheWalkableShare()
    {
        var grid = Empty();
        Zone(grid, 0, 0, 125);    // 95.0 % walkable, far from the centre
        Zone(grid, 150, 150, 135); // 94.6 % walkable, nearer to the centre

        var fine = MobaStressArena.Pick(grid, fractionQuantum: 0.01);
        var coarse = MobaStressArena.Pick(grid, fractionQuantum: 0.02);

        Assert.That(fine.X0, Is.LessThan(75), "with 1 % steps the 95 % zone wins");
        Assert.That(coarse.X0, Is.GreaterThanOrEqualTo(100), "with 2 % steps both are equal and the nearer wins");
    }

    /// <summary>Blocked tiles (structure footprints) push the window away; if every window touches them, the least blocked wins.</summary>
    [Test]
    public void PickAvoidsBlockedTiles()
    {
        var blocked = new bool[Map, Map];
        blocked[127, 127] = true;
        var pick = MobaStressArena.Pick(Open(), blocked);

        Assert.That(pick.Blocked, Is.EqualTo(0));
        var contains = 127 >= pick.X0 && 127 < pick.X0 + pick.Size && 127 >= pick.Y0 && 127 < pick.Y0 + pick.Size;
        Assert.That(contains, Is.False);

        var stripes = new bool[Map, Map];
        for (var x = 0; x < Map; x += 40)
        {
            for (var y = 0; y < Map; y++)
            {
                stripes[x, y] = true;
            }
        }

        var least = MobaStressArena.Pick(Open(), stripes);
        Assert.That(least.Blocked, Is.EqualTo(50), "every window holds at least one stripe of 50 tiles");
    }

    /// <summary>When no window reaches 90 % the best one is returned and flagged, without throwing.</summary>
    [Test]
    public void PickWhenNothingReachesTheThreshold()
    {
        var grid = Open();
        for (var x = 0; x < Map; x++)
        {
            for (var y = 0; y < Map; y++)
            {
                if ((x + y) % 5 == 0)
                {
                    grid[x, y] = 0;
                }
            }
        }

        var pick = MobaStressArena.Pick(grid);

        Assert.That(pick.MeetsThreshold, Is.False);
        Assert.That(pick.Walkable, Is.EqualTo(2000));
    }

    /// <summary>With equal walkable share and blocked count, the window with less turret reach wins.</summary>
    [Test]
    public void PickPrefersLessHazard()
    {
        var hazard = MobaStressArena.FootprintMask(new[] { (127, 127) }, 20);
        var pick = MobaStressArena.Pick(Open(), null, hazard);

        Assert.That(pick.Hazard, Is.EqualTo(0));
        Assert.That(pick.X0 == 103 && pick.Y0 == 103, Is.False);
    }

    /// <summary>The choice is deterministic and a full tie goes to the lowest Y, then the lowest X.</summary>
    [Test]
    public void PickIsDeterministicAndBreaksTiesByCorner()
    {
        Assert.That(MobaStressArena.Pick(Open()), Is.EqualTo(MobaStressArena.Pick(Open())));

        var horizontal = Empty();
        Zone(horizontal, 50, 103);
        Zone(horizontal, 156, 103);
        Assert.That(MobaStressArena.Pick(horizontal).X0, Is.EqualTo(50));

        var vertical = Empty();
        Zone(vertical, 103, 50);
        Zone(vertical, 103, 156);
        var pick = MobaStressArena.Pick(vertical);
        Assert.That(pick.Y0, Is.EqualTo(50));
        Assert.That(pick.X0, Is.EqualTo(103));
    }

    /// <summary>Bad sizes and bad grids are rejected.</summary>
    [Test]
    public void PickRejectsBadInput()
    {
        Assert.Throws<ArgumentException>(() => MobaStressArena.Pick(Open(), size: 300));
        Assert.Throws<ArgumentException>(() => MobaStressArena.Pick(Open(), size: 4));
        Assert.Throws<ArgumentException>(() => MobaStressArena.Pick(new byte[100, 100]));
    }

    /// <summary>The footprint is a square around each centre, clipped at the map edge.</summary>
    [Test]
    public void FootprintMaskMarksSquaresAndClips()
    {
        static int Count(bool[,] mask) => mask.Cast<bool>().Count(b => b);

        Assert.That(Count(MobaStressArena.FootprintMask(new[] { (5, 5) }, 2)), Is.EqualTo(25));
        Assert.That(Count(MobaStressArena.FootprintMask(new[] { (0, 0) }, 2)), Is.EqualTo(9));
        Assert.That(Count(MobaStressArena.FootprintMask(new[] { (255, 255), (255, 254) }, 1)), Is.EqualTo(6));
    }

    /// <summary>Spawn slots stay in their third, are walkable, unique, ordered from the outer edge, and mirror each other.</summary>
    [Test]
    public void SpawnSlotsFillTheOuterThirds()
    {
        var grid = Open();
        var pick = MobaStressArena.Pick(grid);
        var west = MobaStressArena.SpawnSlots(grid, pick, westSide: true);
        var east = MobaStressArena.SpawnSlots(grid, pick, westSide: false);
        var third = pick.Size / 3;

        Assert.That(west.Count, Is.EqualTo(third * pick.Size));
        Assert.That(east.Count, Is.EqualTo(west.Count));
        Assert.That(west.Distinct().Count(), Is.EqualTo(west.Count));
        Assert.That(west.All(s => s.X >= pick.X0 && s.X < pick.X0 + third), Is.True);
        Assert.That(east.All(s => s.X >= pick.X0 + pick.Size - third && s.X < pick.X0 + pick.Size), Is.True);
        Assert.That(west[0].X, Is.EqualTo(pick.X0));
        Assert.That(east[0].X, Is.EqualTo(pick.X0 + pick.Size - 1));
        Assert.That(west[0].Y, Is.EqualTo(pick.Y0 + ((pick.Size - 1) / 2)), "the first slot is in the middle row");
        Assert.That(west.Select(s => s.X).Zip(west.Select(s => s.X).Skip(1), (a, b) => b >= a).All(b => b), Is.True, "columns go from the outer edge inward");

        var walls = Open();
        walls[pick.X0, pick.Y0 + 24] = 0;
        Assert.That(MobaStressArena.SpawnSlots(walls, pick, true).Contains((pick.X0, pick.Y0 + 24)), Is.False);

        var (gx, gy) = MobaStressArena.Goal(grid, pick, fromWest: true, row: pick.Y0 + 10);
        Assert.That(grid[gx, gy], Is.Not.EqualTo(0));
        Assert.That(gx, Is.EqualTo(pick.X0 + pick.Size - 4));
        var (ex, ey) = MobaStressArena.EdgeSpot(grid, pick);
        Assert.That(grid[ex, ey], Is.Not.EqualTo(0));
    }

    /// <summary>The goal and the edge spot slide to walkable tiles.</summary>
    [Test]
    public void GoalAndEdgeSpotSnapToWalkable()
    {
        var grid = Open();
        var pick = MobaStressArena.Pick(grid);
        grid[pick.X0 + pick.Size - 4, pick.Y0 + 10] = 0;

        var (gx, gy) = MobaStressArena.Goal(grid, pick, true, pick.Y0 + 10);

        Assert.That(grid[gx, gy], Is.Not.EqualTo(0));
        Assert.That(Math.Abs(gx - (pick.X0 + pick.Size - 4)) + Math.Abs(gy - (pick.Y0 + 10)), Is.GreaterThan(0));
    }

    /// <summary>On the real arena terrain a 50x50 window of mostly walkable ground exists, and one without structures too.</summary>
    [Test]
    public void PickOnTheRealArenaTerrain()
    {
        var grid = new GameMapTerrain(new GameMapDefinition { Number = 200 }).AIgrid;

        var plain = MobaStressArena.Pick(grid);
        Assert.That(plain.WalkableFraction, Is.GreaterThanOrEqualTo(0.90));

        var centers = MobaLayout.Towers.Select(t => ((int)t.Position.X, (int)t.Position.Y))
            .Concat(new[] { MobaTeam.Blue, MobaTeam.Red }.Select(t => ((int)MobaLayout.NexusOf(t).X, (int)MobaLayout.NexusOf(t).Y)))
            .Concat(MobaLayout.Inhibitors.Select(i => ((int)i.Position.X, (int)i.Position.Y)));
        var withStructures = MobaStressArena.Pick(grid, MobaStressArena.FootprintMask(centers, 2));

        Assert.That(withStructures.Blocked, Is.EqualTo(0));
        Assert.That(withStructures.WalkableFraction, Is.GreaterThanOrEqualTo(0.90));
        TestContext.Out.WriteLine($"plain {plain}; clear of structures {withStructures}");
    }

    // ---- arguments ----

    /// <summary>Only a count: default time, no aoe.</summary>
    [Test]
    public void ParseCountOnly()
    {
        var args = MobaStressPlan.Parse("/mobastress 100");

        Assert.That(args.Verb, Is.EqualTo(MobaStressVerb.Start));
        Assert.That(args.Count, Is.EqualTo(100));
        Assert.That(args.Seconds, Is.EqualTo(MobaStressPlan.DefaultSeconds));
        Assert.That(args.AoeTargets, Is.EqualTo(0));
        Assert.That(args.Warning, Is.Null);
    }

    /// <summary>The aoe option can be anywhere and in any case.</summary>
    [Test]
    public void ParseAoeAnywhere()
    {
        var a = MobaStressPlan.Parse("/mobastress 100 60 aoe=3");
        var b = MobaStressPlan.Parse("/mobastress aoe=2 100");
        var c = MobaStressPlan.Parse("/mobastress 100 AOE=3");

        Assert.That((a.Count, a.Seconds, a.AoeTargets), Is.EqualTo((100, 60, 3)));
        Assert.That((b.Count, b.Seconds, b.AoeTargets), Is.EqualTo((100, MobaStressPlan.DefaultSeconds, 2)));
        Assert.That(c.AoeTargets, Is.EqualTo(3));
    }

    /// <summary>The verbs.</summary>
    [Test]
    public void ParseVerbs()
    {
        Assert.That(MobaStressPlan.Parse("/mobastress stop").Verb, Is.EqualTo(MobaStressVerb.Stop));
        Assert.That(MobaStressPlan.Parse("/mobastress parar").Verb, Is.EqualTo(MobaStressVerb.Stop));
        Assert.That(MobaStressPlan.Parse("/mobastress estado").Verb, Is.EqualTo(MobaStressVerb.Status));
        Assert.That(MobaStressPlan.Parse("/mobastress").Verb, Is.EqualTo(MobaStressVerb.Help));
        Assert.That(MobaStressPlan.Parse("/mobastress ayuda").Verb, Is.EqualTo(MobaStressVerb.Help));
    }

    /// <summary>Anything out of range or not understood is invalid with a message.</summary>
    [TestCase("/mobastress 9")]
    [TestCase("/mobastress 401")]
    [TestCase("/mobastress abc")]
    [TestCase("/mobastress 100 4")]
    [TestCase("/mobastress 100 601")]
    [TestCase("/mobastress 100 60 aoe=9")]
    [TestCase("/mobastress 100 60 aoe=x")]
    [TestCase("/mobastress 100 60 70")]
    [TestCase("/mobastress aoe=3")]
    public void ParseInvalid(string text)
    {
        var args = MobaStressPlan.Parse(text);

        Assert.That(args.Verb, Is.EqualTo(MobaStressVerb.Invalid));
        Assert.That(args.Error, Is.Not.Null.And.Not.Empty);
    }

    /// <summary>Close to the client cap there is a warning, up to 350 there is none.</summary>
    [Test]
    public void ParseWarnsAboveTheLimit()
    {
        var high = MobaStressPlan.Parse("/mobastress 351");
        Assert.That(high.Verb, Is.EqualTo(MobaStressVerb.Start));
        Assert.That(high.Warning, Is.Not.Null);
        Assert.That(MobaStressPlan.Parse("/mobastress 350").Warning, Is.Null);
        Assert.That(MobaStressPlan.Parse("/mobastress 400 600").Verb, Is.EqualTo(MobaStressVerb.Start));
    }

    // ---- top-up ----

    /// <summary>The targets split the total, the extra of an odd one going to red.</summary>
    [Test]
    public void TeamTargetsSplitTheTotal()
    {
        Assert.That(MobaStressPlan.TeamTargets(200), Is.EqualTo((100, 100)));
        Assert.That(MobaStressPlan.TeamTargets(201), Is.EqualTo((100, 101)));
        Assert.That(MobaStressPlan.TeamTargets(10), Is.EqualTo((5, 5)));
    }

    /// <summary>A pass tops up at most 30 and never a negative number.</summary>
    [Test]
    public void ToSpawnIsCapped()
    {
        Assert.That(MobaStressPlan.ToSpawn(100, 40), Is.EqualTo(30));
        Assert.That(MobaStressPlan.ToSpawn(100, 90), Is.EqualTo(10));
        Assert.That(MobaStressPlan.ToSpawn(100, 100), Is.EqualTo(0));
        Assert.That(MobaStressPlan.ToSpawn(100, 130), Is.EqualTo(0));
    }

    // ---- guard ----

    /// <summary>An empty world allows the test; each thing that points to a match in progress blocks it.</summary>
    [Test]
    public void BlockReasonCoversEveryActivity()
    {
        var empty = default(MobaStressWorld);
        Assert.That(MobaStressPlan.BlockReason(empty), Is.Null);

        var blocked = new[]
        {
            empty with { PreMatch = true },
            empty with { WavesRunning = true },
            empty with { JungleRunning = true },
            empty with { RegistryUsesArena = true },
            empty with { BotsOnMap = 1 },
            empty with { OtherTeamedChampions = 1 },
            empty with { LiveNonStressCreeps = 5 },
            empty with { IsStressRunning = true },
        };
        foreach (var world in blocked)
        {
            Assert.That(MobaStressPlan.BlockReason(world), Is.Not.Null.And.Not.Empty, world.ToString());
        }

        Assert.That(MobaStressPlan.BlockReason(empty with { IsStressRunning = true }), Does.Contain("stop"));
    }

    // ---- verdict ----

    private static MobaStressSummary Summary(long overruns, double latAvg, double latMax, double cpu, int gen2)
        => new(
            Samples: 12,
            Seconds: 60,
            TicksPerSecMin: 1900,
            TicksPerSecAvg: 2000,
            TicksPerSecMax: 2100,
            TickAvgUs: 300,
            TickMaxUs: 9000,
            ScansPerSecAvg: 1000,
            ScanAvgUs: 50,
            ScanMaxUs: 800,
            TotalTicks: 120000,
            TotalOverruns: overruns,
            LatencyAvgMs: latAvg,
            LatencyMaxMs: latMax,
            CpuAvgPct: cpu,
            CpuMaxPct: cpu + 5,
            AllocMbPerSecAvg: 20,
            Gen2Total: gen2);

    /// <summary>Three kinds of results give the three lights, and the text says what it measured.</summary>
    [Test]
    public void VerdictHasThreeLights()
    {
        var green = MobaStressPlan.Verdict(Summary(10, 5, 120, 30, 0));
        var yellow = MobaStressPlan.Verdict(Summary(1200, 60, 600, 50, 2));
        var red = MobaStressPlan.Verdict(Summary(20000, 400, 5000, 95, 10));

        Assert.That(green, Does.StartWith("VERDE"));
        Assert.That(yellow, Does.StartWith("AMARILLO"));
        Assert.That(red, Does.StartWith("ROJO"));
        Assert.That(green, Does.Contain("servidor").And.Contain("cliente"));
    }

    /// <summary>The accumulator weights the averages by the ticks and keeps the extremes.</summary>
    [Test]
    public void AccumulatorBuildsTheSummary()
    {
        var acc = new MobaStressAccumulator();
        acc.Add(new MobaPerfSample(1000, 100, 5000, 400, 40, 900, 2, 1000, 10, 80), 5, 20, 10, 0);
        acc.Add(new MobaPerfSample(3000, 200, 9000, 800, 60, 700, 3, 3000, 30, 300), 5, 60, 30, 1);

        var s = acc.Build();

        Assert.That(s.Samples, Is.EqualTo(2));
        Assert.That(s.TotalTicks, Is.EqualTo(4000));
        Assert.That(s.TotalOverruns, Is.EqualTo(5));
        Assert.That(s.TickAvgUs, Is.EqualTo(175).Within(0.001));
        Assert.That(s.TickMaxUs, Is.EqualTo(9000));
        Assert.That(s.LatencyAvgMs, Is.EqualTo(25).Within(0.001));
        Assert.That(s.LatencyMaxMs, Is.EqualTo(300));
        Assert.That(s.TicksPerSecMin, Is.EqualTo(200).Within(0.001));
        Assert.That(s.TicksPerSecMax, Is.EqualTo(600).Within(0.001));
        Assert.That(s.CpuAvgPct, Is.EqualTo(40).Within(0.001));
        Assert.That(s.Gen2Total, Is.EqualTo(1));
        Assert.That(new MobaStressAccumulator().Build().Samples, Is.EqualTo(0));
    }

    /// <summary>A test which never filled the arena, or had few observers, says so in the verdict.</summary>
    [Test]
    public void VerdictFlagsIncompleteLoadAndFewObservers()
    {
        var full = Summary(10, 5, 120, 30, 0) with { Target = 400, AliveAvg = 395, AliveMin = 390, FullLoad = true, ObserversMin = 6 };
        var partial = full with { AliveAvg = 250, FullLoad = false };
        var alone = full with { ObserversMin = 1 };
        var empty = Summary(0, 0, 0, 0, 0) with { Samples = 0 };

        Assert.That(MobaStressPlan.Verdict(full), Does.StartWith("VERDE").And.Not.Contain("INCOMPLETA").And.Not.Contain("observador"));
        Assert.That(MobaStressPlan.Verdict(partial), Does.StartWith("CARGA INCOMPLETA").And.Contain("250 de 400"));
        Assert.That(MobaStressPlan.Verdict(alone), Does.StartWith("VERDE").And.Contain("1 observador"));
        Assert.That(MobaStressPlan.Verdict(empty), Does.StartWith("SIN DATOS"));
    }

    /// <summary>Full load starts at 95 % of the target.</summary>
    [Test]
    public void FullLoadStartsAtNinetyFivePercent()
    {
        Assert.That(MobaStressPlan.IsFullLoad(380, 400), Is.True);
        Assert.That(MobaStressPlan.IsFullLoad(379, 400), Is.False);
        Assert.That(MobaStressPlan.IsFullLoad(10, 10), Is.True);
        Assert.That(MobaStressPlan.IsFullLoad(0, 10), Is.False);
    }

    /// <summary>The accumulator keeps the live creeps and the fewest observers.</summary>
    [Test]
    public void AccumulatorKeepsAliveAndObservers()
    {
        var acc = new MobaStressAccumulator();
        var sample = new MobaPerfSample(1000, 100, 5000, 400, 40, 900, 2, 1000, 10, 80);
        acc.Add(sample, 5, 20, 10, 0, alive: 380, observers: 3);
        acc.Add(sample, 5, 20, 10, 0, alive: 400, observers: 1);

        var s = acc.Build(400, true);

        Assert.That(acc.HasSamples, Is.True);
        Assert.That(s.AliveAvg, Is.EqualTo(390).Within(0.001));
        Assert.That(s.AliveMin, Is.EqualTo(380));
        Assert.That(s.ObserversMin, Is.EqualTo(1));
        Assert.That(s.Target, Is.EqualTo(400));
        Assert.That(new MobaStressAccumulator().Build().ObserversMin, Is.EqualTo(-1));
    }

    // ---- MobaPerf ----

    /// <summary>The counters give averages and maxima and reset when read.</summary>
    [Test]
    [NonParallelizable]
    public void PerfAveragesAndResets()
    {
        MobaPerf.Reset();
        var ms = Stopwatch.Frequency / 1000;
        MobaPerf.NoteTick(2 * ms);
        MobaPerf.NoteTick(4 * ms);
        MobaPerf.NoteScan(1 * ms);
        MobaPerf.NoteScan(3 * ms);
        MobaPerf.NoteOverrun();
        MobaPerf.NoteOverrun();
        MobaPerf.NoteOverrun();
        MobaPerf.NoteLatency(250 * ms);

        var sample = MobaPerf.TakeInterval();

        Assert.That(sample.Ticks, Is.EqualTo(2));
        Assert.That(sample.TickAvgUs, Is.EqualTo(3000).Within(30));
        Assert.That(sample.TickMaxUs, Is.EqualTo(4000).Within(40));
        Assert.That(sample.Scans, Is.EqualTo(2));
        Assert.That(sample.ScanAvgUs, Is.EqualTo(2000).Within(20));
        Assert.That(sample.ScanMaxUs, Is.EqualTo(3000).Within(30));
        Assert.That(sample.Overruns, Is.EqualTo(3));
        Assert.That(sample.LatencySamples, Is.EqualTo(1));
        Assert.That(sample.LatencyAvgMs, Is.EqualTo(50).Within(1));
        Assert.That(sample.LatencyMaxMs, Is.EqualTo(50).Within(1));

        var after = MobaPerf.TakeInterval();
        Assert.That(after.Ticks, Is.EqualTo(0));
        Assert.That(after.Overruns, Is.EqualTo(0));
        Assert.That(after.TickAvgUs, Is.EqualTo(0));
    }

    /// <summary>A tick which came earlier than its period counts as no delay.</summary>
    [Test]
    [NonParallelizable]
    public void PerfLatencyNeverGoesNegative()
    {
        MobaPerf.Reset();
        MobaPerf.NoteLatency(Stopwatch.Frequency / 10);

        var sample = MobaPerf.TakeInterval();

        Assert.That(sample.LatencySamples, Is.EqualTo(1));
        Assert.That(sample.LatencyAvgMs, Is.EqualTo(0));
        Assert.That(sample.LatencyMaxMs, Is.EqualTo(0));
    }

    /// <summary>Many threads noting at once lose no counts and keep the right maximum.</summary>
    [Test]
    [NonParallelizable]
    public void PerfIsThreadSafe()
    {
        MobaPerf.Reset();
        Parallel.For(0, 8, new ParallelOptions { MaxDegreeOfParallelism = 8 }, worker =>
        {
            for (var i = 0; i < 10000; i++)
            {
                MobaPerf.NoteTick(i == 9999 ? worker + 100 : 1);
                MobaPerf.NoteOverrun();
            }
        });

        var sample = MobaPerf.TakeInterval();

        Assert.That(sample.Ticks, Is.EqualTo(80000));
        Assert.That(sample.Overruns, Is.EqualTo(80000));
        var maxTs = sample.TickMaxUs * Stopwatch.Frequency / 1_000_000.0;
        Assert.That(maxTs, Is.EqualTo(107).Within(1));
    }
}
