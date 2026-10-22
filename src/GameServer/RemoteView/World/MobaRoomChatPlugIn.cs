// <copyright file="MobaRoomChatPlugIn.cs" company="MUnique">
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
/// Sends a line of the chat of the private room to the MOBA client. Layout:
/// <code>
/// C2 hi lo D5 23  kind(0 player, 1 notice)  nameLen name  textLen(u16 LE) text
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: room chat", Description = "Sends the chat lines of the private room.")]
[Guid("E48A0C63-7FB5-4129-D3CA-508E1F2B6D87")]
public class MobaRoomChatPlugIn : IMobaRoomChatPlugIn
{
    private const int HeaderLength = 5;
    private const int MaxNameBytes = 20;
    private const int MaxTextBytes = 480;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaRoomChatPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaRoomChatPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowChatLineAsync(string name, string text, bool isNotice)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var nameBytes = Encoding.UTF8.GetBytes(name);
        var nameCount = Math.Min(nameBytes.Length, MaxNameBytes);
        var textBytes = Encoding.UTF8.GetBytes(text);
        var textCount = Math.Min(textBytes.Length, MaxTextBytes);
        var length = HeaderLength + 1 + 1 + nameCount + 2 + textCount;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC2;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)(length & 0xFF);
            span[3] = 0xD5;
            span[4] = 0x23;
            var offset = HeaderLength;
            span[offset++] = isNotice ? (byte)1 : (byte)0;
            span[offset++] = (byte)nameCount;
            nameBytes.AsSpan(0, nameCount).CopyTo(span[offset..]);
            offset += nameCount;
            span[offset++] = (byte)(textCount & 0xFF);
            span[offset++] = (byte)(textCount >> 8);
            textBytes.AsSpan(0, textCount).CopyTo(span[offset..]);
            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
