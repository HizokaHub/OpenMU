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
/// </code>
/// <c>flags</c>: bit 0 visible, bit 1 the player commands the team, bit 2 the command is free (a bot commands the team).
/// </summary>
[PlugIn]
[Display(Name = "MOBA: commander panel", Description = "Shows the commander panel of the commanders mode (essence, income, packs to send).")]
[Guid("7A3C9E15-2D48-4B60-8F71-C5E0A9B64D23")]
public class MobaCommanderPlugIn : IMobaCommanderPlugIn
{
    private const int HeaderLength = 14;

    private const int CostBytes = 2;

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

        var costs = state.UnitCosts;
        var length = HeaderLength + (costs.Count * CostBytes);
        var flags = (byte)((state.Visible ? FlagVisible : 0) | (state.IsCommander ? FlagCommander : 0) | (state.CommandFree ? FlagFree : 0));

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span.Clear();
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

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
