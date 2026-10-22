// <copyright file="MobaRoomInvitePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the invitation to a private room to the MOBA client. Layout:
/// <code>
/// C2 hi lo D5 26  id(u32 LE)  seconds(u8)  roomNameLen roomName  fromLen from
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: room invitation", Description = "Shows the invitation to a private room.")]
[Guid("F59B1D74-8AC6-423A-E4DB-619F2A3C7E98")]
public class MobaRoomInvitePlugIn : IMobaRoomInvitePlugIn
{
    private const int HeaderLength = 5;
    private const int MaxRoomNameBytes = 72;
    private const int MaxFromBytes = 20;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaRoomInvitePlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaRoomInvitePlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowInviteAsync(int roomId, string roomName, string fromName, int seconds)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var body = new List<byte>();
        body.AddRange(BitConverter.GetBytes((uint)roomId));
        body.Add((byte)Math.Clamp(seconds, 1, 255));
        AddText(body, roomName, MaxRoomNameBytes);
        AddText(body, fromName, MaxFromBytes);
        var length = HeaderLength + body.Count;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC2;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)(length & 0xFF);
            span[3] = 0xD5;
            span[4] = 0x26;
            body.CopyTo(span[HeaderLength..]);
            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    private static void AddText(List<byte> body, string text, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var count = Math.Min(bytes.Length, maxBytes);
        body.Add((byte)count);
        body.AddRange(bytes.AsSpan(0, count).ToArray());
    }
}
