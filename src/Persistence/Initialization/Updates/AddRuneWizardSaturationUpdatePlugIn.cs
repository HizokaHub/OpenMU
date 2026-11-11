// <copyright file="AddRuneWizardSaturationUpdatePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.Initialization.CharacterClasses;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// This update adds the skill Rune Phrase of Saturation (number 637) of the Rune Wizard to configurations which already have the class.
/// </summary>
[PlugIn]
[Display(Name = PlugInName, Description = PlugInDescription)]
[Guid("8C41D6A7-5E92-4B0F-A3D8-6F1E2B9C7A54")]
public class AddRuneWizardSaturationUpdatePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Add Rune Phrase of Saturation";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "This update adds the skill Rune Phrase of Saturation (number 637) of the Rune Wizard. It requires the update which adds the Rune Wizard class.";

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.AddRuneWizardSaturation;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override bool IsMandatory => false;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    protected override ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        RuneWizardClassFactory.AddTo(context, gameConfiguration, canGetCreated: true);
        RuneWizardClassFactory.AddSkillsTo(context, gameConfiguration);
        return ValueTask.CompletedTask;
    }
}
