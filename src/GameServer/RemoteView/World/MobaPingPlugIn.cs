// <copyright file="MobaPingPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using System.Text;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends a ping of a teammate to the MOBA client. Layout:
/// <code>
/// C1 len D5 33  kind  team  x  y  keyLo keyHi  nameLen name(UTF-8)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: ping", Description = "Tells the MOBA client a ping of a teammate (the mark on the ground and on the minimap).")]
[Guid("8A4E1C67-2B93-4F05-A6D8-7E3B9C0F5142")]
public class MobaPingPlugIn : IMobaPingPlugIn
{
    private const int MaxNameBytes = 24;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaPingPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaPingPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowPingAsync(MobaPingKind kind, byte team, byte x, byte y, ushort senderKey, string senderName)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var name = Encoding.UTF8.GetBytes(senderName ?? string.Empty);
        if (name.Length > MaxNameBytes)
        {
            name = name[..MaxNameBytes];
        }

        var length = 11 + name.Length;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = 0xD5;
            span[3] = 0x33;
            span[4] = (byte)kind;
            span[5] = team;
            span[6] = x;
            span[7] = y;
            span[8] = (byte)(senderKey & 0xFF);
            span[9] = (byte)(senderKey >> 8);
            span[10] = (byte)name.Length;
            name.CopyTo(span[11..]);
            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
