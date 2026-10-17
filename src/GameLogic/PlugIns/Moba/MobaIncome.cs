// <copyright file="MobaIncome.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

/// <summary>
/// Telemetry of what every champion earned, by source (jungle, lane creeps, passive drip, kills...), written into the periodic
/// <c>[MOBA-INCOME]</c> log line so a jungler can be compared with the laners.
/// </summary>
public static class MobaIncome
{
    private static readonly ConditionalWeakTable<Player, Totals> ByChampion = new();

    /// <summary>Adds an earning.</summary>
    /// <param name="champion">The champion.</param>
    /// <param name="gold"><see langword="true"/> for gold, <see langword="false"/> for experience.</param>
    /// <param name="reason">The source tag.</param>
    /// <param name="amount">The amount.</param>
    public static void Add(Player champion, bool gold, string reason, long amount)
    {
        var totals = ByChampion.GetOrCreateValue(champion);
        var dictionary = gold ? totals.Gold : totals.Exp;
        dictionary.AddOrUpdate(reason, amount, (_, current) => current + amount);
    }

    /// <summary>Describes the earnings of a champion: totals and the split by source.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The text.</returns>
    public static string Describe(Player champion)
    {
        var totals = ByChampion.GetOrCreateValue(champion);
        static string Split(ConcurrentDictionary<string, long> d) => string.Join(", ", d.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value}"));
        return $"gold {totals.Gold.Values.Sum()} [{Split(totals.Gold)}] | exp {totals.Exp.Values.Sum()} [{Split(totals.Exp)}] | purse {champion.Money}";
    }

    private sealed class Totals
    {
        public ConcurrentDictionary<string, long> Gold { get; } = new();

        public ConcurrentDictionary<string, long> Exp { get; } = new();
    }
}
