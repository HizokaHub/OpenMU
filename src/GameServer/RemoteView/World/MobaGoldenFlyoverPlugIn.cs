// <copyright file="MobaGoldenFlyoverPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Starts the fly-over of the golden dragons on the MOBA client. Layout:
/// <code>
/// C1 07 D5 2D  durationMs(u16 LE)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: golden dragons fly-over", Description = "Starts the fly-over of the golden dragons which announces the invasion.")]
[Guid("7D2B9E41-5C08-4A63-91E7-0F4A8C3B6D52")]
public class MobaGoldenFlyoverPlugIn : IMobaGoldenFlyoverPlugIn
{
    private const int Length = 7;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaGoldenFlyoverPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaGoldenFlyoverPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowFlyoverAsync(int durationMs)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var duration = (ushort)Math.Clamp(durationMs, 0, ushort.MaxValue);
        int Write()
        {
            var span = connection.Output.GetSpan(Length)[..Length];
            span[0] = 0xC1;
            span[1] = Length;
            span[2] = 0xD5;
            span[3] = 0x2D;
            span[4] = (byte)(duration & 0xFF);
            span[5] = (byte)(duration >> 8);
            span[6] = 0;
            return Length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
