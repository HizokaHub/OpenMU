// <copyright file="MobaMonsterSkillPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.RemoteView.World;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Network;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Sends a monster skill to the client (<c>ReceiveMonsterSkill</c>). Layout:
/// <code>
/// C1 09 69  skillH(u8) skillL(u8)  sourceKey(u16 LE)  targetKey(u16 LE, bit 15 = hit)
/// </code>
/// </summary>
[PlugIn]
[Display(Name = "MOBA: monster skill", Description = "Shows the skill a jungle boss casts (Selupan) with the animation of its model.")]
[Guid("8C3F6A21-5D74-4B9E-A1F0-2E7B94C60D15")]
public class MobaMonsterSkillPlugIn : IMobaMonsterSkillPlugIn
{
    private const int PacketLength = 9;

    private readonly RemotePlayer _player;

    /// <summary>Initializes a new instance of the <see cref="MobaMonsterSkillPlugIn"/> class.</summary>
    /// <param name="player">The player.</param>
    public MobaMonsterSkillPlugIn(RemotePlayer player) => this._player = player;

    /// <inheritdoc />
    public async ValueTask ShowMonsterSkillAsync(IAttacker attacker, IAttackable target, int skillNumber)
    {
        if (this._player.Connection is not { Connected: true } connection)
        {
            return;
        }

        var source = attacker.GetId(this._player);
        var targetId = (ushort)(target.GetId(this._player) | 0x8000);

        int Write()
        {
            var span = connection.Output.GetSpan(PacketLength)[..PacketLength];
            span[0] = 0xC1;
            span[1] = PacketLength;
            span[2] = 0x69;
            span[3] = (byte)((skillNumber >> 8) & 0xFF);
            span[4] = (byte)(skillNumber & 0xFF);
            span[5] = (byte)(source & 0xFF);
            span[6] = (byte)((source >> 8) & 0xFF);
            span[7] = (byte)(targetId & 0xFF);
            span[8] = (byte)((targetId >> 8) & 0xFF);
            return PacketLength;
        }

        await connection.SendAsync(Write).ConfigureAwait(false);
    }
}
