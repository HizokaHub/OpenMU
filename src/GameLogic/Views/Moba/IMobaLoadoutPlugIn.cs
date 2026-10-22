// <copyright file="IMobaLoadoutPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// What the loadout window of the client shows.
/// </summary>
/// <param name="BaseClass">The base class index of the player's character.</param>
/// <param name="MinSkills">The fewest skills a loadout has.</param>
/// <param name="MaxSkills">The most skills a loadout has.</param>
/// <param name="Selectable">The skills the player can pick from, in the order of the skill bar.</param>
/// <param name="Selected">The skills currently picked.</param>
public sealed record MobaLoadoutInfo(byte BaseClass, int MinSkills, int MaxSkills, IReadOnlyList<short> Selectable, IReadOnlyList<short> Selected);

/// <summary>
/// Opens the loadout window of the client, where the player picks the skills its champion takes into a match.
/// The window answers with <c>C1 len D5 1A count skill(u16 LE)...</c>.
/// </summary>
public interface IMobaLoadoutPlugIn : IViewPlugIn
{
    /// <summary>
    /// Opens the window.
    /// </summary>
    /// <param name="info">The skills to pick from and the current pick.</param>
    /// <returns>A task.</returns>
    ValueTask ShowLoadoutAsync(MobaLoadoutInfo info);
}
