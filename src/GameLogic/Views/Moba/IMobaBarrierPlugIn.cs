// <copyright file="IMobaBarrierPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Tells the client about the barrier which holds the champions inside their base circles during the countdown
/// before a match, so it can stop the walking at the wall and draw it. The server stays authoritative (the ring is
/// blocked on its own terrain too).
/// </summary>
public interface IMobaBarrierPlugIn : IViewPlugIn
{
    /// <summary>
    /// Shows or removes the barrier.
    /// </summary>
    /// <param name="active">Whether the barrier is up.</param>
    /// <param name="radius">The radius of each circle, in tiles.</param>
    /// <param name="blueCenter">The centre of the blue base circle.</param>
    /// <param name="redCenter">The centre of the red base circle.</param>
    /// <returns>A task.</returns>
    ValueTask ShowBarrierAsync(bool active, byte radius, Point blueCenter, Point redCenter);
}
