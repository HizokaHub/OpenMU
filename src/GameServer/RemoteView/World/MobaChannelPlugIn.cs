// <copyright file="MobaChannelPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the channel bar state (recall / teleport) to the MOBA HUD. Layout:
/// <code>
/// C1 07 D5 09  kind(u8)  durationMs(u16 LE)     (durationMs 0 = cancel)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: channel bar", Description = "Notifies the client that the champion started or stopped channelling (recall).")]
[Guid("5B7E2C49-1A36-4F80-9D52-6E8A0C3F71B4")]
public class MobaChannelPlugIn : IMobaChannelPlugIn
{
    private const int PacketLength = 7;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaChannelPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaChannelPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowChannelAsync(byte kind, int durationMs)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var duration = (ushort)Math.Clamp(durationMs, 0, ushort.MaxValue);

        int Write()
        {
            var span = connection.Output.GetSpan(PacketLength)[..PacketLength];
            span[0] = 0xC1;
            span[1] = PacketLength;
            span[2] = 0xD5;
            span[3] = 0x09;
            span[4] = kind;
            span[5] = (byte)(duration & 0xFF);
            span[6] = (byte)((duration >> 8) & 0xFF);
            return PacketLength;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
