// <copyright file="IMobaDamageDirectionPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Tells the MOBA client where the damage it just took came from, so it can draw the red wedge around the champion.
/// </summary>
public interface IMobaDamageDirectionPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows the damage direction.
    /// </summary>
    /// <param name="attackerX">The tile X of the attacker.</param>
    /// <param name="attackerY">The tile Y of the attacker.</param>
    /// <param name="percent">The damage taken since the last notice, in percent of the maximum life (1 to 100).</param>
    /// <param name="flags">Bit 0: the damage has no direction (poison, burning...); bit 1: only the shield took it.</param>
    /// <returns>A task.</returns>
    ValueTask ShowDamageAsync(byte attackerX, byte attackerY, byte percent, byte flags);
}
