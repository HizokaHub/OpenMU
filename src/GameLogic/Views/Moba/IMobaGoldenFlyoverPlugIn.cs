// <copyright file="IMobaGoldenFlyoverPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Starts the fly-over of the golden dragons which announces the invasion: they cross the sky of the arena spitting fire for a while (a visual only).
/// </summary>
public interface IMobaGoldenFlyoverPlugIn : IViewPlugIn
{
    /// <summary>
    /// Starts the fly-over.
    /// </summary>
    /// <param name="durationMs">How long it lasts, in milliseconds.</param>
    /// <returns>A task.</returns>
    ValueTask ShowFlyoverAsync(int durationMs);
}
