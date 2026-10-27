// <copyright file="RuneWizardClassFactory.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.CharacterClasses;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.Initialization.Skills;

/// <summary>
/// Adds the rune wizard class (imported from Season 21) to an existing game configuration.
/// </summary>
/// <remarks>
/// It is the single definition of the class. It is meant to be used by a data update plugin (configuration stored in the database,
/// for the normal game) and by a runtime shortcut which only adds the class to the configuration in memory (to see the character
/// without touching the database). The standard <see cref="CharacterClassInitialization"/> does not create it, so a freshly
/// initialized database and the existing ones stay unchanged until an update is approved.
/// </remarks>
public static class RuneWizardClassFactory
{
    /// <summary>
    /// Gets the class number of the rune wizard.
    /// </summary>
    public static byte ClassNumber => (byte)CharacterClassNumber.RuneWizard;

    /// <summary>
    /// Gets the number of the first skill which only the rune wizard has.
    /// </summary>
    public static short FirstSkillNumber => (short)SkillNumber.RuneMagicArrow;

    /// <summary>
    /// Gets the number of the last skill which only the rune wizard has.
    /// </summary>
    public static short LastSkillNumber => (short)SkillNumber.RunePhrase;

    /// <summary>
    /// Adds the rune wizard to the game configuration, if it doesn't contain it yet.
    /// </summary>
    /// <param name="context">The persistence context which creates the entities.</param>
    /// <param name="gameConfiguration">The game configuration.</param>
    /// <param name="canGetCreated">If set to <c>true</c>, players can create characters of this class.</param>
    /// <returns>The existing or newly created class.</returns>
    public static CharacterClass AddTo(IContext context, GameConfiguration gameConfiguration, bool canGetCreated = true)
    {
        if (gameConfiguration.CharacterClasses.FirstOrDefault(c => c.Number == ClassNumber) is { } existing)
        {
            return existing;
        }

        return new CharacterClassInitialization(context, gameConfiguration).CreateRuneWizard(canGetCreated);
    }

    /// <summary>
    /// Adds the skills of the rune wizard (<see cref="FirstSkillNumber"/> to <see cref="LastSkillNumber"/>) to the game
    /// configuration, if they aren't there yet. The class has to be in the configuration already.
    /// </summary>
    /// <param name="context">The persistence context which creates the entities.</param>
    /// <param name="gameConfiguration">The game configuration.</param>
    public static void AddSkillsTo(IContext context, GameConfiguration gameConfiguration)
    {
        if (gameConfiguration.Skills.Any(s => s.Number == FirstSkillNumber))
        {
            return;
        }

        new RuneWizardSkillsInitializer(context, gameConfiguration).Initialize();
    }
}
