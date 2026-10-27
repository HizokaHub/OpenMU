// <copyright file="MobaSummonChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobainvoca</c> to try the creature of the Elf: <c>nivel N</c> sets its level at once (1-10: it grows), <c>fuerte</c> switches a test mode
/// on or off in which it cannot be hurt, hits four times harder and earns triple kill points, so it always wins its fights against the minions and the
/// jungle (send it with X and the right click) and levels up quickly.
/// </summary>
[Guid("3D5A8C17-92E4-4B60-A1F3-6E07D2B94C58")]
[PlugIn]
[Display(Name = "MOBA: test the creature of the Elf", Description = "Dev command '/mobainvoca [nivel N | fuerte]'.")]
[ChatCommandHelp(Command, "Test the creature of the Elf: /mobainvoca nivel N (1-10) or /mobainvoca fuerte (invulnerable, hits harder, triple kill points)", typeof(MobaSummonChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaSummonChatCommandPlugIn : ChatCommandPlugInBase<MobaSummonChatCommandArgs>
{
    private const string Command = "/mobainvoca";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaSummonChatCommandArgs arguments)
    {
        if (!player.IsMobaClone)
        {
            await player.ShowBlueMessageAsync("[mobainvoca] Solo dentro de una partida MOBA.").ConfigureAwait(false);
            return;
        }

        if (MobaSummons.Of(player) is null)
        {
            await player.ShowBlueMessageAsync("[mobainvoca] No tienes una invocación viva: lanza la skill de invocación de la Elfa primero.").ConfigureAwait(false);
            return;
        }

        switch (arguments.Action.Trim().ToLowerInvariant())
        {
            case "nivel":
                MobaSummons.SetLevel(player, arguments.Level);
                await player.ShowBlueMessageAsync($"[mobainvoca] Tu invocación es de nivel {player.MobaSummonLevel} (crece en ~1 s).").ConfigureAwait(false);
                break;
            case "fuerte":
                var on = MobaSummons.ToggleTesting(player);
                await player.ShowBlueMessageAsync(on
                    ? "[mobainvoca] Modo prueba: tu invocación es invulnerable, pega x4 y gana x3 puntos por kill. Mándala a pelear con X y clic derecho."
                    : "[mobainvoca] Modo prueba apagado.").ConfigureAwait(false);
                break;
            default:
                await player.ShowBlueMessageAsync("[mobainvoca] Uso: /mobainvoca nivel N (1-10)  |  /mobainvoca fuerte").ConfigureAwait(false);
                break;
        }
    }
}
