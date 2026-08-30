// <copyright file="MobaTeam.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The two sides of a MOBA match.
/// </summary>
public enum MobaTeam
{
    /// <summary>
    /// No team / not part of a match.
    /// </summary>
    None = 0,

    /// <summary>
    /// The blue team.
    /// </summary>
    Blue = 1,

    /// <summary>
    /// The red team.
    /// </summary>
    Red = 2,
}
