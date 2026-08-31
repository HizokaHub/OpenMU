// <copyright file="IMobaChampionStatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Sends the client the local champion's MOBA-match state (level + experience toward
/// the next level), so the HUD experience bar can reflect the match progression instead
/// of the MU character experience.
/// </summary>
public interface IMobaChampionStatePlugIn : IViewPlugIn
{
    /// <summary>
    /// Pushes the current champion level and experience.
    /// </summary>
    /// <param name="level">The champion level (1..30).</param>
    /// <param name="experience">Experience accumulated toward the next level.</param>
    /// <param name="experienceToNextLevel">Experience needed for the next level (0 at the cap).</param>
    /// <param name="skillPoints">Unspent champion skill points.</param>
    ValueTask ShowChampionStateAsync(int level, long experience, long experienceToNextLevel, int skillPoints);
}
