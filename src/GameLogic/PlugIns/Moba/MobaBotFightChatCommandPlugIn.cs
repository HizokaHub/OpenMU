// <copyright file="MobaBotFightChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobabotfight</c>: spawns one bot of every family on BOTH teams near
/// the caller, so they immediately brawl for skill / balance observation. Equivalent to
/// <c>/mobabot blue all</c> + <c>/mobabot red all</c>.
/// </summary>
[Guid("6C2E9B41-4A83-4F50-8D67-1B0A7E3C5F92")]
[PlugIn]
[Display(Name = "MOBA: bot brawl", Description = "Dev command '/mobabotfight' - spawn a full bot fight.")]
[ChatCommandHelp(Command, "Spawn one bot of every class on both teams next to you.", null)]
public class MobaBotFightChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/mobabotfight";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        var families = MobaBotChatCommandPlugIn.AllFamilies;
        var blue = await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Blue, families).ConfigureAwait(false);
        var red = await MobaBotChatCommandPlugIn.SpawnAsync(player, MobaTeam.Red, families).ConfigureAwait(false);
        await player.ShowBlueMessageAsync(
            $"[mobabotfight] {blue} azules + {red} rojos peleando en la arena ~(116,128). Mirá: /move {player.SelectedCharacter?.Name} 200 116 128 . /mobabotclear para terminar.").ConfigureAwait(false);
    }
}
