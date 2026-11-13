// <copyright file="MobaSkillSetChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>Arguments for <c>/mobaskillset</c>: the skill (0 = every learned skill) and the rank to set.</summary>
public class MobaSkillSetChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the skill number; 0 sets every learned skill.</summary>
    [Argument("number")]
    public short SkillNumber { get; set; }

    /// <summary>Gets or sets the rank, 0 to <see cref="MobaSkills.SkillLevelCap"/>.</summary>
    [Argument("rank")]
    public int Rank { get; set; }
}

/// <summary>
/// Test command for game masters, <c>/mobaskillset &lt;number|0&gt; &lt;rank&gt;</c>: sets the rank of a learned skill (or of all of them) without spending
/// points, so the skill review (see <c>tools/bot-pruebas/receta.py</c>) can measure rank 1 and rank 5 quickly. It does not touch the unspent points.
/// </summary>
[Guid("5C8E2D14-7A39-4F60-9B21-3E6D0A7F1B94")]
[PlugIn]
[Display(Name = "MOBA: set skill rank (GM test)", Description = "'/mobaskillset <number|0> <rank>' - set the rank of a skill for testing.")]
[ChatCommandHelp(Command, "GM test: set the rank (0-5) of a learned skill, or of all of them with number 0: /mobaskillset <number|0> <rank>.", typeof(MobaSkillSetChatCommandArgs))]
public class MobaSkillSetChatCommandPlugIn : ChatCommandPlugInBase<MobaSkillSetChatCommandArgs>
{
    private const string Command = "/mobaskillset";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaSkillSetChatCommandArgs arguments)
    {
        if (!player.IsMobaClone || player.SelectedCharacter is not { } character)
        {
            await player.ShowBlueMessageAsync("[MOBA] No estás en una partida.").ConfigureAwait(false);
            return;
        }

        var rank = Math.Clamp(arguments.Rank, 0, MobaSkills.SkillLevelCap);
        var changed = 0;
        foreach (var entry in character.LearnedSkills.Where(s => s.Skill is not null && (arguments.SkillNumber == 0 || s.Skill.Number == arguments.SkillNumber)))
        {
            entry.Level = rank;
            changed++;
            if (MobaLoadouts.IsCreatureSummon((short)entry.Skill!.Number))
            {
                MobaSummons.Refresh(player);
            }
        }

        player.Logger.LogInformation("[MOBA-SKILLSET] {Name}: skill {Skill} -> rank {Rank} ({Changed} changed).", character.Name, arguments.SkillNumber, rank, changed);
        await player.ShowBlueMessageAsync($"[mobaskillset] {changed} skill(s) a rango {rank}.").ConfigureAwait(false);
    }
}
