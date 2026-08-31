// <copyright file="MobaWavesChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>
/// Arguments for <c>/mobawaves</c>: an optional interval in seconds, or "off" to stop.
/// </summary>
public class MobaWavesChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the raw argument: seconds between wave sets, "off"/"stop"/"0" to
    /// stop, or empty to toggle at the default interval.
    /// </summary>
    [Argument("interval", false)]
    public string? Interval { get; set; }
}
