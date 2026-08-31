// <copyright file="MobaSkillsChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Chat command <c>/skills</c>: lists the champion's learned skills with their level
/// and the unspent champion skill points.
/// </summary>
[Guid("9E3C1A75-4B62-4D09-8F27-1A5D6B0E4C38")]
[PlugIn]
[Display(Name = "MOBA: list skills command", Description = "'/skills' - show your MOBA skills, their level and unspent points.")]
[ChatCommandHelp(Command, "List your MOBA skills and unspent skill points.", typeof(EmptyChatCommandArgs))]
public class MobaSkillsChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string Command = "/skills";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
    {
        if (!player.IsMobaClone || player.SelectedCharacter is not { } character)
        {
            await player.ShowBlueMessageAsync("[MOBA] No estás en una partida.").ConfigureAwait(false);
            return;
        }

        await player.ShowBlueMessageAsync($"[MOBA] Nivel {player.MobaLevel} - puntos de habilidad sin gastar: {player.MobaSkillPoints}").ConfigureAwait(false);
        foreach (var entry in character.LearnedSkills.Where(s => s.Skill is not null).OrderBy(s => s.Skill!.Number))
        {
            await player.ShowBlueMessageAsync($"  #{entry.Skill!.Number} {entry.Skill.Name} - nivel {entry.Level}/{MobaSkills.SkillLevelCap}").ConfigureAwait(false);
        }
    }
}
