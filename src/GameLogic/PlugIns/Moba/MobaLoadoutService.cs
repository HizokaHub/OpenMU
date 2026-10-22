// <copyright file="MobaLoadoutService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// The loadout window of the client: opens it with the skills of the player's class and saves what the player picked.
/// </summary>
public static class MobaLoadoutService
{
    /// <summary>
    /// Opens the loadout window for a player in town.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OpenAsync(Player player)
    {
        if (player.IsMobaClone || player.SelectedCharacter is not { CharacterClass: { } characterClass } character)
        {
            await player.ShowBlueMessageAsync("[MOBA] Elige tus skills en Lorencia, antes de buscar partida.").ConfigureAwait(false);
            return;
        }

        var selectable = MobaLoadouts.SelectableFor(characterClass.Number);
        if (selectable.Count == 0)
        {
            await player.ShowBlueMessageAsync("[MOBA] Tu clase no tiene skills elegibles.").ConfigureAwait(false);
            return;
        }

        var selected = MobaLoadoutStore.SelectedFor(character.Name, characterClass.Number);
        var info = new MobaLoadoutInfo(MobaMinimapKind.BaseClassIndex(characterClass.Number), MobaLoadoutStore.MinSkills, MobaLoadoutStore.MaxSkills, selectable, selected);
        await player.InvokeViewPlugInAsync<IMobaLoadoutPlugIn>(p => p.ShowLoadoutAsync(info)).ConfigureAwait(false);
    }

    /// <summary>
    /// Saves the loadout a player picked in the window.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="skills">The skill numbers picked.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SaveAsync(Player player, IReadOnlyCollection<short> skills)
    {
        if (player.IsMobaClone || player.SelectedCharacter is not { CharacterClass: { } characterClass } character)
        {
            return;
        }

        if (!MobaLoadoutStore.TrySave(character.Name, characterClass.Number, skills, out var error))
        {
            await player.ShowBlueMessageAsync("[MOBA] " + error).ConfigureAwait(false);
            return;
        }

        var names = skills
            .Select(n => player.GameContext.Configuration.Skills.FirstOrDefault(s => s.Number == n)?.Name ?? $"#{n}")
            .ToList();
        await player.ShowBlueMessageAsync($"[MOBA] Skills guardadas ({skills.Count}): {string.Join(", ", names)}.").ConfigureAwait(false);
    }
}
