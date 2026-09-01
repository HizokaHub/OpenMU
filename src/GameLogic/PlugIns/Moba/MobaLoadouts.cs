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
        [Family.DarkLord] = new[] { new WeaponSpec(2, 8, 1) },                                 // Battle Scepter (sets IsScepterEquipped for Force / Fire Burst)
        [Family.Summoner] = new[] { new WeaponSpec(5, 0, 1) },                                 // Skull Staff / stick
        [Family.RageFighter] = new[] { new WeaponSpec(0, 32, 1) },                             // Sacred Glove (sets IsGloveWeaponEquipped for RF skills)
    };

    // Up to 9 active skills per family (the design's "4 base + 5 picks"). Skill numbers
    // from Persistence SkillNumber. First testing batch - a second batch covers whatever
    // each class is still missing.
    private static readonly Dictionary<Family, short[]> Skills = new()
    {
        // Energy Ball, Fire Ball, Lightning, Power Wave, Evil Spirit, Ice, Poison, Meteo, Ice Storm
        [Family.Wizard] = new short[] { 17, 4, 3, 11, 9, 7, 1, 2, 39 },
        // Falling Slash, Lunge, Cyclone, Slash, Twisting Slash, Uppercut, Rageful Blow, Death Stab, Strike of Destruction
        [Family.Knight] = new short[] { 19, 20, 22, 23, 41, 21, 42, 43, 232 },
        // Triple Shot, Penetration, Ice Arrow, Multi-Shot, Heal, Greater Damage, Greater Defense, Starfall
        [Family.Elf] = new short[] { 24, 52, 51, 235, 26, 28, 27, 46 },
        // Energy Ball, Fire Ball, Lightning, Ice, Falling Slash, Cyclone, Twisting Slash, Fire Slash, Power Slash
        [Family.MagicGladiator] = new short[] { 17, 4, 3, 7, 19, 22, 41, 55, 56 },
        // Force, Fire Burst, Force Wave, Electric Spike, Falling Slash (Earthshake/Chaotic need the Dark Horse mount)
        [Family.DarkLord] = new short[] { 60, 61, 66, 65, 19 },
        // Energy Ball, Fire Ball, Lightning, Ice, Poison, Drain Life, Chain Lightning, Lightning Orb, Blind
        [Family.Summoner] = new short[] { 17, 4, 3, 7, 1, 214, 215, 216, 220 },
        // Killing Blow, Beast Uppercut, Dark Side, Dragon Roar, Dragon Kick, Phoenix Shot (some need the Fenrir mount)
        [Family.RageFighter] = new short[] { 260, 261, 263, 264, 265, 270 },
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

            // Champion skills start at rank 0; champion points raise them 0 -> 5. (The
            // greyed-out icon on the client is a separate, client-side stat/weapon check,
            // fixed there for MOBA mode - not caused by the rank.)
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
