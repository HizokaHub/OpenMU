// <copyright file="MobaBannerPlugIn.cs" company="MUnique">
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
/// Sends a big banner text to the MOBA client. Layout:
/// <code>
/// C1 len D5 14  durationMs(u16 LE)  text(UTF-8)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: banner", Description = "Shows a big banner text in the upper middle of the screen (match start).")]
[Guid("1C8E5A73-9B24-4D60-A7F1-3E6D0B9C2845")]
public class MobaBannerPlugIn : IMobaBannerPlugIn
{
    private const int HeaderLength = 6;
    private const int MaxTextBytes = 120;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaBannerPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaBannerPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowBannerAsync(string text, int durationMs)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        var textLength = Math.Min(bytes.Length, MaxTextBytes);
        var duration = (ushort)Math.Clamp(durationMs, 0, ushort.MaxValue);
        var length = HeaderLength + textLength;

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = 0xD5;
            span[3] = 0x14;
            span[4] = (byte)(duration & 0xFF);
            span[5] = (byte)((duration >> 8) & 0xFF);
            bytes.AsSpan(0, textLength).CopyTo(span[HeaderLength..]);
            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
