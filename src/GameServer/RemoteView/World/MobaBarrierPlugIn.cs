// <copyright file="MobaBarrierPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the pre-match barrier to the client. Layout:
/// <code>
/// C1 0A D5 13  active(u8)  radius(u8)  blueX blueY redX redY
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: pre-match barrier", Description = "Tells the client about the barrier around the bases during the countdown before a match.")]
[Guid("6E1B9D35-4A72-4C08-B3F6-0D5A8E2C7B91")]
public class MobaBarrierPlugIn : IMobaBarrierPlugIn
{
    private const int PacketLength = 10;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaBarrierPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaBarrierPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowBarrierAsync(bool active, byte radius, Point blueCenter, Point redCenter)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        int Write()
        {
            var span = connection.Output.GetSpan(PacketLength)[..PacketLength];
            span[0] = 0xC1;
            span[1] = PacketLength;
            span[2] = 0xD5;
            span[3] = 0x13;
            span[4] = (byte)(active ? 1 : 0);
            span[5] = radius;
            span[6] = blueCenter.X;
            span[7] = blueCenter.Y;
            span[8] = redCenter.X;
            span[9] = redCenter.Y;
            return PacketLength;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
