// <copyright file="MobaLoadouts.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Fixed starter loadout (basic weapon + a handful of active skills) per class family
/// for a MOBA match clone. Deterministic - it does not depend on the player's real
/// character. The interactive "pick 4-6 skills before the match" flow replaces the
/// skill list later; the weapon stays a class default.
/// </summary>
/// <remarks>
/// A clone bypasses skill stat requirements (see <see cref="Player.IsMobaClone"/>), so
/// the skill picks here only need to make sense for the class, not fit a stat budget.
/// Balance is a later tuning pass.
/// </remarks>
public static class MobaLoadouts
{
    private const byte AmmoGroup = 4;
    private const short ArrowsNumber = 15;
    private const short BoltNumber = 7;

    private enum Family
    {
        Wizard,
        Knight,
        Elf,
        MagicGladiator,
        DarkLord,
        Summoner,
        RageFighter,
    }

    private readonly record struct WeaponSpec(byte Group, short Number, byte Slot);

    // Lowest-tier class weapon. Right hand = slot 1, left hand (ammo) = slot 0.
    private static readonly Dictionary<Family, WeaponSpec[]> Weapons = new()
    {
        [Family.Wizard] = new[] { new WeaponSpec(5, 0, 1) },                                   // Skull Staff
        [Family.Knight] = new[] { new WeaponSpec(0, 1, 1) },                                   // Short Sword
        [Family.Elf] = new[] { new WeaponSpec(4, 0, 1), new WeaponSpec(AmmoGroup, ArrowsNumber, 0) }, // Short Bow + Arrows
        [Family.MagicGladiator] = new[] { new WeaponSpec(0, 1, 1) },                           // Short Sword
        [Family.DarkLord] = new[] { new WeaponSpec(0, 1, 1) },                                 // Short Sword (no low scepter)
        [Family.Summoner] = new[] { new WeaponSpec(5, 0, 1) },                                 // Skull Staff / stick
        [Family.RageFighter] = new[] { new WeaponSpec(0, 1, 1) },                              // Short Sword (no low knuckle)
    };

    // 4-6 active skills per family (skill numbers from Persistence SkillNumber).
    private static readonly Dictionary<Family, short[]> Skills = new()
    {
        [Family.Wizard] = new short[] { 17, 4, 3, 11, 9, 7 },   // Energy Ball, Fire Ball, Lightning, Power Wave, Evil Spirit, Ice
        [Family.Knight] = new short[] { 19, 20, 22, 23, 41, 21 }, // Falling Slash, Lunge, Cyclone, Slash, Twisting Slash, Uppercut
        [Family.Elf] = new short[] { 24, 26, 28, 27, 52, 46 },   // Triple Shot, Heal, Greater Damage, Greater Defense, Penetration, Starfall
        [Family.MagicGladiator] = new short[] { 17, 4, 19, 41, 22, 3 }, // Energy Ball, Fire Ball, Falling Slash, Twisting Slash, Cyclone, Lightning
        [Family.DarkLord] = new short[] { 60, 74, 62, 19 },      // Force, Fire Blast, Earthshake, Falling Slash
        [Family.Summoner] = new short[] { 17, 4, 3, 214, 7 },    // Energy Ball, Fire Ball, Lightning, Drain Life, Ice
        [Family.RageFighter] = new short[] { 19, 22, 23, 41 },   // Falling Slash, Cyclone, Slash, Twisting Slash
    };

    /// <summary>
    /// Adds the class-default weapon (with ammo if it is a bow / crossbow) and the class
    /// skill list to a freshly built clone.
    /// </summary>
    /// <param name="context">The persistence context the clone was created with.</param>
    /// <param name="config">The game configuration.</param>
    /// <param name="clone">The clone character.</param>
    /// <param name="characterClass">The clone's class.</param>
    public static void Apply(Persistence.IContext context, GameConfiguration config, Character clone, CharacterClass characterClass)
    {
        var inventory = clone.Inventory ?? throw new InvalidOperationException("Clone has no inventory.");
        var family = FamilyOf(characterClass.Number);

        foreach (var spec in Weapons[family])
        {
            var definition = config.Items.FirstOrDefault(d => d.Group == spec.Group && d.Number == spec.Number);
            if (definition is null)
            {
                continue;
            }

            var item = context.CreateNew<Item>();
            item.Definition = definition;
            item.Durability = definition.Durability > 0 ? definition.Durability : 255d;
            item.Level = 0;
            item.ItemSlot = spec.Slot;
            inventory.Items.Add(item);
        }

        foreach (var number in Skills[family])
        {
            if (config.Skills.FirstOrDefault(s => s.Number == number) is not { } skill)
            {
                continue;
            }

            var entry = context.CreateNew<SkillEntry>();
            entry.Skill = skill;
            entry.Level = 0;
            clone.LearnedSkills.Add(entry);
        }
    }

    /// <summary>
    /// The MOBA loadout skill numbers for a class (the same list the match clone gets).
    /// </summary>
    /// <param name="characterClass">The character class.</param>
    /// <returns>The skill numbers, or an empty list if the class has no loadout.</returns>
    public static IReadOnlyList<short> SkillNumbersFor(CharacterClass characterClass)
        => Skills.TryGetValue(FamilyOf(characterClass.Number), out var numbers) ? numbers : Array.Empty<short>();

    private static Family FamilyOf(byte classNumber) => classNumber switch
    {
        0 or 2 or 3 => Family.Wizard,
        4 or 6 or 7 => Family.Knight,
        8 or 10 or 11 => Family.Elf,
        12 or 13 => Family.MagicGladiator,
        16 or 17 => Family.DarkLord,
        20 or 22 or 23 => Family.Summoner,
        24 or 25 => Family.RageFighter,
        _ => Family.Wizard,
    };
}
