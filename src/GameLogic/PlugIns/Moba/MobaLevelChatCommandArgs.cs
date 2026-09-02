// <copyright file="MobaLevelChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobalevel &lt;level&gt;</c>.</summary>
public class MobaLevelChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the champion level to jump to (1..30).</summary>
    [Argument("level")]
    public int Level { get; set; }
}
