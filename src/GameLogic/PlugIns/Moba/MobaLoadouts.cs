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

    // Testing loadout: EVERY castable damage / area / CC skill the family has, so all of a
    // class's skills can be reviewed in one match. Skill numbers = Persistence SkillNumber.
    // Only 9 auto-fill the client skill bar (the first 9 here) - the rest are learned and
    // can be dragged onto the bar. Numbers missing from the game config are skipped by
    // Apply(). Balance / the real "pick a few" flow comes later.
    private static readonly Dictionary<Family, short[]> Skills = new()
    {
        // Dark Wizard / Soul Master / Grand Master - full spell kit + buffs.
        // Energy Ball, Fire Ball, Lightning, Power Wave, Poison, Ice, Evil Spirit, Meteorite, Ice Storm,
        // Flame, Twister, Hellfire, Aqua Beam, Cometfall, Inferno, Decay, Nova, Lance, Plasma Storm,
        // Soul Barrier, Expansion of Wizardry
        [Family.Wizard] = new short[] { 17, 4, 3, 11, 1, 7, 9, 2, 39, 5, 8, 10, 12, 13, 14, 38, 40, 45, 76, 16, 233 },
        // Blade Knight - full melee kit + buffs.
        // Falling Slash, Lunge, Uppercut, Cyclone, Slash, Twisting Slash, Rageful Blow, Death Stab, Strike of Destruction,
        // Crescent Moon Slash, Impale, Fire Breath, Stun, Plasma Storm, Defense, Swell Life
        [Family.Knight] = new short[] { 19, 20, 21, 22, 23, 41, 42, 43, 232, 44, 47, 49, 67, 76, 18, 48 },
        // High Elf - every ranged attack + party buffs + the seven monster summons.
        // Triple Shot, Ice Arrow, Penetration, Multi-Shot, Heal, Greater Damage, Greater Defense, Stun, Plasma Storm,
        // Infinity Arrow, Recovery, Summon Goblin/Stone Golem/Assassin/Elite Yeti/Dark Knight/Bali/Soldier
        [Family.Elf] = new short[] { 24, 51, 52, 235, 26, 28, 27, 67, 76, 77, 234, 30, 31, 32, 33, 34, 35, 36 },
        // Magic Gladiator - hybrid: wizard spells + knight slashes + Defense.
        // Energy Ball, Fire Ball, Lightning, Ice, Falling Slash, Cyclone, Twisting Slash, Fire Slash, Power Slash,
        // Poison, Meteorite, Flame, Evil Spirit, Power Wave, Inferno, Spiral Slash, Flame Strike, Gigantic Storm, Plasma Storm, Defense
        [Family.MagicGladiator] = new short[] { 17, 4, 3, 7, 19, 22, 41, 55, 56, 1, 2, 5, 9, 11, 14, 57, 236, 237, 76, 18 },
        // Lord Emperor - scepter skills + basic slashes + buffs + Summon (Earthshake 62 / Chaotic 238 need the Dark Horse mount).
        // Force, Fire Burst, Force Wave, Electric Spike, Fire Scream, Falling Slash, Lunge, Uppercut, Cyclone, Slash,
        // Fire Blast, Plasma Storm, Defense, Increase Critical Damage, Summon
        [Family.DarkLord] = new short[] { 60, 61, 66, 65, 78, 19, 20, 21, 22, 23, 74, 76, 18, 64, 63 },
        // Summoner - full curse / nature kit + debuffs (Lightning Orb 216 & Blind 220 are not in the config).
        // Fire Ball, Ice, Meteorite, Power Wave, Lance, Drain Life, Chain Lightning, Explosion, Requiem, Pollution,
        // Lightning Shock, Plasma Storm, Damage Reflection, Berserker, Sleep, Weakness, Innovation, Recovery
        [Family.Summoner] = new short[] { 4, 7, 2, 11, 45, 214, 215, 223, 224, 225, 230, 76, 217, 218, 219, 221, 222, 234 },
        // Rage Fighter - full kit + self-buffs (Chain Drive 262 / Charge 269 / some hits still want the Fenrir mount).
        // Killing Blow, Beast Uppercut, Chain Drive, Dark Side, Dragon Roar, Dragon Slasher, Charge, Phoenix Shot, Falling Slash,
        // Ignore Defense, Increase Health, Increase Block
        [Family.RageFighter] = new short[] { 260, 261, 262, 263, 264, 265, 269, 270, 19, 266, 267, 268 },
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
