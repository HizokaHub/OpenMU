// <copyright file="RuneWizardSkillsInitializer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.Skills;

using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Attributes;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.Persistence.Initialization.CharacterClasses;

/// <summary>
/// Creates the skills of the rune wizard (Season 21), which only this class can learn. The numbers are in the 630s because the
/// client tables hold 650 skills. The values are a first pass (based on the Season 6 skills which behave the same way).
/// </summary>
internal class RuneWizardSkillsInitializer : SkillsInitializerBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RuneWizardSkillsInitializer"/> class.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="gameConfiguration">The game configuration.</param>
    public RuneWizardSkillsInitializer(IContext context, GameConfiguration gameConfiguration)
        : base(context, gameConfiguration)
    {
    }

    /// <inheritdoc/>
    public override void Initialize()
    {
        const CharacterClasses rw = CharacterClasses.RuneWizard;

        // Magic Arrow: a bolt which also hurts the ones next to the target.
        this.CreateSkill(SkillNumber.RuneMagicArrow, "Magic Arrow", rw, DamageType.Wizardry, 20, 6, manaConsumption: 4, energyRequirement: 30, skillType: SkillType.AreaSkillAutomaticHits);
        this.AddAreaSkillSettings(SkillNumber.RuneMagicArrow, false, default, default, default, true, TimeSpan.Zero, TimeSpan.FromMilliseconds(500), 0, 2, default, default, 0.5f, targetAreaDiameter: 2, useTargetAreaFilter: true);

        // Plasma Ball: rune energy which travels in the targeted direction.
        this.CreateSkill(SkillNumber.RunePlasmaBall, "Plasma Ball", rw, DamageType.Wizardry, 38, 6, manaConsumption: 30, energyRequirement: 120, elementalModifier: ElementalType.Lightning, skillType: SkillType.AreaSkillAutomaticHits);
        this.AddAreaSkillSettings(SkillNumber.RunePlasmaBall, true, 1.5f, 1.5f, 5f, true, TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(1000), 0, 2, default, default, 0.7f);

        // Lightning Storm: rune energy thrown to the sky which falls on a circle of the ground.
        this.CreateSkill(SkillNumber.RuneLightningStorm, "Lightning Storm", rw, DamageType.Wizardry, 70, 6, manaConsumption: 80, energyRequirement: 300, elementalModifier: ElementalType.Lightning, skillType: SkillType.AreaSkillAutomaticHits);
        this.AddAreaSkillSettings(SkillNumber.RuneLightningStorm, false, default, default, default, true, TimeSpan.Zero, TimeSpan.Zero, targetAreaDiameter: 3, useTargetAreaFilter: true);

        // Burst and Haste: the two buffs.
        this.CreateSkill(SkillNumber.RuneBurst, "Burst", rw, distance: 6, abilityConsumption: 30, manaConsumption: 90, energyRequirement: 200, skillType: SkillType.Buff, targetRestriction: SkillTargetRestriction.Self);
        this.CreateSkill(SkillNumber.RuneHaste, "Haste", rw, distance: 6, abilityConsumption: 30, manaConsumption: 80, energyRequirement: 200, skillType: SkillType.Buff, targetRestriction: SkillTargetRestriction.Party);

        // Runic Spear: explosive rune energy on the target and around it.
        this.CreateSkill(SkillNumber.RuneRunicSpear, "Runic Spear", rw, DamageType.Wizardry, 55, 5, manaConsumption: 45, energyRequirement: 220, skillType: SkillType.AreaSkillAutomaticHits);
        this.AddAreaSkillSettings(SkillNumber.RuneRunicSpear, false, default, default, default, targetAreaDiameter: 2, useTargetAreaFilter: true);

        // Rune Phrase: runes with explosive magic, through an orb, on many targets.
        this.CreateSkill(SkillNumber.RunePhrase, "Rune Phrase", rw, DamageType.Wizardry, 95, 6, manaConsumption: 120, energyRequirement: 400, skillType: SkillType.AreaSkillAutomaticHits);
        this.AddAreaSkillSettings(SkillNumber.RunePhrase, false, default, default, default, true, TimeSpan.Zero, TimeSpan.Zero, targetAreaDiameter: 3, useTargetAreaFilter: true);

        this.CreateBurstEffect();
        this.CreateHasteEffect();
    }

    private void CreateBurstEffect()
    {
        var effect = this.CreateEffect(MagicEffectNumber.RuneBurst, "Burst", 60);
        effect.PowerUpDefinitions.Add(this.CreatePowerUp(Stats.WizardryAttackDamageIncrease, 0.2f));
        this.GameConfiguration.Skills.First(s => s.Number == (short)SkillNumber.RuneBurst).MagicEffectDef = effect;
    }

    private void CreateHasteEffect()
    {
        var effect = this.CreateEffect(MagicEffectNumber.RuneHaste, "Haste", 60);
        effect.PowerUpDefinitions.Add(this.CreatePowerUp(Stats.AttackSpeedAny, 20f));
        this.GameConfiguration.Skills.First(s => s.Number == (short)SkillNumber.RuneHaste).MagicEffectDef = effect;
    }

    private MagicEffectDefinition CreateEffect(MagicEffectNumber number, string name, float durationInSeconds)
    {
        var effect = this.Context.CreateNew<MagicEffectDefinition>();
        this.GameConfiguration.MagicEffects.Add(effect);
        effect.Number = (short)number;
        effect.Name = name;
        effect.InformObservers = true;
        effect.SendDuration = false;
        effect.StopByDeath = true;
        effect.Duration = this.Context.CreateNew<PowerUpDefinitionValue>();
        effect.Duration.ConstantValue.Value = durationInSeconds;
        return effect;
    }

    private PowerUpDefinition CreatePowerUp(AttributeDefinition target, float value)
    {
        var powerUp = this.Context.CreateNew<PowerUpDefinition>();
        powerUp.TargetAttribute = target.GetPersistent(this.GameConfiguration);
        powerUp.Boost = this.Context.CreateNew<PowerUpDefinitionValue>();
        powerUp.Boost.ConstantValue.Value = value;
        return powerUp;
    }
}
