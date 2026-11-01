// <copyright file="MobaModeChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>
/// Arguments for <c>/mobamode</c>: an optional mode identifier, or "list".
/// </summary>
public class MobaModeChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the raw argument: the identifier of the mode to set, "list" to list them, or empty to show the current one.
    /// </summary>
    [Argument("mode", false)]
    public string? Mode { get; set; }
}
