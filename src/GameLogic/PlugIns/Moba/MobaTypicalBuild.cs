// <copyright file="MobaTypicalBuild.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// What a typical champion can have bought at each level, for the balance duels (approved 2026-11-25): the gold earned up to that level by the "Promedio" profile of
/// <c>MobaGoldEconomyReport</c> (60 % presence, 20 % last hits, 0.25 kills and 0.30 assists per minute; drops not included), spent in the buying order of the bots
/// (<see cref="MobaBotEconomy"/>). Re-run that report and copy its numbers here when the economy changes.
/// </summary>
public static class MobaTypicalBuild
{
    private static readonly int[] GoldByLevel =
    {
        0, 787, 1527, 2325, 3076, 3887, 4703, 5525, 6354, 7244,
        8140, 8986, 9895, 10811, 11791, 12720, 13713, 14655, 15662, 16677,
        17698, 18787, 21034, 23419, 25679, 28079, 30485, 33032, 35454, 38016,
    };

    /// <summary>The gold a typical champion has earned when it reaches the level.</summary>
    /// <param name="level">The champion level.</param>
    /// <returns>The gold.</returns>
    public static int GoldAt(int level) => GoldByLevel[Math.Clamp(level, 1, GoldByLevel.Length) - 1];

    /// <summary>The shop phase a typical match is in at the level (it decides which potions are sold).</summary>
    /// <param name="level">The champion level.</param>
    /// <returns>The phase.</returns>
    public static MobaShopTier PhaseAt(int level)
        => level >= MobaMatchPhase.T3LevelThreshold ? MobaShopTier.T3 : level >= MobaMatchPhase.T2LevelThreshold ? MobaShopTier.T2 : MobaShopTier.T1;
}
