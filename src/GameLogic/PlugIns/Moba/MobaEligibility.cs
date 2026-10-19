// <copyright file="MobaEligibility.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Entry requirements of a MOBA match: the real character must be level 400 and
/// have the Master class active (see GAMEDESIGN.md, "Elegibilidad").
/// </summary>
public static class MobaEligibility
{
    /// <summary>The minimum real character level.</summary>
    public const int RequiredLevel = 400;

    /// <summary>
    /// Checks whether a real character may enter a MOBA match.
    /// </summary>
    /// <param name="player">The session which owns the character.</param>
    /// <param name="character">The real character.</param>
    /// <returns>The reason it may not enter, or <see langword="null"/> if it is eligible.</returns>
    public static string? Check(Player player, Character character)
    {
        var level = player.Attributes is { } attributes ? (int)attributes[Stats.Level] : 0;
        return Check(level, character.CharacterClass?.IsMasterClass ?? false);
    }

    /// <summary>
    /// Checks the requirements from plain values.
    /// </summary>
    /// <param name="level">The real character level.</param>
    /// <param name="isMasterClass">Whether the character has the Master class.</param>
    /// <returns>The reason it may not enter, or <see langword="null"/> if it is eligible.</returns>
    public static string? Check(int level, bool isMasterClass)
    {
        if (level < RequiredLevel)
        {
            return $"You need level {RequiredLevel} to enter a match (you are level {level}).";
        }

        if (!isMasterClass)
        {
            return "You need the Master class (Master Skill Tree) active to enter a match.";
        }

        return null;
    }
}
