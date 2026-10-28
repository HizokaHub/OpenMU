// <copyright file="RuneWizardSkills.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic;

/// <summary>
/// The skills which only the rune wizard (class imported from Season 21) has.
/// </summary>
/// <remarks>
/// They are defined by the persistence layer (<c>RuneWizardSkillsInitializer</c>); this class only tells the game logic which ones they are.
/// </remarks>
public static class RuneWizardSkills
{
    /// <summary>
    /// The number of the first skill which only the rune wizard has (Magic Arrow).
    /// </summary>
    public const short First = 630;

    /// <summary>
    /// The number of the last skill which only the rune wizard has (Rune Phrase).
    /// </summary>
    public const short Last = 636;

    /// <summary>
    /// Determines whether the skill is one of the own skills of the rune wizard.
    /// </summary>
    /// <param name="skillNumber">The skill number.</param>
    /// <returns><c>true</c> if the skill is one of the own skills of the rune wizard; otherwise, <c>false</c>.</returns>
    public static bool IsOwnSkill(short skillNumber) => skillNumber is >= First and <= Last;
}
