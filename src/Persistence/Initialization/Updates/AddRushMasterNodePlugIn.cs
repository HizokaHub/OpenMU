// <copyright file="AddRushMasterNodePlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.Initialization.Skills;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// This update makes the Rush skill (638) a master skill node of the Blade Master (MOBA, F3): middle root, rank 6, five levels
/// (the level is the skill rank). Like the skill itself (<see cref="AddRushSkillPlugIn"/>) it is brought from Season 21,
/// where Rush is a node of the Blade Master tree.
/// </summary>
[Display(Name = PlugInName, Description = PlugInDescription)]
[PlugIn]
[Guid("8E2D4F6A-1C73-4B95-A0D8-5F3E9B7C2A14")]
public class AddRushMasterNodePlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Add Rush Master Node";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "This update makes the Rush skill (number 638) a node of the Blade Master master skill tree (middle root, rank 6, 5 levels).";

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.AddRushMasterNode;

    /// <inheritdoc />
    public override bool IsMandatory => true;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 11, 23, 18, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    protected override ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        var skill = gameConfiguration.Skills.FirstOrDefault(s => s.Number == (short)SkillNumber.Rush);
        if (skill is null || skill.MasterDefinition is not null)
        {
            // Missing skill (the previous update was not applied) or already a node: nothing to do.
            return ValueTask.CompletedTask;
        }

        var root = gameConfiguration.MasterSkillRoots.First(r => r.Name == "Middle Root");
        var definition = context.CreateNew<MasterSkillDefinition>();
        definition.Root = root;
        definition.Rank = 6;
        definition.MaximumLevel = 5;
        definition.MinimumLevel = 1;
        definition.ValueFormula = "level";
        definition.DisplayValueFormula = "level";
        definition.TargetAttribute = null;
        definition.ReplacedSkill = null;
        definition.ExtendsDuration = false;
        skill.MasterDefinition = definition;
        return ValueTask.CompletedTask;
    }
}
