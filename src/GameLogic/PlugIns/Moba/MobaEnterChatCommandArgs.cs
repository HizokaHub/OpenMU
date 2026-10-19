// <copyright file="MobaEnterChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>
/// Arguments of <c>/moba</c>: the optional word "force" skips the entry requirements (GM test shortcut).
/// </summary>
public class MobaEnterChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the optional mode; "force" skips the level / Master check.
    /// </summary>
    [Argument("mode", false)]
    public string? Mode { get; set; }

    /// <summary>
    /// Gets a value indicating whether the entry requirements are skipped.
    /// </summary>
    public bool Force => string.Equals(this.Mode?.Trim(), "force", StringComparison.OrdinalIgnoreCase);
}
