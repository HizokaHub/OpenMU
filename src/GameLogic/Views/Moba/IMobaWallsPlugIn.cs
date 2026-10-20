// <copyright file="IMobaWallsPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tells the client which rectangles of the arena are walls (the side lanes of a middle-lane-only match are closed). The client turns
/// the tiles into walls on its own terrain too and draws a wall of energy along them. Sent every second.
/// </summary>
public interface IMobaWallsPlugIn : IViewPlugIn
{
    /// <summary>
    /// Sends the walls.
    /// </summary>
    /// <param name="rects">The wall rectangles.</param>
    /// <returns>A task.</returns>
    ValueTask ShowWallsAsync(IReadOnlyList<MobaWallRect> rects);
}
