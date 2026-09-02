// <copyright file="MobaAddStatChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobaadd &lt;stat&gt; [amount]</c>.</summary>
public class MobaAddStatChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the stat to raise ("str" / "agi" / "ene" / "vit" / "cmd").</summary>
    [Argument("stat")]
    public string? Stat { get; set; }

    /// <summary>Gets or sets how many points to invest (default 1000).</summary>
    [Argument("amount", false)]
    public int Amount { get; set; } = 1000;
}
