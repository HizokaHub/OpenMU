// <copyright file="MobaShieldBreakChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobasd sound [animation]</c>: plays the shield-break (SD to zero) effect candidates on the caller's client.
/// It reuses the channel packet (<c>C1 D5 09</c>) with kind 4 and the duration field carrying <c>sound | animation &lt;&lt; 4</c>.
/// </summary>
[Guid("3D8F5B21-7C94-4E60-A1B2-9F0E6D4C8A53")]
[PlugIn]
[Display(Name = "MOBA: shield break effect test", Description = "Dev command '/mobasd sound [animation]' - plays the SD-break sphere with sound 1-8 and animation 1-5.")]
[ChatCommandHelp(Command, "Plays the shield-break effect: /mobasd <sound 1-8> [animation 1-5]", null, CharacterStatus.GameMaster)]
public class MobaShieldBreakChatCommandPlugIn : IChatCommandPlugIn
{
    /// <summary>The channel kind that tells the client to play the SD-break effect.</summary>
    public const byte ChannelKindShieldBreak = 4;

    private const string Command = "/mobasd";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var args = command.Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        if (args.Length is < 1 or > 2
            || !int.TryParse(args[0], out var sound) || sound is < 1 or > 8
            || (args.Length == 2 && (!int.TryParse(args[1], out var parsed) || parsed is < 1 or > 5)))
        {
            await player.ShowBlueMessageAsync("[mobasd] Uso: /mobasd <sonido 1-8> [animación 1-5]").ConfigureAwait(false);
            return;
        }

        var animation = args.Length == 2 ? int.Parse(args[1]) : 1;
        await player.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(ChannelKindShieldBreak, sound | (animation << 4))).ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[mobasd] sonido {sound}, animación {animation}").ConfigureAwait(false);
    }
}
