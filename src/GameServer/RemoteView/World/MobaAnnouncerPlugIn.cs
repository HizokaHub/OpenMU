// <copyright file="MobaAnnouncerPlugIn.cs" company="MUnique">
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
/// Sends an announcer event to the MOBA client. Layout:
/// <code>
/// C1 len D5 32  kind  team  killerLen killer(UTF-8)  victimLen victim(UTF-8)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: announcer", Description = "Tells the MOBA client the events of the match (kills, multi-kills, towers, victory) for the voice and the kill feed.")]
[Guid("5D2B7E91-3A64-4C08-B1F5-8E0A9C46D273")]
public class MobaAnnouncerPlugIn : IMobaAnnouncerPlugIn
{
    private const int HeaderLength = 4;
    private const int MaxNameBytes = 24;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaAnnouncerPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaAnnouncerPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask AnnounceAsync(MobaAnnouncement kind, byte team, string killer, string victim)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var killerBytes = TrimName(killer);
        var victimBytes = TrimName(victim);
        var length = HeaderLength + 2 + 1 + killerBytes.Length + 1 + victimBytes.Length;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = 0xD5;
            span[3] = 0x32;
            span[4] = (byte)kind;
            span[5] = team;
            span[6] = (byte)killerBytes.Length;
            killerBytes.CopyTo(span[7..]);
            var offset = 7 + killerBytes.Length;
            span[offset] = (byte)victimBytes.Length;
            victimBytes.CopyTo(span[(offset + 1)..]);
            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    private static byte[] TrimName(string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name ?? string.Empty);
        return bytes.Length <= MaxNameBytes ? bytes : bytes[..MaxNameBytes];
    }
}
