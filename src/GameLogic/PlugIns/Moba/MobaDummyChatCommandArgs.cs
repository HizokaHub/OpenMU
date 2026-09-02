// <copyright file="MobaDummyChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobadummy [class] [count]</c>.</summary>
public class MobaDummyChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the class alias for the dummy body ("bk", "sum", ...). Default "bk".</summary>
    [Argument("class", false)]
    public string? Class { get; set; }

    /// <summary>Gets or sets how many dummies to spawn (default 1).</summary>
    [Argument("count", false)]
    public int Count { get; set; } = 1;
}
