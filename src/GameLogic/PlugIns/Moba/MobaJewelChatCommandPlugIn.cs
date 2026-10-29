// <copyright file="MobaJewelChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobajoya [count]</c>: drops perfection jewels (the loot of the golden goblins) next to the caller, to try them on the items of the inventory.
/// </summary>
[Guid("8E4B2A61-5C93-4D07-B1F8-29A6D03C7E15")]
[PlugIn]
[Display(Name = "MOBA: drop perfection jewels", Description = "Dev command '/mobajoya [count]'.")]
[ChatCommandHelp(Command, "Drop perfection jewels next to you: /mobajoya [count]", typeof(MobaJewelChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaJewelChatCommandPlugIn : ChatCommandPlugInBase<MobaJewelChatCommandArgs>
{
    private const string Command = "/mobajoya";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaJewelChatCommandArgs arguments)
    {
        if (!player.IsMobaClone || player.CurrentMap is not { } map)
        {
            await player.ShowBlueMessageAsync("[mobajoya] Solo dentro de una partida MOBA.").ConfigureAwait(false);
            return;
        }

        var count = Math.Clamp(arguments.Count, 1, 10);
        for (var i = 0; i < count; i++)
        {
            await MobaGoldenLoot.DropAsync(map, MobaGoldenLoot.Kind.Perfection, player.Position, player).ConfigureAwait(false);
        }
    }
}
