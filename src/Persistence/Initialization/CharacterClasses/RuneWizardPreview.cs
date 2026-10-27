// <copyright file="RuneWizardPreview.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.CharacterClasses;

using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Adds the rune wizard to a game configuration only in memory, to see the character in game before the data update which
/// stores the class in the database is approved.
/// </summary>
/// <remarks>
/// The class is created by <see cref="RuneWizardClassFactory"/> with a context which is never saved, and it can't be created by
/// players, so nothing reaches the database. It has its own skills (<see cref="RuneWizardClassFactory.AddSkillsTo"/>); it is still
/// allowed to wear what the dark wizard wears, because its own items (Season 21) are not imported yet.
/// </remarks>
public static class RuneWizardPreview
{
    private static readonly byte[] DarkWizardNumbers =
    {
        (byte)CharacterClassNumber.DarkWizard,
        (byte)CharacterClassNumber.SoulMaster,
        (byte)CharacterClassNumber.GrandMaster,
    };

    /// <summary>
    /// Adds the rune wizard to the configuration in memory, and gives it its own skills, and lets it wear the items of the dark wizard.
    /// </summary>
    /// <param name="memoryContext">A persistence context which is never saved (an in-memory one).</param>
    /// <param name="gameConfiguration">The game configuration.</param>
    /// <returns>The rune wizard class.</returns>
    public static CharacterClass Apply(IContext memoryContext, GameConfiguration gameConfiguration)
    {
        var runeWizard = RuneWizardClassFactory.AddTo(memoryContext, gameConfiguration, canGetCreated: false);

        RuneWizardClassFactory.AddSkillsTo(memoryContext, gameConfiguration);

        foreach (var item in gameConfiguration.Items.Where(i => i.QualifiedCharacters.Any(IsDarkWizard)))
        {
            if (!item.QualifiedCharacters.Contains(runeWizard))
            {
                item.QualifiedCharacters.Add(runeWizard);
            }
        }

        return runeWizard;
    }

    private static bool IsDarkWizard(CharacterClass characterClass) => DarkWizardNumbers.Contains(characterClass.Number);
}
