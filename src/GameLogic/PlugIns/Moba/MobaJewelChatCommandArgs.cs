// <copyright file="MobaJewelChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobajoya [count]</c>.</summary>
public class MobaJewelChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets how many perfection jewels to drop.</summary>
    [Argument("count", false)]
    public int Count { get; set; } = 1;
}
