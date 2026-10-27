// <copyright file="MobaSummonChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobainvoca [nivel N | fuerte]</c>.</summary>
public class MobaSummonChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets what to do: <c>nivel</c> or <c>fuerte</c>.</summary>
    [Argument("action", false)]
    public string Action { get; set; } = string.Empty;

    /// <summary>Gets or sets the level for <c>nivel</c> (1-10).</summary>
    [Argument("level", false)]
    public int Level { get; set; } = 10;
}
