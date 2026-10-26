// <copyright file="RuneWizardPreviewHook.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Startup;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.InMemory;
using MUnique.OpenMU.Persistence.Initialization.CharacterClasses;

/// <summary>
/// Adds the rune wizard class to the configuration in memory when the server is started with the
/// <see cref="EnvironmentVariable"/> environment variable set to <c>1</c> (see <see cref="RuneWizardPreview"/>). Nothing is written to the database.
/// </summary>
internal static class RuneWizardPreviewHook
{
    /// <summary>
    /// The name of the environment variable which enables the preview.
    /// </summary>
    internal const string EnvironmentVariable = "OPENMU_RUNE_WIZARD_PREVIEW";

    /// <summary>
    /// Adds the class if the preview is enabled and the configuration doesn't contain it yet.
    /// </summary>
    /// <param name="gameConfiguration">The configuration of the game server which is about to start.</param>
    /// <param name="logger">The logger.</param>
    internal static void ApplyIfEnabled(GameConfiguration? gameConfiguration, ILogger logger)
    {
        if (gameConfiguration is null || Environment.GetEnvironmentVariable(EnvironmentVariable) != "1")
        {
            return;
        }

        if (gameConfiguration.CharacterClasses.Any(c => c.Number == RuneWizardClassFactory.ClassNumber))
        {
            return;
        }

        using var memoryContext = new InMemoryPersistenceContextProvider().CreateNewContext();
        var runeWizard = RuneWizardPreview.Apply(memoryContext, gameConfiguration);
        logger.LogWarning("Rune wizard preview: the class {Number} exists in memory only (nothing is saved to the database).", runeWizard.Number);
    }
}
