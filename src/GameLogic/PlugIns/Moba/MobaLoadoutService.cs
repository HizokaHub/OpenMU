// <copyright file="MobaLoadoutService.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Persistence;

/// <summary>
/// The loadout window of the client. It opens by itself when the champion stands in its base during the countdown before the
/// match, from the queue master's menu in town and with <c>/misskills</c>. It shows every skill the class can pick and saves what
/// the player picked; in the arena the champion's skills change on the spot. When the countdown ends the window closes and the
/// picks of that match are final.
/// </summary>
public static class MobaLoadoutService
{
    /// <summary>
    /// Opens the loadout window the first time the champion is seen in its arena during the countdown.
    /// </summary>
    /// <param name="player">The champion.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OfferAsync(Player player)
    {
        if (player.MobaState.LoadoutOffered || !CanEditInArena(player))
        {
            return;
        }

        player.MobaState.LoadoutOffered = true;
        await OpenAsync(player).ConfigureAwait(false);
    }

    /// <summary>
    /// Opens the loadout window: in town, or in the base of the arena while the match has not started.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OpenAsync(Player player)
    {
        if (!CanEditInArena(player) && !CanEditInTown(player))
        {
            await player.ShowBlueMessageAsync("[MOBA] Solo puedes elegir tus skills en Lorencia o en tu base antes de que empiece la partida.").ConfigureAwait(false);
            return;
        }

        var character = player.SelectedCharacter!;
        var classNumber = character.CharacterClass!.Number;
        var info = new MobaLoadoutInfo(
            MobaMinimapKind.BaseClassIndex(classNumber),
            MobaLoadoutStore.MinSkills,
            MobaLoadoutStore.MaxSkills,
            MobaLoadouts.SelectableFor(classNumber),
            MobaLoadoutStore.SelectedFor(character.Name, classNumber));
        await player.InvokeViewPlugInAsync<IMobaLoadoutPlugIn>(p => p.ShowLoadoutAsync(info)).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the loadout window of a champion (the countdown is over).
    /// </summary>
    /// <param name="player">The champion.</param>
    /// <returns>A task.</returns>
    public static ValueTask CloseAsync(Player player)
        => player.InvokeViewPlugInAsync<IMobaLoadoutPlugIn>(p => p.ShowLoadoutAsync(new MobaLoadoutInfo(0, 0, 0, [], [])));

    /// <summary>
    /// Saves the loadout a player picked in the window; in the arena the champion gets it on the spot.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="skills">The skill numbers picked.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SaveAsync(Player player, IReadOnlyCollection<short> skills)
    {
        var inArena = CanEditInArena(player);
        if (!inArena && !CanEditInTown(player))
        {
            await player.ShowBlueMessageAsync("[MOBA] La partida ya empezó: las skills no se pueden cambiar.").ConfigureAwait(false);
            return;
        }

        var character = player.SelectedCharacter!;
        var classNumber = character.CharacterClass!.Number;
        if (!MobaLoadoutStore.TrySave(character.Name, classNumber, skills, out var error))
        {
            await player.ShowBlueMessageAsync("[MOBA] " + error).ConfigureAwait(false);
            return;
        }

        var configuration = player.GameContext.Configuration;
        if (inArena && player.SkillList is SkillList skillList)
        {
            var wanted = skills
                .Concat(MobaLoadouts.BuffsFor(classNumber))
                .Distinct()
                .Select(n => configuration.Skills.FirstOrDefault(s => s.Number == n))
                .OfType<DataModel.Configuration.Skill>()
                .ToList();
            await skillList.ReplaceMobaPicksAsync(MobaLoadouts.SelectableFor(classNumber).ToHashSet(), wanted).ConfigureAwait(false);
        }

        var names = skills.Select(n => configuration.Skills.FirstOrDefault(s => s.Number == n)?.Name ?? $"#{n}");
        await player.ShowBlueMessageAsync($"[MOBA] Skills elegidas: {string.Join(", ", names)} (y los buffs de tu clase).").ConfigureAwait(false);
    }

    private static bool CanEditInTown(Player player)
        => !player.IsMobaClone
           && player.SelectedCharacter is { CharacterClass: not null }
           && !(player.Account is { } account && MobaMatchRegistry.IsInMatch(account.GetId()));

    private static bool CanEditInArena(Player player)
        => player.IsMobaClone
           && player is not MobaBotPlayer
           && player.SelectedCharacter is { CharacterClass: not null }
           && player.CurrentMap is { } map
           && MobaArena.Of(map).IsPreMatch;
}
