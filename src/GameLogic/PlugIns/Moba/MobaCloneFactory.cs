// <copyright file="MobaCloneFactory.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// Builds the ephemeral MOBA match clone of a player's real character.
/// </summary>
/// <remarks>
/// The clone is created through the player's persistence context (so its nav
/// collections are initialized) but the session runs with
/// <see cref="Player.SuppressPersistence"/> set, so it is never written to the
/// database. The real character is only read, never mutated. See GAMEDESIGN.md.
///
/// Still to do in later blocks: per-class baseline stat table (right now the real
/// character's stat point distribution is copied), the basic starter weapon per
/// class, and the 4-6 skill active loadout.
/// </remarks>
public static class MobaCloneFactory
{
    /// <summary>
    /// The map number of the MOBA Arena.
    /// </summary>
    public const ushort ArenaMapNumber = 200;

    private const int MatchStartLevel = 400;

    /// <summary>
    /// Flat baseline value assigned to every increasable stat (STR / AGI / VIT / ENE /
    /// CMD) of every clone, regardless of class or the player's real build. Placeholder
    /// while damage / scaling is tuned; see GAMEDESIGN.md.
    /// </summary>
    private const int BaselineStatValue = 10;

    /// <summary>
    /// Placeholder vitality for the clone so it survives long enough to test creep
    /// targeting. Overrides the flat baseline just for VIT. Real per-class stats and
    /// balance come later.
    /// </summary>
    private const int TestVitality = 2000;

    /// <summary>Free stat points handed to the clone so the tester can distribute them (never auto-assigned).</summary>
    private const int TestLevelUpPoints = 5000;

    private static readonly (byte X, byte Y) ArenaSpawn = (116, 60);

    /// <summary>
    /// Builds a fresh clone character from the given real character.
    /// </summary>
    /// <param name="player">The player who will play the clone (its persistence context is used to create the entities).</param>
    /// <param name="realCharacter">The real character to clone. Only read, never mutated.</param>
    /// <returns>The clone character, ready to be used as the selected character for a match.</returns>
    public static async ValueTask<Character> BuildCloneAsync(Player player, Character realCharacter)
    {
        var real = realCharacter ?? throw new ArgumentNullException(nameof(realCharacter));
        var characterClass = real.CharacterClass ?? throw new InvalidOperationException("The character has no class assigned.");
        var context = player.PersistenceContext;

        var clone = context.CreateNew<Character>();
        clone.Name = real.Name;
        clone.CharacterClass = characterClass;
        clone.CharacterSlot = real.CharacterSlot;
        clone.CharacterStatus = real.CharacterStatus; // keep GM logo etc.
        clone.Pose = CharacterPose.Standing;
        clone.State = HeroState.Normal;
        clone.KeyConfiguration = real.KeyConfiguration is { } key ? (byte[])key.Clone() : null;

        // Stats: every clone of every class starts from the same flat baseline (not the
        // player's real build), then the fixed match level. The class stat-attribute set
        // defines which stats exist (STR/AGI/VIT/ENE, plus CMD for Dark Lord).
        foreach (var classStat in characterClass.StatAttributes.Where(a => a is { IncreasableByPlayer: true, Attribute: not null }))
        {
            var value = classStat.Attribute!.Id == Stats.BaseVitality.Id ? TestVitality : BaselineStatValue;
            clone.Attributes.Add(context.CreateNew<StatAttribute>(classStat.Attribute, value));
        }

        EnsureAttribute(context, clone, Stats.Level, MatchStartLevel);

        // Master progression starts from scratch every match.
        clone.MasterExperience = 0;
        clone.MasterLevelUpPoints = 0;

        // TEST: free stat points for the tester to distribute (never auto-assigned).
        clone.LevelUpPoints = TestLevelUpPoints;

        clone.Inventory = context.CreateNew<ItemStorage>();
        clone.Inventory.Money = 0;

        // Weapon: TEST placeholder - copy the real character's equipped weapon / ammo
        // (hand slots), no armor. The real flow is a basic class weapon (later topic).
        foreach (var equipped in real.Inventory?.Items.Where(i => i.ItemSlot == InventoryConstants.LeftHandSlot || i.ItemSlot == InventoryConstants.RightHandSlot) ?? Enumerable.Empty<Item>())
        {
            if (equipped.Definition is null)
            {
                continue;
            }

            var copy = context.CreateNew<Item>();
            copy.Definition = equipped.Definition;
            copy.Durability = equipped.Durability;
            copy.Level = equipped.Level;
            copy.ItemSlot = equipped.ItemSlot;
            clone.Inventory.Items.Add(copy);
        }

        // Skills: TEST placeholder - copy the real character's learned skills so the
        // player can cast something. The real flow is the 4-6 active-skill loadout
        // picker (later topic).
        foreach (var learned in real.LearnedSkills.Where(s => s.Skill is not null))
        {
            var entry = context.CreateNew<SkillEntry>();
            entry.Skill = learned.Skill;
            entry.Level = learned.Level;
            clone.LearnedSkills.Add(entry);
        }

        // TEST: the test accounts often have no gear / no skills, which makes the clone
        // unable to attack at all (a bow with no arrows, a caster with no spell). Give it
        // a working minimum for its class so creep targeting can actually be tested.
        EnsureUsableLoadout(context, player.GameContext.Configuration, clone, characterClass);

        player.Logger.LogInformation(
            "[MOBA] Clone '{Name}' (class {Class}) loadout: hands=[{Hands}], skills=[{Skills}], points={Points}",
            clone.Name,
            characterClass.Number,
            string.Join(", ", clone.Inventory!.Items
                .Where(i => i.ItemSlot is 0 or 1)
                .Select(i => $"slot{i.ItemSlot}:{i.Definition?.Name}({i.Durability:0})")),
            string.Join(", ", clone.LearnedSkills.Where(s => s.Skill is not null).Select(s => $"{s.Skill!.Name}#{s.Skill.Number}")),
            clone.LevelUpPoints);

        var arenaMap = await player.GameContext.GetMapAsync(ArenaMapNumber).ConfigureAwait(false);
        clone.CurrentMap = arenaMap?.Definition
            ?? player.GameContext.Configuration.Maps.FirstOrDefault(m => m.Number == ArenaMapNumber)
            ?? throw new InvalidOperationException($"MOBA Arena map {ArenaMapNumber} is not configured.");
        clone.PositionX = ArenaSpawn.X;
        clone.PositionY = ArenaSpawn.Y;

        return clone;
    }

    /// <summary>
    /// Detaches the clone (and the entities created for it) from the persistence context.
    /// Safety net on top of <see cref="Player.SuppressPersistence"/>.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="clone">The clone to discard.</param>
    public static void DetachClone(Player player, Character clone)
    {
        var context = player.PersistenceContext;
        if (clone.Inventory is { } inventory)
        {
            context.Detach(inventory);
        }

        foreach (var attribute in clone.Attributes.ToList())
        {
            context.Detach(attribute);
        }

        context.Detach(clone);
    }

    /// <summary>Group 4 weapon numbers that are bows (fire arrows); the rest are crossbows (fire bolts).</summary>
    private static readonly HashSet<short> BowNumbers = new() { 0, 1, 2, 3, 4, 5, 6, 17, 20, 21, 22, 23, 24 };

    private const byte AmmunitionGroup = 4;
    private const short ArrowsNumber = 15;
    private const short BoltNumber = 7;

    /// <summary>
    /// Makes sure the clone can attack for its class: a basic weapon if the hand slots
    /// are empty, a full ammo stack behind any bow / crossbow, and one class-appropriate
    /// attack skill if it learned none. All TEST scaffolding until the real class weapon
    /// + loadout picker exist.
    /// </summary>
    private static void EnsureUsableLoadout(Persistence.IContext context, GameConfiguration config, Character clone, CharacterClass characterClass)
    {
        var inventory = clone.Inventory ?? throw new InvalidOperationException("Clone has no inventory.");
        var classNumber = characterClass.Number;
        var isWizard = classNumber is 0 or 2 or 3;
        var isElf = classNumber is 8 or 10 or 11;
        var isSummoner = classNumber is 20 or 22 or 23;

        Item? MakeItem(byte group, short number, byte slot)
        {
            var definition = config.Items.FirstOrDefault(d => d.Group == group && d.Number == number);
            if (definition is null)
            {
                return null;
            }

            var item = context.CreateNew<Item>();
            item.Definition = definition;
            item.Durability = definition.Durability > 0 ? definition.Durability : 255d;
            item.Level = 0;
            item.ItemSlot = slot;
            inventory.Items.Add(item);
            return item;
        }

        bool IsRealWeapon(Item? i) => i?.Definition is { } d
            && !(d.Group == AmmunitionGroup && (d.Number == ArrowsNumber || d.Number == BoltNumber));

        var rightHand = inventory.Items.FirstOrDefault(i => i.ItemSlot == InventoryConstants.RightHandSlot);
        var leftHand = inventory.Items.FirstOrDefault(i => i.ItemSlot == InventoryConstants.LeftHandSlot);

        // No weapon at all -> give the class its stock one.
        if (!IsRealWeapon(rightHand) && !IsRealWeapon(leftHand))
        {
            rightHand = isWizard || isSummoner
                ? MakeItem(5, 0, InventoryConstants.RightHandSlot)   // Skull Staff
                : isElf
                    ? MakeItem(4, 0, InventoryConstants.RightHandSlot) // Short Bow
                    : MakeItem(0, 1, InventoryConstants.RightHandSlot); // Short Sword
        }

        // Bow / crossbow -> force it into the right hand and a full ammo stack into the
        // left (MU requires ammo in the left-hand slot), clearing anything else there.
        var rangedWeapon = new[] { rightHand, leftHand }
            .FirstOrDefault(i => i?.Definition is { Group: AmmunitionGroup } d && d.Number != ArrowsNumber && d.Number != BoltNumber);
        if (rangedWeapon?.Definition is { } rangedDef)
        {
            rangedWeapon.ItemSlot = InventoryConstants.RightHandSlot;

            foreach (var occupant in inventory.Items
                         .Where(i => !ReferenceEquals(i, rangedWeapon) && i.ItemSlot == InventoryConstants.LeftHandSlot)
                         .ToList())
            {
                inventory.Items.Remove(occupant);
                context.Detach(occupant);
            }

            var ammoNumber = BowNumbers.Contains((short)rangedDef.Number) ? ArrowsNumber : BoltNumber;
            if (inventory.Items.All(i => i.Definition is not { Group: AmmunitionGroup } d || (d.Number != ArrowsNumber && d.Number != BoltNumber)))
            {
                MakeItem(AmmunitionGroup, ammoNumber, InventoryConstants.LeftHandSlot);
            }
        }

        // Top up durability on everything in the hands so nothing breaks mid-test.
        foreach (var handItem in inventory.Items.Where(i =>
                     (i.ItemSlot == InventoryConstants.LeftHandSlot || i.ItemSlot == InventoryConstants.RightHandSlot)
                     && i.Definition is not null))
        {
            handItem.Durability = handItem.Definition!.Durability > 0 ? handItem.Definition.Durability : 255d;
        }

        // No skills learned -> grant one attack skill the class can actually use.
        if (!clone.LearnedSkills.Any(s => s.Skill is not null))
        {
            short defaultSkill = classNumber switch
            {
                0 or 2 or 3 => 17,          // Energy Ball (wizards)
                8 or 10 or 11 => 24,        // Triple Shot (elves)
                4 or 6 or 7 or 24 or 25 => 19, // Falling Slash (knights, rage fighter)
                _ => 17,
            };

            if (config.Skills.FirstOrDefault(s => s.Number == defaultSkill) is { } skill)
            {
                var entry = context.CreateNew<SkillEntry>();
                entry.Skill = skill;
                entry.Level = 1;
                clone.LearnedSkills.Add(entry);
            }
        }
    }

    private static void EnsureAttribute(Persistence.IContext context, Character character, AttributeDefinition definition, float value)
    {
        var existing = character.Attributes.FirstOrDefault(a => a.Definition.Id == definition.Id);
        if (existing is not null)
        {
            existing.Value = value;
            return;
        }

        character.Attributes.Add(context.CreateNew<StatAttribute>(definition, value));
    }
}
