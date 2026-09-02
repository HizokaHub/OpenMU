// <copyright file="MobaSkillCooldownPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends a "skill went on cooldown" hint for the MOBA HUD. Layout:
/// <code>
/// C1 0A D5 04  skillNum(u16 LE)  durationMs(u16 LE)  graceMs(u16 LE)
/// </code>
/// graceMs = the window the skill stays castable before the cooldown starts.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: skill cooldown", Description = "Notifies the client that a champion ability went on its per-match cooldown.")]
[Guid("D6A2F715-3E48-4C0B-9F1D-7C2E5B84A093")]
public class MobaSkillCooldownPlugIn : IMobaSkillCooldownPlugIn
{
    private const byte PacketCode = 0xD5;
    private const byte PacketSubCode = 0x04;
    private const int PacketLength = 10;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaSkillCooldownPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaSkillCooldownPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowSkillCooldownAsync(short skillNumber, int durationMs, int graceMs)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var number = (ushort)skillNumber;
        var duration = (ushort)Math.Clamp(durationMs, 0, ushort.MaxValue);
        var grace = (ushort)Math.Clamp(graceMs, 0, ushort.MaxValue);

        int Write()
        {
            var span = connection.Output.GetSpan(PacketLength)[..PacketLength];
            span[0] = 0xC1;
            span[1] = PacketLength;
            span[2] = PacketCode;
            span[3] = PacketSubCode;
            span[4] = (byte)(number & 0xFF);
            span[5] = (byte)((number >> 8) & 0xFF);
            span[6] = (byte)(duration & 0xFF);
            span[7] = (byte)((duration >> 8) & 0xFF);
            span[8] = (byte)(grace & 0xFF);
            span[9] = (byte)((grace >> 8) & 0xFF);
            return PacketLength;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
