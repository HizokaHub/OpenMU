// <copyright file="AddRushSkillPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Updates;

using System.Runtime.InteropServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence.Initialization.CharacterClasses;
using MUnique.OpenMU.Persistence.Initialization.Skills;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// This update adds the Rush skill (MOBA: the Blade Knight closing skill brought from Season 21, where it is skill 813).
/// The skill has no cost; its cooldown and its hit are handled by the MOBA logic (<c>MobaRush</c>).
/// </summary>
[Display(Name = PlugInName, Description = PlugInDescription)]
[PlugIn]
[Guid("5C7E2A91-3B48-4F6D-9A1E-72D0B8C4E613")]
public class AddRushSkillPlugIn : UpdatePlugInBase
{
    /// <summary>
    /// The plug in name.
    /// </summary>
    internal const string PlugInName = "Add Rush Skill";

    /// <summary>
    /// The plug in description.
    /// </summary>
    internal const string PlugInDescription = "This update adds the Rush skill (number 638) of the Blade Knight and Blade Master, brought from Season 21.";

    /// <inheritdoc />
    public override string Name => PlugInName;

    /// <inheritdoc />
    public override string Description => PlugInDescription;

    /// <inheritdoc />
    public override string DataInitializationKey => VersionSeasonSix.DataInitialization.Id;

    /// <inheritdoc />
    public override UpdateVersion Version => UpdateVersion.AddRushSkill;

    /// <inheritdoc />
    public override bool IsMandatory => true;

    /// <inheritdoc />
    public override DateTime CreatedAt => new(2026, 11, 22, 23, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc />
    protected override ValueTask ApplyAsync(IContext context, GameConfiguration gameConfiguration)
    {
        const short rushNumber = (short)SkillNumber.Rush;
        if (gameConfiguration.Skills.Any(s => s.Number == rushNumber))
        {
            return ValueTask.CompletedTask;
        }

        var skill = context.CreateNew<Skill>();
        gameConfiguration.Skills.Add(skill);
        skill.Number = rushNumber;
        skill.Name = "Rush";
        skill.MovesToTarget = true;
        skill.MovesTarget = false;
        skill.AttackDamage = 0;
        skill.NumberOfHitsPerAttack = 1;
        skill.Range = 7;
        skill.DamageType = DamageType.Physical;
        skill.SkillType = SkillType.DirectHit;
        skill.Target = SkillTarget.Explicit;
        skill.TargetRestriction = SkillTargetRestriction.Undefined;
        skill.ImplicitTargetRange = 0;

        foreach (var characterClass in gameConfiguration.DetermineCharacterClasses(CharacterClasses.BladeKnightAndBladeMaster, ignoreMissing: true))
        {
            skill.QualifiedCharacters.Add(characterClass);
        }

        skill.SetGuid(skill.Number);
        return ValueTask.CompletedTask;
    }
}
