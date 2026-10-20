// <copyright file="MobaReadyCheckPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the state of the "match found" popup to the MOBA client. Layout:
/// <code>
/// C1 0F D5 15  status  remainingMs(u32 LE)  windowMs(u32 LE)  accepted  total
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: ready check popup", Description = "Shows the 'match found' popup of the queue (countdown, accepted count, accept / decline).")]
[Guid("5D2F7B14-8C36-4E71-B9A0-4F1E6C3D8A52")]
public class MobaReadyCheckPlugIn : IMobaReadyCheckPlugIn
{
    private const int Length = 15;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaReadyCheckPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaReadyCheckPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowReadyCheckAsync(MobaReadyCheckStatus status, int remainingMs, int windowMs, int accepted, int total)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var remaining = (uint)Math.Max(0, remainingMs);
        var window = (uint)Math.Max(0, windowMs);
        var acceptedCount = (byte)Math.Clamp(accepted, 0, byte.MaxValue);
        var totalCount = (byte)Math.Clamp(total, 0, byte.MaxValue);

        int Write()
        {
            var span = connection.Output.GetSpan(Length)[..Length];
            span[0] = 0xC1;
            span[1] = Length;
            span[2] = 0xD5;
            span[3] = 0x15;
            span[4] = (byte)status;
            BitConverter.TryWriteBytes(span.Slice(5, 4), remaining);
            BitConverter.TryWriteBytes(span.Slice(9, 4), window);
            span[13] = acceptedCount;
            span[14] = totalCount;
            return Length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
