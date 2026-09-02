// <copyright file="IMobaSkillCooldownPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Tells the client that a champion ability just went on its per-match cooldown, so the
/// HUD can draw the sweep over that skill's bar slot and grey it out until it is ready.
/// The server stays authoritative - this is purely a visual hint sent on a successful cast.
/// </summary>
public interface IMobaSkillCooldownPlugIn : IViewPlugIn
{
    /// <summary>
    /// Pushes the cooldown that was just started for a skill.
    /// </summary>
    /// <param name="skillNumber">The Persistence skill number.</param>
    /// <param name="durationMs">The full cooldown duration in milliseconds.</param>
    /// <param name="graceMs">
    /// The grace window in milliseconds: the skill stays castable for this long after the
    /// cast, and the cooldown only starts once it elapses.
    /// </param>
    ValueTask ShowSkillCooldownAsync(short skillNumber, int durationMs, int graceMs);
}
