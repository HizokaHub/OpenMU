// <copyright file="MobaWallsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the wall rectangles of a middle-lane-only arena to the MOBA client. Layout:
/// <code>
/// C1 len D5 17  count  count * (x y width height)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: lane walls", Description = "Sends the rectangles of the walls which close the side lanes (1v1, ARAM).")]
[Guid("3E9B5C72-6D14-4A08-8F31-7C2A9D4E0B65")]
public class MobaWallsPlugIn : IMobaWallsPlugIn
{
    private const int HeaderLength = 5;
    private const int RectLength = 4;
    private const int MaxRects = 16;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaWallsPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaWallsPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowWallsAsync(IReadOnlyList<MobaWallRect> rects)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var list = rects.Take(MaxRects).ToArray();
        var length = HeaderLength + (list.Length * RectLength);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = 0xD5;
            span[3] = 0x17;
            span[4] = (byte)list.Length;
            for (var i = 0; i < list.Length; i++)
            {
                var offset = HeaderLength + (i * RectLength);
                span[offset] = list[i].X;
                span[offset + 1] = list[i].Y;
                span[offset + 2] = list[i].Width;
                span[offset + 3] = list[i].Height;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
