// <copyright file="ClassRuneWizard.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.CharacterClasses;

using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Initialization of the character class data of the rune wizard (imported from Season 21).
/// </summary>
internal partial class CharacterClassInitialization
{
    /// <summary>
    /// The bit of the character creation unlock flags (see <c>CharacterCreationUnlockFlags</c>) of the rune wizard.
    /// The existing classes use 1 (summoner), 2 (dark lord), 4 (magic gladiator) and 8 (rage fighter).
    /// </summary>
    internal const byte RuneWizardCreationAllowedFlag = 16;

    /// <summary>
    /// The level a character needs to reach before a rune wizard can get created.
    /// Provisional value: the Season 21 value is not part of the client data.
    /// </summary>
    internal const short RuneWizardLevelRequirementByCreation = 150;

    /// <summary>
    /// Creates the rune wizard.
    /// </summary>
    /// <returns>The created character class.</returns>
    /// <remarks>
    /// The class is a magician: its attack, defense, health, mana and shield formulas are the ones of the dark wizard.
    /// The base stats and the growth per level of the real class are not part of the Season 21 client files,
    /// so they are a placeholder which has to be tuned (see GAMEDESIGN.md, "Rune Wizard").
    /// </remarks>
    internal CharacterClass CreateRuneWizard()
    {
        var result = this.CreateDarkWizard(CharacterClassNumber.RuneWizard, "Rune Wizard", false, null, true);
        result.CreationAllowedFlag = RuneWizardCreationAllowedFlag;
        result.LevelRequirementByCreation = RuneWizardLevelRequirementByCreation;
        return result;
    }
}
