// <copyright file="MobaCommanderUnit.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// A pack of creeps a commander can send down a lane in exchange for essence.
/// </summary>
/// <param name="Id">The short identifier the commander types.</param>
/// <param name="Name">The name for the players.</param>
/// <param name="Cost">The essence it costs.</param>
/// <param name="Description">What the pack is, for the players.</param>
/// <param name="Build">Builds the ranks of the pack for a team (each team has its own look).</param>
public sealed record MobaCommanderUnit(string Id, string Name, int Cost, string Description, Func<MobaTeam, IReadOnlyList<MobaWaveUnit>> Build);

/// <summary>
/// The packs a commander can send. Every creep fights with the flat stats of the wave creeps; the packs differ in count
/// and in how much stronger than a normal creep they are.
/// </summary>
public static class MobaCommanderUnits
{
    /// <summary>Four normal creeps.</summary>
    public static readonly MobaCommanderUnit Soldier = new(
        "soldado",
        "Soldados",
        100,
        "4 creeps normales",
        team => MobaWaveSpawner.BasicRanks(team, 2));

    /// <summary>Two tough creeps.</summary>
    public static readonly MobaCommanderUnit Brute = new(
        "bruto",
        "Brutos",
        250,
        "2 creeps con 2,5 veces la vida y 1,5 veces el daño",
        team => [new MobaWaveUnit(MobaSuperMinions.ModelOf(team), 2, 2.5f, 1.5f, 1.5f)]);

    /// <summary>One super-minion.</summary>
    public static readonly MobaCommanderUnit Elite = new(
        "elite",
        "Élite",
        600,
        "1 súper-esbirro, grande: 5 veces la vida, 2,5 veces el daño y 2 veces la defensa",
        team => [MobaWaveUnit.SuperMinion(team, 1)]);

    /// <summary>Gets every pack, the cheapest first.</summary>
    public static IReadOnlyList<MobaCommanderUnit> All { get; } = [Soldier, Brute, Elite];

    /// <summary>
    /// Finds a pack by what a commander types: its identifier, its name or its first letter.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The pack, or <see langword="null"/> if none matches.</returns>
    public static MobaCommanderUnit? Find(string? text)
    {
        var word = text?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(word))
        {
            return null;
        }

        return All.FirstOrDefault(u => u.Id == word || u.Name.Equals(word, StringComparison.OrdinalIgnoreCase) || (word.Length == 1 && u.Id[0] == word[0]));
    }
}
