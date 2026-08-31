// <copyright file="MobaChampionStatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends the champion's MOBA level + experience + learned-skill levels in a custom
/// packet so the client HUD bar and the "+" skill-up buttons can be drawn. Layout:
/// <code>
/// C1 len D5 02  level(1)  exp(u32 LE)  nextExp(u32 LE)  skillPoints(1)  count(1)  [ skillNum(u16 LE) level(1) ] * count
/// </code>
/// nextExp 0 = at the champion level cap.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: champion state", Description = "Sends champion level + experience + skill levels for the HUD.")]
[Guid("8B4D1E27-0C6A-4F39-9D82-5A1C7E3B0F64")]
public class MobaChampionStatePlugIn : IMobaChampionStatePlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte PacketSubCode = 0x02;
    private const int HeaderLength = 15;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaChampionStatePlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaChampionStatePlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowChampionStateAsync(int level, long experience, long experienceToNextLevel, int skillPoints, IReadOnlyList<(short Number, byte Level)> skillLevels)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var exp = (uint)Math.Clamp(experience, 0, uint.MaxValue);
        var next = (uint)Math.Clamp(experienceToNextLevel, 0, uint.MaxValue);
        var count = Math.Min(skillLevels.Count, 40);
        var length = HeaderLength + 1 + (count * 3);

        int Write()
        {
            var span = connection.Output.GetSpan(length)[..length];
            span[0] = 0xC1;
            span[1] = (byte)length;
            span[2] = PacketCode;
            span[3] = PacketSubCode;
            span[4] = (byte)Math.Clamp(level, 0, 255);
            span[5] = (byte)(exp & 0xFF);
            span[6] = (byte)((exp >> 8) & 0xFF);
            span[7] = (byte)((exp >> 16) & 0xFF);
            span[8] = (byte)((exp >> 24) & 0xFF);
            span[9] = (byte)(next & 0xFF);
            span[10] = (byte)((next >> 8) & 0xFF);
            span[11] = (byte)((next >> 16) & 0xFF);
            span[12] = (byte)((next >> 24) & 0xFF);
            span[13] = (byte)Math.Clamp(skillPoints, 0, 255);
            span[14] = (byte)count;
            var offset = HeaderLength;
            for (var i = 0; i < count; i++)
            {
                var (number, lvl) = skillLevels[i];
                span[offset] = (byte)((ushort)number & 0xFF);
                span[offset + 1] = (byte)(((ushort)number >> 8) & 0xFF);
                span[offset + 2] = lvl;
                offset += 3;
            }

            return length;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
