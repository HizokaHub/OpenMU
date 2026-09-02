// <copyright file="MobaLevelChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev command <c>/mobalevel &lt;level&gt;</c>: jumps the caller's MOBA champion to a level,
/// granting the champion skill points and stat points it would have earned on the way, so
/// a full level-1-vs-level-30 comparison can be set up instantly. Level can go up or down.
/// </summary>
[Guid("A0F31C64-2B58-4E97-8D1A-6C0B9E7F4A25")]
[PlugIn]
[Display(Name = "MOBA: set champion level", Description = "Dev command '/mobalevel <level>'.")]
[ChatCommandHelp(Command, "Jump your MOBA champion to a level (1..30): /mobalevel <level>", typeof(MobaLevelChatCommandArgs))]
public class MobaLevelChatCommandPlugIn : ChatCommandPlugInBase<MobaLevelChatCommandArgs>
{
    private const string Command = "/mobalevel";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaLevelChatCommandArgs arguments)
    {
        if (!player.IsMobaClone || player.SelectedCharacter is not { } character)
        {
            await player.ShowBlueMessageAsync("[mobalevel] Solo dentro de una partida MOBA.").ConfigureAwait(false);
            return;
        }

        var target = Math.Clamp(arguments.Level, 1, MobaLevels.MaxLevel);
        var from = player.MobaLevel;

        if (target > from)
        {
            for (var lvl = from; lvl < target; lvl++)
            {
                player.MobaLevel++;
                player.MobaSkillPoints++;
                character.LevelUpPoints += MobaStatEconomy.PointsPerLevel(player);
            }
        }
        else if (target < from)
        {
            // Roll back: strip the points those levels would have granted (clamped at 0).
            for (var lvl = from; lvl > target; lvl--)
            {
                player.MobaLevel--;
                player.MobaSkillPoints = Math.Max(0, player.MobaSkillPoints - 1);
                character.LevelUpPoints = Math.Max(0, character.LevelUpPoints - MobaStatEconomy.PointsPerLevel(player));
            }
        }

        player.MobaExperience = 0;
        MobaProgression.ApplyLevelScaling(player);
        await MobaExperience.PushStateAsync(player).ConfigureAwait(false);

        await player.ShowBlueMessageAsync(
            $"[mobalevel] Nivel de campeón {from} → {player.MobaLevel}. skill={player.MobaSkillPoints} sin gastar, stats={character.LevelUpPoints:N0}. HP {player.Attributes?[Stats.MaximumHealth]:N0}, escala daño x{MobaProgression.DamageScale(player.MobaLevel):F1}. (/mobastats)")
            .ConfigureAwait(false);
    }
}
