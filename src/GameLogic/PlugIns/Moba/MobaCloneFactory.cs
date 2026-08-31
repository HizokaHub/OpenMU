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
    /// Flat baseline for STR / CMD of every clone, regardless of class or the player's
    /// real build. Combat power is meant to come from skills + a later tuning pass, not
    /// from stats; see GAMEDESIGN.md.
    /// </summary>
    private const int BaselineStatValue = 10;

    /// <summary>
    /// Flat AGI for every clone. High so the clone doesn't get shredded by creeps before
    /// the balance pass (AGI drives defense / attack rate / block). Uniform across classes.
    /// </summary>
    private const int BaselineAgilityValue = 2000;

    /// <summary>
    /// Flat ENE for every clone. Not left at 10 because ENE is the mana pool: casters
    /// need to afford their spells. Uniform across classes.
    /// </summary>
    private const int BaselineEnergyValue = 1000;

    /// <summary>
    /// Placeholder vitality for the clone so it survives long enough to test. Real
    /// per-class stats / balance come later.
    /// </summary>
    private const int TestVitality = 2000;

    /// <summary>Free stat points handed to the clone so the tester can distribute them (never auto-assigned).</summary>
    private const int TestLevelUpPoints = 5000;

    private static readonly (byte X, byte Y) ArenaSpawn = (124, 140);

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
        // Fresh hotkey bar: the real character's config maps its own skills, which the
        // clone doesn't have. A default MOBA bar (base 4 on keys 1-4) is a client-side
        // task; for now the player drags the 4 base skills from the skill window.
        clone.KeyConfiguration = null;

        // Stats: every clone of every class starts from the same flat baseline (not the
        // player's real build), then the fixed match level. The class stat-attribute set
        // defines which stats exist (STR/AGI/VIT/ENE, plus CMD for Dark Lord).
        foreach (var classStat in characterClass.StatAttributes.Where(a => a is { IncreasableByPlayer: true, Attribute: not null }))
        {
            var value = BaselineStatValue;
            if (classStat.Attribute!.Id == Stats.BaseVitality.Id)
            {
                value = TestVitality;
            }
            else if (classStat.Attribute.Id == Stats.BaseAgility.Id)
            {
                value = BaselineAgilityValue;
            }
            else if (classStat.Attribute.Id == Stats.BaseEnergy.Id)
            {
                value = BaselineEnergyValue;
            }

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

        // Fixed class starter loadout: a basic class weapon (+ ammo for bows) and the
        // class skill list. Deterministic, independent of the real character. The "pick
        // 4-6 skills" flow replaces the skill list later.
        MobaLoadouts.Apply(context, player.GameContext.Configuration, clone, characterClass);

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
