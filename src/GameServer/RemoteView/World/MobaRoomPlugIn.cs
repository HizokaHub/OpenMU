// <copyright file="MobaRoomPlugIn.cs" company="MUnique">
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
/// Sends the private room of the player to the MOBA client. Layout:
/// <code>
/// C2 hi lo D5 1B  present  isHost  fillBots  midOnly  teamSize  id(u32 LE)  nameLen name
///   blueCount (flags baseClass nameLen name)*  redCount (flags baseClass nameLen name)*       flags: bit0 host, bit1 yours
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: room panel", Description = "Shows the panel of the private room (teams, chat, host buttons).")]
[Guid("C26E8A41-5D93-4F07-B1A8-3E7C9D0F4B65")]
public class MobaRoomPlugIn : IMobaRoomPlugIn
{
    private const int HeaderLength = 5;
    private const int MaxNameBytes = 20;
    private const int MaxRoomNameBytes = 72;
    private const int MaxMembers = 12;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaRoomPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaRoomPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowRoomAsync(MobaRoomInfo? room)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var body = new List<byte>();
        if (room is null)
        {
            body.Add(0);
        }
        else
        {
            body.Add(1);
            body.Add(room.IsHost ? (byte)1 : (byte)0);
            body.Add(room.FillWithBots ? (byte)1 : (byte)0);
            body.Add(room.MidOnly ? (byte)1 : (byte)0);
            body.Add((byte)room.TeamSize);
            body.AddRange(BitConverter.GetBytes((uint)room.Id));
            AddText(body, room.Name, MaxRoomNameBytes);
            AddTeam(body, room.Blue);
            AddTeam(body, room.Red);
        }

        var length = HeaderLength + body.Count;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC2;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)(length & 0xFF);
            span[3] = 0xD5;
            span[4] = 0x1B;
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

    private static void AddTeam(List<byte> body, IReadOnlyList<MobaRoomMember> team)
    {
        var members = team.Take(MaxMembers).ToList();
        body.Add((byte)members.Count);
        foreach (var member in members)
        {
            body.Add((byte)((member.IsHost ? 1 : 0) | (member.IsMine ? 2 : 0)));
            body.Add(member.BaseClass);
            AddText(body, member.Name, MaxNameBytes);
        }
    }
}
