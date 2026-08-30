// <copyright file="MobaTeamChatCommandArgs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;

/// <summary>
/// Arguments for MOBA GM commands that optionally take a team ("blue" / "red").
/// </summary>
public class MobaTeamChatCommandArgs : ArgumentsBase
{
    /// <summary>
    /// Gets or sets the team name ("blue" or "red"). Optional; defaults to blue.
    /// </summary>
    [Argument("team", false)]
    public string? Team { get; set; }

    /// <summary>
    /// Parses <see cref="Team"/> into a <see cref="MobaTeam"/>, defaulting to <see cref="MobaTeam.Blue"/>.
    /// </summary>
    /// <returns>The parsed team.</returns>
    public MobaTeam ResolveTeam() => this.Team?.Trim().ToLowerInvariant() switch
    {
        "red" or "r" or "rojo" => MobaTeam.Red,
        _ => MobaTeam.Blue,
    };
}
