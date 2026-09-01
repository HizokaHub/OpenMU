// <copyright file="LearnSkillsChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.Views.Character;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Dev/test chat command <c>/learnskills</c>: teaches the current character its MOBA
/// loadout skills (the same list a match clone gets) at level 1, so a freshly made test
/// character (see <c>/makechar</c>) is playable and its skills show on the bar.
/// </summary>
[Guid("B5E20C71-9A4D-4F38-8C61-2D7A0B6E5F14")]
[PlugIn]
[Display(Name = "MOBA: learn loadout skills command", Description = "Dev command '/learnskills' - learn your class MOBA loadout skills.")]
[ChatCommandHelp(Command, "Learn your class MOBA loadout skills at level 1.", null)]
public class LearnSkillsChatCommandPlugIn : IChatCommandPlugIn
{
    private const string Command = "/learnskills";

    /// <inheritdoc />
    public string Key => Command;

    /// <inheritdoc />
    public CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    public async ValueTask HandleCommandAsync(Player player, string command)
    {
        if (player.IsMobaClone)
        {
            await player.ShowBlueMessageAsync("[learnskills] Salí del MOBA primero (/mobaleave).").ConfigureAwait(false);
            return;
        }

        if (player.SelectedCharacter?.CharacterClass is not { } characterClass)
        {
            return;
        }

        var config = player.GameContext.Configuration;
        var numbers = MobaLoadouts.SkillNumbersFor(characterClass);
        if (numbers.Count == 0)
        {
            await player.ShowBlueMessageAsync("[learnskills] Esta clase no tiene loadout MOBA definido.").ConfigureAwait(false);
            return;
        }

        var added = 0;
        foreach (var number in numbers)
        {
            if (player.SelectedCharacter.LearnedSkills.Any(s => s.Skill?.Number == number))
            {
                continue;
            }

            if (config.Skills.FirstOrDefault(s => s.Number == number) is not { } skill)
            {
                continue;
            }

            var entry = player.PersistenceContext.CreateNew<SkillEntry>();
            entry.Skill = skill;
            entry.Level = 1;
            player.SelectedCharacter.LearnedSkills.Add(entry);
            added++;
        }

        if (added > 0)
        {
            await player.SaveProgressAsync().ConfigureAwait(false);
            await player.InvokeViewPlugInAsync<ISkillListViewPlugIn>(p => p.UpdateSkillListAsync()).ConfigureAwait(false);
        }

        await player.ShowBlueMessageAsync($"[learnskills] {added} skill(s) aprendida(s). Si no aparecen en la barra, relogueá.").ConfigureAwait(false);
    }
}
