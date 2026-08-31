// <copyright file="MobaSkillUpChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Chat command <c>/skillup &lt;number&gt;</c>: spends one champion skill point to raise a
/// learned skill by a level (cap <see cref="MobaSkills.SkillLevelCap"/>). Interim UI
/// until the in-client skill window gets + buttons.
/// </summary>
[Guid("2D7B9F41-6E0C-4A38-8B15-9C4E1A6D3F82")]
[PlugIn]
[Display(Name = "MOBA: skill up command", Description = "'/skillup <number>' - spend a champion skill point on a skill.")]
[ChatCommandHelp(Command, "Spend a champion skill point to level a learned skill: /skillup <number>.", typeof(MobaSkillUpChatCommandArgs))]
public class MobaSkillUpChatCommandPlugIn : ChatCommandPlugInBase<MobaSkillUpChatCommandArgs>
{
    private const string Command = "/skillup";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaSkillUpChatCommandArgs arguments)
    {
        var result = MobaSkills.TryLevelUp(player, arguments.SkillNumber);
        var entry = player.SelectedCharacter?.LearnedSkills.FirstOrDefault(s => s.Skill?.Number == arguments.SkillNumber);

        var message = result switch
        {
            MobaSkills.SkillUpResult.Ok => $"[MOBA] {entry?.Skill?.Name} subió a nivel {entry?.Level}/{MobaSkills.SkillLevelCap} ({player.MobaSkillPoints} puntos restantes).",
            MobaSkills.SkillUpResult.NoPoints => "[MOBA] No tenés puntos de habilidad sin gastar.",
            MobaSkills.SkillUpResult.SkillNotLearned => $"[MOBA] No aprendiste la habilidad #{arguments.SkillNumber}. Usá /skills para ver las tuyas.",
            MobaSkills.SkillUpResult.AtCap => $"[MOBA] Esa habilidad ya está al máximo ({MobaSkills.SkillLevelCap}).",
            _ => "[MOBA] No estás en una partida.",
        };

        await player.ShowBlueMessageAsync(message).ConfigureAwait(false);
    }
}
