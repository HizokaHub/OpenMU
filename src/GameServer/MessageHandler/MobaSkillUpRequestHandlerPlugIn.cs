// <copyright file="MobaSkillUpRequestHandlerPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameServer.MessageHandler;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Handles the client's MOBA skill-up request (the "+" button over a skill):
/// <code>C1 06 D5 03  skillNumber(u16 LE)</code>.
/// Spends one champion skill point on that skill and pushes the updated champion state.
/// Also handles the option picked in a server-driven NPC menu (the MOBA shop categories):
/// <code>C1 06 D5 07  menuId  optionIndex</code>, and the recall request (key B):
/// <code>C1 04 D5 09</code>, the fountain capture key (<code>C1 05 D5 0F on</code>), and the minion picked for the teleport scroll:
/// <code>C1 06 D5 0B  targetId(u16 LE)</code>, the results table's button (<code>C1 04 D5 18</code>) and the loadout picked in the
/// loadout window (<code>C1 len D5 1A count skill(u16 LE)...</code>).
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
            var menuId = span[4];
            var option = span[5];
            if (!await MobaQueueNpc.SelectOptionAsync(player, menuId, option).ConfigureAwait(false))
            {
                await MobaShop.SelectOptionAsync(player, menuId, option).ConfigureAwait(false);
            }

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

        if (span.Length >= 5 && span[3] == 0x0F)
        {
            // C1 05 D5 0F on: the capture key (F) of the fountains was pressed (1) or released (0); repeated while held.
            await MobaFountains.SignalAsync(player, span[4] != 0).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 5 && span[3] == 0x2A)
        {
            // C1 05 D5 2A slot: the right click of a pet or of a golden gem in the inventory (a pet is switched on or off, a gem is used).
            var itemSlot = span[4];
            if (!await MobaGoldenLoot.TryUseAsync(player, itemSlot).ConfigureAwait(false))
            {
                await MobaPets.ToggleFromInventoryAsync(player, itemSlot).ConfigureAwait(false);
            }

            return;
        }

        if (span.Length >= 7 && span[3] == 0x31)
        {
            // C1 07 D5 31 action keyLo keyHi: control a disconnected teammate (1 = take it / release it if it is the controlled one, 0 = release).
            if (span[4] == 0)
            {
                await MobaPuppet.ReleaseAsync(player, null).ConfigureAwait(false);
            }
            else
            {
                await MobaPuppet.SelectAsync(player, (ushort)(span[5] | (span[6] << 8))).ConfigureAwait(false);
            }

            return;
        }

        if (span.Length >= 9 && span[3] == 0x32)
        {
            // C1 09 D5 32 kind x y keyLo keyHi: an order for the controlled teammate (1 walk to the tile, 2 attack the object).
            await MobaPuppet.OrderAsync(player, span[4], span[5], span[6], (ushort)(span[7] | (span[8] << 8))).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 10 && span[3] == 0x33)
        {
            // C1 0A D5 33 skillLo skillHi keyLo keyHi x y: a skill of the controlled teammate, on an object (key) or on a tile.
            await MobaPuppet.CastAsync(player, (short)(span[4] | (span[5] << 8)), (ushort)(span[6] | (span[7] << 8)), span[8], span[9]).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 8 && span[3] == 0x36)
        {
            // C1 08 D5 36 build(u32 LE): the build number of the client (see MobaClientBuild); sent right after connecting.
            var build = BitConverter.ToUInt32(span.Slice(4, 4));
            MobaClientBuild.Report(player, build);
            player.Logger.LogInformation("[MOBA-CLIENT] build {Build} (minimum {Minimum})", build, MobaClientBuild.Minimum);
            return;
        }

        if (span.Length >= 7 && span[3] == 0x35)
        {
            // C1 07 D5 35 kind x y: a ping of the champion (see MobaPing; the team sees it).
            await MobaPing.HandleAsync(player, span[4], span[5], span[6]).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 4 && span[3] == 0x34)
        {
            // C1 04 D5 34: the recall (key B) of the controlled teammate.
            await MobaPuppet.RecallAsync(player).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 5 && span[3] == 0x27)
        {
            // C1 05 D5 27 mode: the keys of the summon: 1 = command it (X), 0 = release it where it stands (X again), 2 = call it back (Z).
            await MobaSummons.SetControlAsync(player, span[4]).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 8 && span[3] == 0x38)
        {
            // C1 08 D5 38 slot keyLo keyHi: a skill of the summon (key 1 or 2 of the control mode) on the object under the cursor (0xFFFF = none).
            await MobaSummons.CastSkillAsync(player, span[4], (ushort)(span[5] | (span[6] << 8))).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 9 && span[3] == 0x28)
        {
            // C1 09 D5 28 kind x y keyLo keyHi: an order for the summon (1 walk to the tile, 2 attack the object).
            await MobaSummons.OrderAsync(player, span[4], span[5], span[6], (ushort)(span[7] | (span[8] << 8))).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 5 && span[3] == 0x1A)
        {
            // C1 len D5 1A count skill(u16 LE)...: the loadout picked in the loadout window.
            var count = span[4];
            if (span.Length >= 5 + (count * 2))
            {
                var skills = new List<short>(count);
                for (var i = 0; i < count; i++)
                {
                    skills.Add((short)(span[5 + (i * 2)] | (span[6 + (i * 2)] << 8)));
                }

                await MobaLoadoutService.SaveAsync(player, skills).ConfigureAwait(false);
            }

            return;
        }

        if (span.Length >= 5 && span[3] == 0x1C)
        {
            // C1 05 D5 1C action: a button of the private room panel (1 change team, 2 bots, 3 start, 4 leave).
            await MobaRooms.HandleActionAsync(player, span[4]).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 4 && span[3] == 0x1E)
        {
            // C1 04 D5 1E: the room browser asks for a fresh list of rooms.
            await MobaRooms.SendListAsync(player, false).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 6 && span[3] == 0x20)
        {
            // C1 len D5 20 teamSize flags nameLen name passLen pass: create a room (flags: 1 middle lane only, 2 bots).
            var nameLength = span.Length >= 7 ? Math.Min((int)span[6], span.Length - 7) : 0;
            var roomName = nameLength > 0 ? System.Text.Encoding.UTF8.GetString(span.Slice(7, nameLength)) : null;
            var passOffset = 7 + nameLength;
            var passLength = span.Length > passOffset ? Math.Min((int)span[passOffset], span.Length - passOffset - 1) : 0;
            var roomPassword = passLength > 0 ? System.Text.Encoding.UTF8.GetString(span.Slice(passOffset + 1, passLength)) : null;
            var answer = await MobaRooms.CreateAsync(player, roomName, span[4], (span[5] & 1) != 0, (span[5] & 2) != 0, roomPassword).ConfigureAwait(false);
            await player.ShowBlueMessageAsync("[Sala] " + answer).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 8 && span[3] == 0x21)
        {
            // C1 len D5 21 id(u32 LE) passLen pass: join a room.
            var roomId = BitConverter.ToInt32(span.Slice(4, 4));
            var joinPassLength = span.Length > 8 ? Math.Min((int)span[8], span.Length - 9) : 0;
            var joinPassword = joinPassLength > 0 ? System.Text.Encoding.UTF8.GetString(span.Slice(9, joinPassLength)) : null;
            await player.ShowBlueMessageAsync("[Sala] " + await MobaRooms.JoinAsync(player, roomId, joinPassword).ConfigureAwait(false)).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 6 && span[3] == 0x22)
        {
            // C1 len D5 22 action nameLen name: a host button which acts on another player (1 move, 2 kick, 3 make host).
            var targetLength = Math.Min((int)span[5], span.Length - 6);
            await MobaRooms.HandleTargetActionAsync(player, span[4], System.Text.Encoding.UTF8.GetString(span.Slice(6, targetLength))).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 5 && span[3] == 0x24)
        {
            // C1 len D5 24 textLen text: a line of the room chat.
            var textLength = Math.Min((int)span[4], span.Length - 5);
            await MobaRooms.ChatAsync(player, System.Text.Encoding.UTF8.GetString(span.Slice(5, textLength))).ConfigureAwait(false);
            return;
        }

        if (span.Length >= 4 && span[3] == 0x18)
        {
            // C1 04 D5 18: the button of the results table (back to town).
            await MobaMatchEnder.ReturnToTownAsync(player).ConfigureAwait(false);
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
