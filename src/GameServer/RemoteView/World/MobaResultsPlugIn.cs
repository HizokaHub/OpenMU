// <copyright file="MobaResultsPlugIn.cs" company="MUnique">
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
/// Sends the results of a finished match to the MOBA client. Layout:
/// <code>
/// C2 hi lo D5 18  winner(1 blue, 2 red)  duration(u32 LE)  secondsToReturn(u16 LE)  count
///   count * ( team  baseClass  flags(bit0 yours, bit1 bot)  level  kills(u16)  deaths(u16)  assists(u16)  creeps(u16)
///             gold(u32)  damageDealt(u32)  damageTaken(u32)  nameLength  name(UTF-8) )
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: results panel", Description = "Shows the results table at the end of a match.")]
[Guid("5D2B9E64-1F83-4A07-B6C1-7E4A0D8F3921")]
public class MobaResultsPlugIn : IMobaResultsPlugIn
{
    private const int HeaderLength = 5;
    private const int FixedLength = HeaderLength + 1 + 4 + 2 + 1;
    private const int RowFixedLength = 3 + 1 + (4 * 2) + (3 * 4) + 1;
    private const int MaxRows = 12;
    private const int MaxNameBytes = 20;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaResultsPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaResultsPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowResultsAsync(MobaMatchSummary summary, string yourName, int secondsToReturn)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var rows = summary.Rows.Take(MaxRows).Select(r => (Row: r, Name: Truncate(r.Name))).ToList();
        var length = FixedLength + rows.Sum(r => RowFixedLength + r.Name.Length);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC2;
            span[1] = (byte)(length >> 8);
            span[2] = (byte)(length & 0xFF);
            span[3] = 0xD5;
            span[4] = 0x18;
            var offset = HeaderLength;
            span[offset++] = (byte)summary.Winner;
            BitConverter.TryWriteBytes(span.Slice(offset, 4), (uint)Math.Max(0, summary.DurationSeconds));
            offset += 4;
            BitConverter.TryWriteBytes(span.Slice(offset, 2), (ushort)Math.Clamp(secondsToReturn, 0, ushort.MaxValue));
            offset += 2;
            span[offset++] = (byte)rows.Count;
            foreach (var (row, name) in rows)
            {
                span[offset++] = (byte)row.Team;
                span[offset++] = row.BaseClass;
                span[offset++] = (byte)((string.Equals(row.Name, yourName, StringComparison.Ordinal) ? 1 : 0) | (row.IsBot ? 2 : 0));
                span[offset++] = (byte)Math.Clamp(row.Level, 0, byte.MaxValue);
                offset = WriteU16(span, offset, row.Kills);
                offset = WriteU16(span, offset, row.Deaths);
                offset = WriteU16(span, offset, row.Assists);
                offset = WriteU16(span, offset, row.CreepScore);
                offset = WriteU32(span, offset, row.Gold);
                offset = WriteU32(span, offset, row.DamageDealt);
                offset = WriteU32(span, offset, row.DamageTaken);
                span[offset++] = (byte)name.Length;
                name.CopyTo(span[offset..]);
                offset += name.Length;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }

    private static int WriteU16(Span<byte> span, int offset, int value)
    {
        BitConverter.TryWriteBytes(span.Slice(offset, 2), (ushort)Math.Clamp(value, 0, ushort.MaxValue));
        return offset + 2;
    }

    private static int WriteU32(Span<byte> span, int offset, long value)
    {
        BitConverter.TryWriteBytes(span.Slice(offset, 4), (uint)Math.Clamp(value, 0, uint.MaxValue));
        return offset + 4;
    }

    private static byte[] Truncate(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        return bytes.Length <= MaxNameBytes ? bytes : bytes[..MaxNameBytes];
    }
}
