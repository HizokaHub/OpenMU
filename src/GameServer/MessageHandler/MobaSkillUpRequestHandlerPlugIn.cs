// <copyright file="MobaSkillUpRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the client's MOBA skill-up request (the "+" button over a skill):
/// <code>C1 06 D5 03  skillNumber(u16 LE)</code>.
/// Spends one champion skill point on that skill and pushes the updated champion state.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: skill-up request handler", Description = "Handles the client's '+' skill level-up request (C1 D5 03).")]
[Guid("4C1E9A38-7B25-4D60-8F14-2A9C6E0B3D57")]
internal class MobaSkillUpRequestHandlerPlugIn : IPacketHandlerPlugIn
{
    /// <inheritdoc />
    public bool IsEncryptionExpected => false;

    /// <inheritdoc />
    public byte Key => 0xD5;

    /// <inheritdoc />
    public async ValueTask HandlePacketAsync(Player player, Memory<byte> packet)
    {
        var span = packet.Span;
        if (span.Length < 6 || span[3] != 0x03)
        {
            return;
        }

        var skillNumber = (short)(span[4] | (span[5] << 8));
        MobaSkills.TryLevelUp(player, skillNumber);
        await MobaExperience.PushStateAsync(player).ConfigureAwait(false);
    }
}
