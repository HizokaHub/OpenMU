// <copyright file="MobaGoldChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobaoro [amount]</c>: gives the caller's MOBA champion gold (5,000 by default), to try the shop items.
/// </summary>
[Guid("B71D4E85-3C69-4FA8-9E2B-7D1CAF805B36")]
[PlugIn]
[Display(Name = "MOBA: grant gold", Description = "Dev command '/mobaoro [amount]'.")]
[ChatCommandHelp(Command, "Give your MOBA champion gold: /mobaoro [amount]", typeof(MobaGoldChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaGoldChatCommandPlugIn : ChatCommandPlugInBase<MobaGoldChatCommandArgs>
{
    private const string Command = "/mobaoro";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaGoldChatCommandArgs arguments)
    {
        if (!player.IsMobaClone)
        {
            await player.ShowBlueMessageAsync("[mobaoro] Solo dentro de una partida MOBA.").ConfigureAwait(false);
            return;
        }

        var amount = Math.Clamp(arguments.Amount, 1, 1_000_000);
        await MobaGold.GrantAsync(player, amount, "dev", scaleByPhase: false).ConfigureAwait(false);
        await player.ShowBlueMessageAsync($"[mobaoro] +{amount:N0} de oro.").ConfigureAwait(false);
    }
}
