// <copyright file="MobaCommanderPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the state of the commander panel to the MOBA client. Layout:
/// <code>
/// C1 len D5 40  flags  essence(u16 LE)  income  level  maxLevel  nextCost(u16 LE)  laneMask  unitCount  cost(u16 LE) x unitCount
///     ownCreeps x 3 (top, mid, bot)  enemyCreeps x 3
///     waveSeconds  waveInterval  sendNowCooldown  queued[lane][unit] x (3 * unitCount)
/// </code>
/// The last four fields (<c>waveSeconds</c> to <c>queued</c>) are the waiting zone of the Direct Strike way of playing: the seconds
/// left until the next wave, the seconds between two waves, the seconds left of the cooldown of "send now" and the packs which wait
/// in the base of the player's team, lane-major (top, mid, bot), <c>unitCount</c> counts per lane. Total length:
/// <c>14 + 2 * unitCount + 6 + 3 + 3 * unitCount</c>.
/// <c>flags</c>: bit 0 visible, bit 1 the player commands the team, bit 2 the command is free (a bot commands the team).
/// </summary>
[PlugIn]
[Display(Name = "MOBA: commander panel", Description = "Shows the commander panel of the commanders mode (essence, income, packs to send).")]
[Guid("7A3C9E15-2D48-4B60-8F71-C5E0A9B64D23")]
public class MobaCommanderPlugIn : IMobaCommanderPlugIn
{
    private const int HeaderLength = 14;

    private const int CostBytes = 2;

    private const int LaneCountBytes = 3;

    private const int WaveBytes = 3;

    private const byte FlagVisible = 1;

    private const byte FlagCommander = 2;

    private const byte FlagFree = 4;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaCommanderPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaCommanderPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowCommanderAsync(MobaCommanderPanelState state)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var length = PacketLength(state);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            WritePacket(span, state);
            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    /// <summary>
    /// Gets the length of the packet of a state.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The length: <c>14 + 2 * unitCount + 6 + 3 + 3 * unitCount</c>.</returns>
    internal static int PacketLength(MobaCommanderPanelState state)
    {
        var unitCount = state.UnitCosts.Count;
        return HeaderLength + (unitCount * CostBytes) + (LaneCountBytes * 2) + WaveBytes + (LaneCountBytes * unitCount);
    }

    /// <summary>
    /// Writes the packet of a state.
    /// </summary>
    /// <param name="span">The buffer; at least <see cref="PacketLength"/> bytes.</param>
    /// <param name="state">The state.</param>
    internal static void WritePacket(Span<byte> span, MobaCommanderPanelState state)
    {
        var costs = state.UnitCosts;
        var length = PacketLength(state);
        var flags = (byte)((state.Visible ? FlagVisible : 0) | (state.IsCommander ? FlagCommander : 0) | (state.CommandFree ? FlagFree : 0));
        span[..length].Clear();
        span[0] = 0xC1;
        span[1] = (byte)length;
        span[2] = 0xD5;
        span[3] = 0x40;
        span[4] = flags;
        BitConverter.TryWriteBytes(span.Slice(5, 2), (ushort)Math.Clamp(state.Essence, 0, ushort.MaxValue));
        span[7] = (byte)Math.Clamp(state.IncomePerSecond, 0, byte.MaxValue);
        span[8] = (byte)Math.Clamp(state.IncomeLevel, 0, byte.MaxValue);
        span[9] = (byte)Math.Clamp(state.MaxIncomeLevel, 0, byte.MaxValue);
        BitConverter.TryWriteBytes(span.Slice(10, 2), (ushort)Math.Clamp(state.NextIncomeCost, 0, ushort.MaxValue));
        span[12] = (byte)state.OpenLaneMask;
        span[13] = (byte)costs.Count;
        for (var i = 0; i < costs.Count; i++)
        {
            BitConverter.TryWriteBytes(span.Slice(HeaderLength + (i * CostBytes), CostBytes), (ushort)Math.Clamp(costs[i], 0, ushort.MaxValue));
        }

        var laneStart = HeaderLength + (costs.Count * CostBytes);
        for (var lane = 0; lane < LaneCountBytes; lane++)
        {
            span[laneStart + lane] = LaneCount(state.OwnLaneCreeps, lane);
            span[laneStart + LaneCountBytes + lane] = LaneCount(state.EnemyLaneCreeps, lane);
        }

        var waveStart = laneStart + (LaneCountBytes * 2);
        span[waveStart] = (byte)Math.Clamp(state.WaveSeconds, 0, byte.MaxValue);
        span[waveStart + 1] = (byte)Math.Clamp(state.WaveInterval, 0, byte.MaxValue);
        span[waveStart + 2] = (byte)Math.Clamp(state.SendNowCooldown, 0, byte.MaxValue);
        var queuedStart = waveStart + WaveBytes;
        for (var i = 0; i < LaneCountBytes * costs.Count; i++)
        {
            span[queuedStart + i] = LaneCount(state.Queued, i);
        }
    }

    private static byte LaneCount(IReadOnlyList<int> counts, int index)
        => index < counts.Count ? (byte)Math.Clamp(counts[index], 0, byte.MaxValue) : (byte)0;
}
