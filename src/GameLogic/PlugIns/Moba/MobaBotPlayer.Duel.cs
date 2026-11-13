// <copyright file="MobaBotPlayer.Duel.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;

/// <summary>
/// The duel mode of the bots (2026-11-13): <see cref="MobaDuel"/> (<c>/mobaduel</c>) puts two bots against each other with the REAL bot AI (rotation
/// of skills, combo, buffs, walking in and kiting) but without the macro brain (lane, shop, recall). In a duel the skills go through the same
/// handlers a player uses (range, mana, cooldown, area, multi-hit), not through the single-hit shortcut of the normal bots.
/// </summary>
public sealed partial class MobaBotPlayer
{
    /// <summary>Gets or sets a value indicating whether this bot only lives for a duel: it does nothing until <see cref="DuelOpponent"/> is set.</summary>
    public bool DuelMode { get; set; }

    /// <summary>Gets or sets the opponent of the duel; the bot fights only it. <see langword="null"/> = waiting (or the duel is over).</summary>
    public Player? DuelOpponent { get; set; }

    /// <summary>
    /// Brings the freshly spawned clone to the state of a champion at <paramref name="level"/>: the skill, buff and stat points of the levels
    /// are granted and spent like a human would (<see cref="DevelopIfDueAsync"/>), and every skill of the kit is set to the given rank.
    /// </summary>
    /// <param name="level">The champion level.</param>
    /// <param name="rank">The rank of every skill and buff.</param>
    /// <returns>A task.</returns>
    public async ValueTask PrepareDuelAsync(int level, int rank)
    {
        if (this.SelectedCharacter is not { } character)
        {
            return;
        }

        this.MobaLevel = 1;
        for (var lvl = 2; lvl <= level; lvl++)
        {
            this.MobaLevel = lvl;
            var (skillPoints, buffPoints) = MobaSkills.PointsForReaching(this, lvl);
            this.MobaSkillPoints += skillPoints;
            this.MobaBuffPoints += buffPoints;
            character.LevelUpPoints += MobaStatEconomy.PointsPerLevel(this);
        }

        MobaProgression.ApplyLevelScaling(this);
        this._nextDevelopUtc = default;
        await this.DevelopIfDueAsync().ConfigureAwait(false);

        foreach (var entry in character.LearnedSkills.Where(e => e.Skill is not null))
        {
            entry.Level = Math.Max(entry.Level, rank);
        }

        this.MobaSkillCooldowns.Clear();
        this.MobaSkillGraceEnds.Clear();
        if (this.Attributes is { } a)
        {
            a[Stats.CurrentHealth] = a[Stats.MaximumHealth];
            a[Stats.CurrentMana] = a[Stats.MaximumMana];
            a[Stats.CurrentShield] = a[Stats.MaximumShield];
        }
    }

    /// <summary>
    /// Wears the full tier 3 build of the class: the standard weapon, the primary set (helm to boots), the resistance shield (or the book) and
    /// the accessories (offensive ring, tank ring and resources pendant). What the champion wore in those slots is replaced.
    /// </summary>
    /// <returns>The number of pieces worn.</returns>
    public async ValueTask<int> EquipFullT3Async()
    {
        if (this.SelectedCharacter?.CharacterClass is not { } characterClass || this.Inventory is not { } inventory)
        {
            return 0;
        }

        var configuration = this.GameContext.Configuration;
        foreach (var category in new[] { MobaShopCategory.Weapons, MobaShopCategory.Offhand, MobaShopCategory.Sets, MobaShopCategory.Accessories })
        {
            _ = MobaShop.BuildCategoryItems(configuration, characterClass, category, out _);
        }

        var family = MobaPassives.FamilyOf(this);
        bool OfFamily(MobaShopEntry e) => e.Families is { Length: > 0 } f && f[0] == family;
        var entries = new List<MobaShopEntry>();
        entries.AddRange(MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Weapons && OfFamily(e) && e.Tier == MobaShopTier.T3 && e.Variant == MobaShopVariant.Standard && e.Quantity == 1));
        entries.AddRange(MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Sets && OfFamily(e) && e.Tier == MobaShopTier.T3 && e.Variant == MobaShopVariant.Standard));
        var offhands = MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Offhand && OfFamily(e) && e.Tier == MobaShopTier.T3 && e.Quantity == 1).ToList();
        if ((offhands.FirstOrDefault(e => e.Variant == MobaShopVariant.Bulwark) ?? offhands.FirstOrDefault()) is { } offhand)
        {
            entries.Add(offhand);
        }

        foreach (var role in new[] { MobaShopVariant.Aggressive, MobaShopVariant.Bulwark, MobaShopVariant.Sustain })
        {
            if (MobaShopCatalog.Entries.FirstOrDefault(e => e.Category == MobaShopCategory.Accessories && e.Tier == MobaShopTier.T3 && e.Variant == role) is { } accessory)
            {
                entries.Add(accessory);
            }
        }

        var worn = 0;
        var usedSlots = new HashSet<byte>();
        foreach (var entry in entries)
        {
            if (configuration.Items.FirstOrDefault(d => d.Group == entry.Group && d.Number == entry.Number) is not { } definition
                || definition.QualifiedCharacters.Count > 0 && !definition.QualifiedCharacters.Contains(characterClass)
                || definition.ItemSlot?.ItemSlots is not { Count: > 0 } slotList)
            {
                continue;
            }

            // The first listed slot not yet filled by this build (rings go to the two ring slots).
            var slots = slotList.Select(s => (byte)s).ToList();
            var slot = slots.FirstOrDefault(s => !usedSlots.Contains(s), slots[0]);
            usedSlots.Add(slot);
            if (inventory.GetItem(slot) is { } current)
            {
                await inventory.RemoveItemAsync(current).ConfigureAwait(false);
                await this.PersistenceContext.DeleteAsync(current).ConfigureAwait(false);
            }

            await inventory.AddItemAsync(slot, MobaShop.CreateItem(definition, entry)).ConfigureAwait(false);
            worn++;
        }

        if (this.Attributes is { } a)
        {
            a[Stats.CurrentHealth] = a[Stats.MaximumHealth];
            a[Stats.CurrentMana] = a[Stats.MaximumMana];
            a[Stats.CurrentShield] = a[Stats.MaximumShield];
        }

        return worn;
    }

    /// <summary>One tick of a duelist: its class buffs, then the real fight logic against the opponent.</summary>
    /// <param name="map">The map.</param>
    /// <returns>A task.</returns>
    private async ValueTask TickDuelAsync(GameMap map)
    {
        if (this.DuelOpponent is not { IsAlive: true } foe || !this.IsAlive)
        {
            return;
        }

        if (this.Attributes is not { } a || a[Stats.IsStunned] > 0 || a[Stats.IsFrozen] > 0 || a[Stats.IsAsleep] > 0)
        {
            return;
        }

        var pos = this.Position;
        var ctx = this.BuildContext(map, pos, DateTime.UtcNow);
        await this.TickUtilityAsync(ctx).ConfigureAwait(false);
        await this.EngageAsync(ctx, foe).ConfigureAwait(false);
    }

    /// <summary>
    /// Uses a skill of the rotation: in a duel through the handlers a player uses (the cost, the cooldown, the range and the area are theirs); otherwise
    /// the classic bot shortcut (pay, then hit the target directly).
    /// </summary>
    /// <param name="entry">The skill.</param>
    /// <param name="target">The target.</param>
    /// <returns><see langword="true"/> if the skill was cast.</returns>
    private async ValueTask<bool> TryUseSkillAsync(SkillEntry entry, IAttackable target)
    {
        if (this.DuelOpponent is null)
        {
            if (!await this.TryConsumeForSkillAsync(entry).ConfigureAwait(false))
            {
                return false;
            }

            await this.FireSkillAsync(entry, target).ConfigureAwait(false);
            return true;
        }

        var skill = entry.Skill!;
        var manaBefore = this.Attributes?[Stats.CurrentMana] ?? 0f;
        var now = DateTime.UtcNow;
        try
        {
            await MobaSkillProbe.CastCoreAsync(this, entry, target).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Logger.LogWarning(ex, "[MOBA-DUEL] {Name} failed casting {Skill}.", this.Name, skill.Name);
            return false;
        }

        var armed = this.MobaSkillCooldowns.TryGetValue(skill.Number, out var ready) && ready > now;
        return armed || (this.Attributes?[Stats.CurrentMana] ?? 0f) < manaBefore;
    }
}
