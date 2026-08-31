// <copyright file="MobaSkillUpChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/skillup</c>: the skill number to level.</summary>
public class MobaSkillUpChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the skill number to raise a level.</summary>
    [Argument("number")]
    public short SkillNumber { get; set; }
}
