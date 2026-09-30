// <copyright file="MobaGoldEconomyReport.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Offline income simulator for the MOBA gold economy: replays the real constants of
/// <see cref="MobaGold"/> / <see cref="MobaLevels"/> in 5 s ticks for a few play styles and
/// compares the accumulated gold with the real shop prices of a "full" T1 / T2 / T3 loadout.
/// Writes <c>%TEMP%\moba-gold-economy.txt</c>. Run with
/// <c>dotnet test --filter "FullyQualifiedName~MobaGoldEconomyReport"</c>.
/// </summary>
[TestFixture]
[Explicit("Report only")]
public class MobaGoldEconomyReport
{
    private const double WaveSeconds = 48;
    private const int CreepsPerWave = 6;

    private GameConfiguration _gameConfiguration = null!;

    /// <summary>Loads the Season 6 configuration.</summary>
    /// <returns>A task.</returns>
    [OneTimeSetUp]
    public async ValueTask SetupAsync()
    {
        var contextProvider = new InMemoryPersistenceContextProvider();
        await new DataInitialization(contextProvider, new NullLoggerFactory()).CreateInitialDataAsync(3, true).ConfigureAwait(false);
        this._gameConfiguration = (await contextProvider.CreateNewConfigurationContext().GetAsync<GameConfiguration>().ConfigureAwait(false)).First();
    }

    /// <summary>Writes the report.</summary>
    [Test]
    public void Report()
    {
        var sb = new StringBuilder();
        var costs = this.FullLoadoutCosts(sb);
        var profiles = new[]
        {
            new Profile("Promedio", 0.60, 0.20, 0.25, 0.30),
            new Profile("Bien farmeado", 0.90, 0.40, 0.50, 0.40),
            new Profile("Muy bien", 1.00, 0.55, 0.80, 0.50),
        };

        foreach (var (name, t2, t3) in new[] { ($"Factores actuales x1,0/x{MobaGold.PhaseMultiplierT2:0.0}/x{MobaGold.PhaseMultiplierT3:0.0}", MobaGold.PhaseMultiplierT2, MobaGold.PhaseMultiplierT3), ("Plano x1,0/x1,0/x1,0 (referencia)", 1.0, 1.0) })
        {
            sb.AppendLine().AppendLine($"=== {name} ===");
            foreach (var profile in profiles)
            {
                Simulate(sb, profile, profiles[0], t2, t3, costs);
            }
        }

        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "moba-gold-economy.txt"), sb.ToString());
        TestContext.Out.WriteLine(sb.ToString());
    }

    /// <summary>Grid search of a global gold scale + T2/T3 phase factors against the design targets.</summary>
    [Test]
    public void Search()
    {
        var sb = new StringBuilder();
        var costs = this.FullLoadoutCosts(new StringBuilder());
        var avg = new Profile("Promedio", 0.60, 0.20, 0.25, 0.30);
        var good = new Profile("Bien farmeado", 0.90, 0.40, 0.50, 0.40);
        var results = new List<(double Score, string Text)>();
        for (var scale = 0.7; scale <= 1.31; scale += 0.05)
        {
            for (var m2 = 0.6; m2 <= 1.51; m2 += 0.2)
            {
                for (var m3 = 1.0; m3 <= 4.01; m3 += 0.2)
                {
                    var a = Simulate(null, avg, avg, m2, m3, costs, scale);
                    var g = Simulate(null, good, avg, m2, m3, costs, scale);
                    // Targets (perfect purchases, average profile): T1 ~8.5, T2 ~23, T3 ~40.
                    double Miss(double? v, double target) => v is null ? 30 : Math.Abs(v.Value - target);
                    var score = Miss(a.T1, 8.5) + (Miss(a.T2, 23) * 0.5) + Miss(a.T3, 40);
                    results.Add((score, $"K={scale:0.00} m2={m2:0.0} m3={m3:0.0} | prom T1={a.T1:0.0} T2={a.T2:0.0} T3={a.T3:0.0} | bien T1={g.T1:0.0} T2={g.T2:0.0} T3={g.T3:0.0} | score={score:0.0}"));
                }
            }
        }

        foreach (var r in results.OrderBy(r => r.Score).Take(400))
        {
            sb.AppendLine(r.Text);
        }

        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "moba-gold-search.txt"), sb.ToString());
    }

    private static (double? T1, double? T2, double? T3) Simulate(StringBuilder? sb, Profile profile, Profile phaseProfile, double t2Mult, double t3Mult, (long T1, long T2, long T3) costs, double scale = 1.0)
    {
        // The match phase follows the average profile (2 of 5 champions per team must reach the
        // threshold); the profile's own EXP decides its level.
        var (level, exp) = (1, 0.0);
        var (phaseLevel, phaseExp) = (1, 0.0);
        var phase = 1;
        var gold = 0.0;
        double totalGold = 0;
        double? t1 = null, t2 = null, t3 = null;
        var t2Upgrade = costs.T2 - (0.7 * costs.T1);
        var t3Upgrade = costs.T3 - (0.7 * costs.T2);
        var phaseStart = new double[4];
        var perMinute = new double[4];
        var minutesInPhase = new double[4];
        var line = new StringBuilder();

        for (var second = 5; second <= 60 * 60; second += 5)
        {
            var mult = phase switch { 3 => t3Mult, 2 => t2Mult, _ => 1.0 };
            var (g1, e1) = Income(profile, level, mult);
            g1 *= scale;
            gold += g1;
            totalGold += g1;
            perMinute[phase] += g1;
            minutesInPhase[phase] += 5 / 60.0;
            exp += e1;
            while (level < MobaLevels.MaxLevel && exp >= MobaLevels.ExpToNext(level))
            {
                exp -= MobaLevels.ExpToNext(level);
                level++;
                var levelUp = mult * MobaGold.LevelUpGold * scale;
                gold += levelUp;
                totalGold += levelUp;
                perMinute[phase] += levelUp;
            }

            phaseExp += Income(phaseProfile, phaseLevel, 1).Exp;
            while (phaseLevel < MobaLevels.MaxLevel && phaseExp >= MobaLevels.ExpToNext(phaseLevel))
            {
                phaseExp -= MobaLevels.ExpToNext(phaseLevel);
                phaseLevel++;
            }

            var newPhase = phaseLevel >= MobaMatchPhase.T3LevelThreshold ? 3 : phaseLevel >= MobaMatchPhase.T2LevelThreshold ? 2 : 1;
            if (newPhase > phase)
            {
                phase = newPhase;
                phaseStart[phase] = second / 60.0;
            }

            var minute = second / 60.0;
            if (t1 is null && gold >= costs.T1)
            {
                t1 = minute;
                gold -= costs.T1;
            }
            else if (t1 is not null && t2 is null && gold >= t2Upgrade)
            {
                t2 = minute;
                gold -= t2Upgrade;
            }
            else if (t2 is not null && t3 is null && gold >= t3Upgrade)
            {
                t3 = minute;
                gold -= t3Upgrade;
            }

            if (second % 300 == 0)
            {
                line.Append($" m{second / 60}:L{level}/T{phase}");
            }
        }

        if (sb is null)
        {
            return (t1, t2, t3);
        }

        static string F(double? m) => m is null ? "  -  " : $"{m:0.0}";
        static string Rate(double[] gold, double[] mins, int p) => mins[p] > 0 ? $"{gold[p] / mins[p]:0}" : "-";
        sb.AppendLine($"{profile.Name,-14} T1 completo min {F(t1),5} | T2 (mejora) min {F(t2),5} | T3 (mejora) min {F(t3),5} | fase T2 desde min {phaseStart[2]:0.0}, T3 desde min {phaseStart[3]:0.0}");
        sb.AppendLine($"               oro/min por fase: T1={Rate(perMinute, minutesInPhase, 1)} T2={Rate(perMinute, minutesInPhase, 2)} T3={Rate(perMinute, minutesInPhase, 3)} | oro total a 40 min no calculado aparte; total 60 min={totalGold:N0}");
        sb.AppendLine($"               nivel/fase:{line}");
        return (t1, t2, t3);

        (double Gold, double Exp) Income(Profile p, int lvl, double phaseMult)
        {
            var creeps = CreepsPerWave * 5.0 / WaveSeconds;
            var present = creeps * p.Presence;
            var lastHits = present * p.LastHitShare;
            var prox = present - lastHits;
            var kills = p.KillsPerMin * 5 / 60;
            var assists = p.AssistsPerMin * 5 / 60;
            var g = (phaseMult * ((lastHits * MobaGold.CreepLastHitGold) + (prox * MobaGold.CreepProximityGold) + (assists * MobaGold.AssistGold) + MobaGold.PassiveGoldPerTick))
                    + (kills * (MobaGold.ChampionKillGoldBase + (lvl * MobaGold.ChampionKillGoldPerVictimLevel)));
            var e = (lastHits * MobaLevels.CreepLastHitExp) + (prox * MobaLevels.CreepProximityExp) + (assists * MobaLevels.AssistExp)
                    + (kills * (MobaLevels.ChampionKillExp + (lvl * MobaLevels.ChampionKillPerVictimLevel))) + MobaLevels.PassiveExpPerTick;
            return (g, e);
        }
    }

    private (long T1, long T2, long T3) FullLoadoutCosts(StringBuilder sb)
    {
        var totals = new long[3];
        var families = Enum.GetValues<MobaFamily>();
        foreach (var family in families)
        {
            var perTier = new long[3];
            var weaponTiers = new HashSet<MobaShopTier>();
            foreach (var entry in MobaShopCatalog.Entries.Where(e => (e.Families is null || e.Families.Contains(family)) && e.Variant == MobaShopVariant.Standard))
            {
                var counts = entry.Category switch
                {
                    MobaShopCategory.Weapons => !(entry.Group == 4 && entry.Number == 15) && weaponTiers.Add(entry.Tier),
                    MobaShopCategory.Sets or MobaShopCategory.Wings or MobaShopCategory.Accessories => true,
                    _ => false,
                };

                if (counts)
                {
                    perTier[(int)entry.Tier - 1] += MobaShop.PriceOfEntry(this._gameConfiguration, entry);
                }
            }

            sb.AppendLine($"{family,-16} loadout completo (arma+5 piezas+alas+anillo+colgante): T1={perTier[0]:N0} T2={perTier[1]:N0} T3={perTier[2]:N0}");
            for (var i = 0; i < 3; i++)
            {
                totals[i] += perTier[i];
            }
        }

        var n = families.Length;
        sb.AppendLine($"PROMEDIO familias: T1={totals[0] / n:N0} T2={totals[1] / n:N0} T3={totals[2] / n:N0}");
        return (totals[0] / n, totals[1] / n, totals[2] / n);
    }

    private sealed record Profile(string Name, double Presence, double LastHitShare, double KillsPerMin, double AssistsPerMin);
}
