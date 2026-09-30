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
/// Also handles the option picked in a server-driven NPC menu (the MOBA shop categories):
/// <code>C1 06 D5 07  menuId  optionIndex</code>, and the recall request (key B):
/// <code>C1 04 D5 09</code>, and the minion picked for the teleport scroll:
/// <code>C1 06 D5 0B  targetId(u16 LE)</code>.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: client request handler", Description = "Handles the MOBA client requests: skill level-up (C1 D5 03) and NPC menu option picks (C1 D5 07).")]
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
        if (span.Length >= 6 && span[3] == 0x07)
        {
            // C1 06 D5 07 menuId optionIndex: an option picked in a server-driven NPC menu.
            await MobaShop.SelectOptionAsync(player, span[4], span[5]).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 6 && span[3] == 0x0B)
        {
            // C1 06 D5 0B targetId(u16 LE): the minion clicked while the teleport scroll is targeting.
            await MobaTeleport.SelectTargetAsync(player, (ushort)(span[4] | (span[5] << 8))).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 4 && span[3] is 0x0D or 0x0E)
        {
            // C1 04 D5 0D: ward (HUD slot 10); C1 04 D5 0E: sweeper (HUD slot 11).
            var message = span[3] == 0x0D ? MobaVision.TryPlaceWard(player) : MobaVision.TrySweep(player);
            await player.ShowBlueMessageAsync(message).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 4 && span[3] == 0x09)
        {
            // C1 04 D5 09: recall (key B).
            await MobaRecall.StartRecallAsync(player).ConfigureAwait(false);
            return;
        }

        if (span.Length < 6 || span[3] != 0x03)
        {
            return;
        }

        var skillNumber = (short)(span[4] | (span[5] << 8));
        MobaSkills.TryLevelUp(player, skillNumber);
        await MobaExperience.PushStateAsync(player).ConfigureAwait(false);
    }
}
