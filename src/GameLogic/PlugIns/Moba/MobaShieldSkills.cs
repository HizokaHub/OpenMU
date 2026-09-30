// <copyright file="MobaShieldSkills.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.DataModel.Configuration.Items;

/// <summary>
/// The active skill every shield of the shop carries (native MU shields have none, or a class buff like
/// Defense that champions also learn). It is <b>Spell of Protection</b>, a Season 6 skill that exists in
/// the client with its own icon and that no champion learns in the MOBA, so casting it can only come
/// from a shield. Its cooldown depends on the tier of the equipped shield (60 / 50 / 40 s).
/// <para>
/// Casting the skill of the equipped off-hand item raises the 30 s "Aegis" barrier (see
/// <see cref="MobaCastEffects"/>); the skill exists only while the shield is equipped, because
/// the skill list of the player adds and removes item skills with the equipment. The Summoner's
/// books already carry a skill of their own (the curses), which plays the same role.
/// </para>
/// </summary>
public static class MobaShieldSkills
{
    /// <summary>The skill number of Spell of Protection, the skill of every shield.</summary>
    public const short ShieldSkillNumber = 210;

    private static readonly object ConfigLock = new();

    /// <summary>Cooldown in seconds by shield tier 1..3 (the barrier itself lasts 30 s).</summary>
    private static readonly double[] CooldownSecondsByTier = { 0, 60, 50, 40 };

    private static GameConfiguration? _configuredFor;

    /// <summary>Gets a value indicating whether the skill is the shield skill.</summary>
    /// <param name="skillNumber">The skill number.</param>
    /// <returns><c>true</c> for Spell of Protection.</returns>
    public static bool IsShieldSkill(short skillNumber) => skillNumber == ShieldSkillNumber;

    /// <summary>The cooldown of the shield skill for the champion's equipped shield.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The cooldown by the tier of the equipped off-hand item.</returns>
    public static TimeSpan CooldownOf(Player champion)
        => TimeSpan.FromSeconds(CooldownSecondsByTier[Math.Clamp(MobaItemPower.OffhandTierOf(champion), 1, 3)]);

    /// <summary>
    /// Whether the cast skill is the skill of the champion's equipped off-hand item (shield or book),
    /// which is what raises the Aegis barrier.
    /// </summary>
    /// <param name="champion">The casting champion.</param>
    /// <param name="skill">The skill that was cast.</param>
    /// <returns><c>true</c> if the skill comes from the equipped shield / book.</returns>
    public static bool IsEquippedOffhandSkill(Player champion, Skill skill)
        => champion.Inventory?.Items.FirstOrDefault(i => i.ItemSlot == InventoryConstants.LeftHandSlot) is { HasSkill: true, Definition.Skill: { } offhandSkill }
           && offhandSkill.Number == skill.Number;

    /// <summary>
    /// Gives the shield definitions of the catalog their skill (once, in memory) and makes those skills
    /// castable by the classes that can wear the shield.
    /// </summary>
    /// <param name="configuration">The game configuration.</param>
    public static void EnsureConfigured(GameConfiguration configuration)
    {
        if (ReferenceEquals(_configuredFor, configuration))
        {
            return;
        }

        lock (ConfigLock)
        {
            if (ReferenceEquals(_configuredFor, configuration))
            {
                return;
            }

            foreach (var entry in MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Offhand && e.Group == 6))
            {
                if (configuration.Items.FirstOrDefault(d => d.Group == entry.Group && d.Number == entry.Number) is not { } definition
                    || configuration.Skills.FirstOrDefault(s => s.Number == ShieldSkillNumber) is not { } skill)
                {
                    continue;
                }

                definition.Skill = skill;

                // A self-cast: no target, no damage; the effect is the Aegis barrier.
                skill.SkillType = SkillType.Buff;
                skill.TargetRestriction = SkillTargetRestriction.Self;
                skill.ImplicitTargetRange = 0;
                foreach (var characterClass in definition.QualifiedCharacters)
                {
                    if (!skill.QualifiedCharacters.Contains(characterClass))
                    {
                        skill.QualifiedCharacters.Add(characterClass);
                    }
                }
            }

            _configuredFor = configuration;
        }
    }
}
