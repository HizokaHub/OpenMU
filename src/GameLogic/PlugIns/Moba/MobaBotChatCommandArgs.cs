// <copyright file="MobaBotChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>Arguments for <c>/mobabot &lt;team&gt; &lt;class&gt; [count]</c>.</summary>
public class MobaBotChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the team ("blue" / "red").</summary>
    [Argument("team")]
    public string? Team { get; set; }

    /// <summary>Gets or sets the class alias ("rf", "sum", ...), or "all" for one of every family.</summary>
    [Argument("class")]
    public string? Class { get; set; }

    /// <summary>Gets or sets how many bots to spawn (default 1, ignored for "all").</summary>
    [Argument("count", false)]
    public int Count { get; set; } = 1;

    /// <summary>Parses <see cref="Team"/> into a <see cref="MobaTeam"/> (default red).</summary>
    /// <returns>The team.</returns>
    public MobaTeam ResolveTeam() => this.Team?.Trim().ToLowerInvariant() switch
    {
        "blue" or "b" or "azul" => MobaTeam.Blue,
        _ => MobaTeam.Red,
    };
}
