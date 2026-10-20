// <copyright file="MobaQueueStatusPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the state of the player's search to the MOBA client. Layout:
/// <code>
/// C1 len D5 16  mode  elapsed(u16 LE)  waiting  nextIn(u16 LE, 0xFFFF = none)  nextHumans  needed  stageCount  stageCount * (seconds(u16 LE) humans)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: queue panel", Description = "Shows the wait in the queue (time, players, steps of the wait with bots).")]
[Guid("8B3D6E21-4A97-4C05-9E1F-2D7A5B0C6F38")]
public class MobaQueueStatusPlugIn : IMobaQueueStatusPlugIn
{
    private const int FixedLength = 13;
    private const int StageLength = 3;
    private const int MaxStages = 8;
    private const ushort NoNext = 0xFFFF;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaQueueStatusPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaQueueStatusPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowQueueStatusAsync(MobaQueueStatusInfo status)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var stages = status.Stages.Take(MaxStages).ToArray();
        var length = FixedLength + (stages.Length * StageLength);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = 0xD5;
            span[3] = 0x16;
            span[4] = (byte)status.Mode;
            BitConverter.TryWriteBytes(span.Slice(5, 2), (ushort)Math.Clamp(status.ElapsedSeconds, 0, ushort.MaxValue));
            span[7] = (byte)Math.Clamp(status.Waiting, 0, byte.MaxValue);
            BitConverter.TryWriteBytes(span.Slice(8, 2), status.NextInSeconds is { } next ? (ushort)Math.Clamp(next, 0, NoNext - 1) : NoNext);
            span[10] = (byte)Math.Clamp(status.NextHumans, 0, byte.MaxValue);
            span[11] = (byte)Math.Clamp(status.HumansNeeded, 0, byte.MaxValue);
            span[12] = (byte)stages.Length;
            for (var i = 0; i < stages.Length; i++)
            {
                var offset = FixedLength + (i * StageLength);
                BitConverter.TryWriteBytes(span.Slice(offset, 2), (ushort)Math.Clamp(stages[i].Seconds, 0, ushort.MaxValue));
                span[offset + 2] = (byte)Math.Clamp(stages[i].Humans, 0, byte.MaxValue);
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
