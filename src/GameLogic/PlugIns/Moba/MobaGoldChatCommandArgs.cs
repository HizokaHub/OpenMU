// <copyright file="MobaGoldChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobaoro &lt;amount&gt;</c>.</summary>
public class MobaGoldChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the gold to grant.</summary>
    [Argument("amount", false)]
    public int Amount { get; set; } = 5000;
}
