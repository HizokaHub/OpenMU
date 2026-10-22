// <copyright file="MobaRoomListPlugIn.cs" company="MUnique">
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
/// Sends the list of the open private rooms to the MOBA client. Layout:
/// <code>
/// C2 hi lo D5 1F  open  count  count * ( id(u32 LE)  nameLen name  hostLen host  teamSize  flags(1 mid only, 2 bots, 4 locked)  players
///                                         ageSeconds(u16 LE)  idleSeconds(u16 LE) )
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: room list", Description = "Sends the list of the open private rooms to the room browser.")]
[Guid("D37F9B52-6EA4-4018-C2B9-4F8D0E1A5C76")]
public class MobaRoomListPlugIn : IMobaRoomListPlugIn
{
    private const int HeaderLength = 5;
    private const int MaxNameBytes = 72;
    private const int MaxHostBytes = 20;
    private const int MaxRooms = 60;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaRoomListPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaRoomListPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowRoomListAsync(IReadOnlyList<MobaRoomListEntry> rooms, bool open)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var entries = rooms.Take(MaxRooms).ToList();
        var body = new List<byte> { open ? (byte)1 : (byte)0, (byte)entries.Count };
        foreach (var room in entries)
        {
            body.AddRange(BitConverter.GetBytes((uint)room.Id));
            AddText(body, room.Name, MaxNameBytes);
            AddText(body, room.HostName, MaxHostBytes);
            body.Add((byte)room.TeamSize);
            body.Add((byte)((room.MidOnly ? 1 : 0) | (room.FillWithBots ? 2 : 0) | (room.Locked ? 4 : 0)));
            body.Add((byte)Math.Min(room.Players, byte.MaxValue));
            body.AddRange(BitConverter.GetBytes((ushort)Math.Clamp(room.AgeSeconds, 0, ushort.MaxValue)));
            body.AddRange(BitConverter.GetBytes((ushort)Math.Clamp(room.IdleSeconds, 0, ushort.MaxValue)));
        }

        var length = HeaderLength + body.Count;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC2;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)(length & 0xFF);
            span[3] = 0xD5;
            span[4] = 0x1F;
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
