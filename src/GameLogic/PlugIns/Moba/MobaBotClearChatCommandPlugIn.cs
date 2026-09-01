// <copyright file="MobaBotClearChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>Dev command <c>/mobabotclear</c>: removes every MOBA test bot.</summary>
[Guid("A1C4F736-8E29-4D50-9B63-2F7A0C6E5B18")]
[PlugIn]
[Display(Name = "MOBA: clear test bots", Description = "Dev command '/mobabotclear' - remove all MOBA test bots.")]
[ChatCommandHelp(Command, "Remove all MOBA test bots.", null)]
public class MobaBotClearChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/mobabotclear";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var removed = await MobaBotPlayer.ClearAllAsync().ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[mobabotclear] {removed} bot(s) eliminados.").ConfigureAwait(false);
    }
}
