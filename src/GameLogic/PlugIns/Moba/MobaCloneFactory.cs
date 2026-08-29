// <copyright file="MobaCloneFactory.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.AttributeSystem;
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

    private static readonly (byte X, byte Y) ArenaSpawn = (128, 128);

    /// <summary>
    /// Builds a fresh clone character from the given player's selected (real) character.
    /// </summary>
    /// <param name="player">The player whose real character is cloned.</param>
    /// <returns>The detached-in-spirit clone character, ready to be used as the selected character for a match.</returns>
    public static async ValueTask<Character> BuildCloneAsync(Player player)
    {
        var real = player.SelectedCharacter ?? throw new InvalidOperationException("Player has no selected character to clone.");
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

        // Stats: copy the real distribution for now (per-class baseline table is a later topic),
        // then force the match starting level.
        foreach (var stat in real.Attributes)
        {
            clone.Attributes.Add(context.CreateNew<StatAttribute>(stat.Definition, stat.Value));
        }

        EnsureAttribute(context, clone, Stats.Level, MatchStartLevel);

        // Master progression starts from scratch every match.
        clone.MasterExperience = 0;
        clone.MasterLevelUpPoints = 0;
        clone.LevelUpPoints = 0;

        // No inherited items (starter weapon per class is a later topic) and no
        // inherited skills (the active loadout picker is a later topic).
        clone.Inventory = context.CreateNew<ItemStorage>();
        clone.Inventory.Money = 0;

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
    public static void DiscardClone(Player player, Character clone)
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
