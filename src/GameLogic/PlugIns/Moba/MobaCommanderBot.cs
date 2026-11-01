// <copyright file="MobaCommanderBot.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>What a bot commander decides to do.</summary>
public enum MobaCommanderBotAction
{
    /// <summary>Wait and save essence.</summary>
    Nothing,

    /// <summary>Send a pack down a lane.</summary>
    Send,

    /// <summary>Buy the next income level.</summary>
    Economy,
}

/// <summary>
/// A decision of a bot commander.
/// </summary>
/// <param name="Action">What to do.</param>
/// <param name="Unit">The pack to send (for <see cref="MobaCommanderBotAction.Send"/>).</param>
/// <param name="Lane">The lane to send it down (for <see cref="MobaCommanderBotAction.Send"/>).</param>
public readonly record struct MobaCommanderBotDecision(MobaCommanderBotAction Action, MobaCommanderUnit? Unit = null, int Lane = 0);

/// <summary>
/// The commander a team without a human one has: it invests in income early, then sends a mix of packs down open lanes.
/// A pure function of the board, so the policy is tested without a map.
/// </summary>
public static class MobaCommanderBot
{
    /// <summary>Seconds in which the bot hurries to level 2 of the income.</summary>
    public const int EarlyEconomySeconds = 150;

    /// <summary>How many seconds the bot lets pass between two decisions.</summary>
    public const int ThinkSeconds = 3;

    /// <summary>
    /// Decides what a bot commander does now.
    /// </summary>
    /// <param name="board">The board.</param>
    /// <param name="team">The team it commands.</param>
    /// <param name="elapsed">How long the match has run.</param>
    /// <param name="random">The source of randomness.</param>
    /// <param name="openLanes">The lanes which are open.</param>
    /// <returns>The decision.</returns>
    public static MobaCommanderBotDecision Decide(MobaCommanderBoard board, MobaTeam team, TimeSpan elapsed, Random random, IReadOnlyList<int> openLanes)
    {
        if (openLanes.Count == 0 || !board.Running)
        {
            return new MobaCommanderBotDecision(MobaCommanderBotAction.Nothing);
        }

        var essence = board.Essence(team);
        var level = board.IncomeLevel(team);
        if (board.NextIncomeCost(team) is { } economyCost && essence >= economyCost)
        {
            var early = elapsed.TotalSeconds < EarlyEconomySeconds && level < 2;
            if (early || (essence >= economyCost + 300 && random.NextDouble() < 0.3))
            {
                return new MobaCommanderBotDecision(MobaCommanderBotAction.Economy);
            }
        }

        MobaCommanderUnit? unit = null;
        if (essence >= MobaCommanderUnits.Elite.Cost + 150 && random.NextDouble() < 0.5)
        {
            unit = MobaCommanderUnits.Elite;
        }
        else if (essence >= MobaCommanderUnits.Brute.Cost + 100 && random.NextDouble() < 0.5)
        {
            unit = MobaCommanderUnits.Brute;
        }
        else if (essence >= MobaCommanderUnits.Soldier.Cost)
        {
            unit = MobaCommanderUnits.Soldier;
        }

        return unit is null
            ? new MobaCommanderBotDecision(MobaCommanderBotAction.Nothing)
            : new MobaCommanderBotDecision(MobaCommanderBotAction.Send, unit, openLanes[random.Next(openLanes.Count)]);
    }
}
