// <copyright file="MobaJungleChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobajungle &lt;list|reset|selupan|n&gt;</c>.</summary>
public class MobaJungleChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets what to do: <c>list</c>, <c>reset</c>, <c>selupan</c> or the number of a spot to go to.</summary>
    [Argument("what")]
    public string What { get; set; } = "list";
}
