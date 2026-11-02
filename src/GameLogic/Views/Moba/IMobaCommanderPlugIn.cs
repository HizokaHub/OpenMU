// <copyright file="IMobaCommanderPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// What the client's commander panel shows: the essence of the team and, for the commander, the buttons which send packs
/// down the lanes. The player is not a commander of the commanders mode when <see cref="Visible"/> is <see langword="false"/>.
/// </summary>
/// <param name="Visible">Whether the panel is shown at all.</param>
/// <param name="IsCommander">Whether the player commands the team (the buttons work).</param>
/// <param name="CommandFree">Whether nobody commands the team (a bot does) and the player may claim the command.</param>
/// <param name="Essence">The essence of the team.</param>
/// <param name="IncomePerSecond">The essence per second the team earns.</param>
/// <param name="IncomeLevel">The income level of the team.</param>
/// <param name="MaxIncomeLevel">The highest income level.</param>
/// <param name="NextIncomeCost">What the next income level costs (0 when the team is at the highest one).</param>
/// <param name="OpenLaneMask">One bit per lane (bit 0 top, 1 mid, 2 bot): the lanes which are open.</param>
/// <param name="UnitCosts">The essence each pack costs, in the order of the packs.</param>
/// <param name="OwnLaneCreeps">The live creeps of the player's team in each lane (top, mid, bot).</param>
/// <param name="EnemyLaneCreeps">The live creeps of the enemy team in each lane (top, mid, bot).</param>
/// <param name="WaveSeconds">The seconds left until the next wave (0 when there is no timer).</param>
/// <param name="WaveInterval">The seconds between two waves.</param>
/// <param name="SendNowCooldown">The seconds left until the commander can use "send now" again (0 when it is ready).</param>
/// <param name="Queued">The packs which wait in the base of the player's team, lane-major: lane 0 pack 0, lane 0 pack 1, ..., lane 1 pack 0, ...</param>
public readonly record struct MobaCommanderPanelState(
    bool Visible,
    bool IsCommander,
    bool CommandFree,
    int Essence,
    int IncomePerSecond,
    int IncomeLevel,
    int MaxIncomeLevel,
    int NextIncomeCost,
    int OpenLaneMask,
    IReadOnlyList<int> UnitCosts,
    IReadOnlyList<int> OwnLaneCreeps,
    IReadOnlyList<int> EnemyLaneCreeps,
    int WaveSeconds,
    int WaveInterval,
    int SendNowCooldown,
    IReadOnlyList<int> Queued)
{
    /// <summary>Gets the state which hides the panel.</summary>
    public static MobaCommanderPanelState Hidden { get; } = new(false, false, false, 0, 0, 0, 0, 0, 0, [], [], [], 0, 0, 0, []);
}

/// <summary>
/// Drives the client's commander panel (the commanders mode). The client answers with the order packet
/// (<c>C1 07 D5 41 action unit lane</c>).
/// </summary>
public interface IMobaCommanderPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows, updates or hides the panel.
    /// </summary>
    /// <param name="state">What the panel shows.</param>
    /// <returns>A task.</returns>
    ValueTask ShowCommanderAsync(MobaCommanderPanelState state);
}
