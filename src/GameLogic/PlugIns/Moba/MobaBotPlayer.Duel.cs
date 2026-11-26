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
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(byte, int, bool), bool> LoggedBuilds = new();

    /// <summary>
    /// Gets or sets a value indicating whether a Blade Knight or Magic Gladiator bot buys a second weapon instead of a shield
    /// (see <see cref="MobaBotEconomy.DualWields"/>; the other classes ignore it).
    /// </summary>
    public bool DualWield { get; set; }

    /// <summary>Gets or sets a value indicating whether this duelist keeps and drinks potions (see <see cref="MobaDuel.Drinking"/>).</summary>
    public bool DuelDrinks { get; set; } = true;

    /// <summary>Gets or sets the health potions the duelist used in the current fight.</summary>
    public int DuelHealthPotions { get; set; }

    /// <summary>Gets or sets the mana potions the duelist used in the current fight.</summary>
    public int DuelManaPotions { get; set; }

    /// <summary>Gets or sets a value indicating whether this bot only lives for a duel: it does nothing until <see cref="DuelOpponent"/> is set.</summary>
    public bool DuelMode { get; set; }

    /// <summary>Gets or sets a value indicating whether the duelist fights without moving (no kiting, jumps or rushes: see <see cref="MobaDuel.Static"/>).</summary>
    public bool DuelStatic { get; set; }

    /// <summary>Gets or sets the opponent of the duel; the bot fights only it. <see langword="null"/> = waiting (or the duel is over).</summary>
    public Player? DuelOpponent { get; set; }

    /// <summary>Gets the casts of the duelist per skill number during the blows (measurement of the duels).</summary>
    public System.Collections.Concurrent.ConcurrentDictionary<int, int> DuelCasts { get; } = new();

    /// <summary>Gets or sets a value indicating whether the duelist is in the preparation phase: it only raises its buffs (all of them, one per tick) and does not attack.</summary>
    public bool DuelBuffing { get; set; }

    /// <summary>Gets the number of consecutive preparation ticks in which no buff was cast: the duel starts when both duelists reach a few.</summary>
    public int DuelBuffIdleTicks { get; private set; }

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

            // Like a real level-up (MobaExperience): without the master points a class with a tree (Grand Master, High Elf) fought with an empty
            // tree term - a fifth less damage, mitigation and critical chance than the classes without a tree.
            character.MasterLevelUpPoints += MobaMasterTree.PointsPerLevel;
        }

        MobaProgression.ApplyLevelScaling(this);
        this._nextDevelopUtc = default;
        await this.DevelopIfDueAsync().ConfigureAwait(false);

        if (rank > 0)
        {
            foreach (var entry in character.LearnedSkills.Where(e => e.Skill is not null))
            {
                entry.Level = Math.Max(entry.Level, rank);
            }
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
    /// Buys what a typical champion of the level can have: the gold of <see cref="MobaTypicalBuild"/> spent like a bot would. The first duel of every class and level
    /// writes what it bought to the log (<c>[MOBA-BUILD]</c>).
    /// </summary>
    /// <param name="level">The champion level.</param>
    /// <returns>The number of pieces (gear and pets) bought and worn.</returns>
    public async ValueTask<int> BuyTypicalBuildAsync(int level)
    {
        var gold = MobaTypicalBuild.GoldAt(level);
        var (worn, left) = await MobaBotEconomy.BuyWithBudgetAsync(this, gold, MobaTypicalBuild.PhaseAt(level), this.DuelDrinks).ConfigureAwait(false);
        MobaItemCaps.Apply(this); // sets the life the champion really fights with (F10 pins it), so that the log below tells the truth
        if (this.Attributes is { } a)
        {
            a[Stats.CurrentHealth] = a[Stats.MaximumHealth];
            a[Stats.CurrentMana] = a[Stats.MaximumMana];
            a[Stats.CurrentShield] = a[Stats.MaximumShield];
        }

        if (this.SelectedCharacter?.CharacterClass is { } characterClass && LoggedBuilds.TryAdd((characterClass.Number, level, MobaBotEconomy.DualWields(this)), true))
        {
            this.Logger.LogInformation(
                "[MOBA-BUILD] level {Level} class {Class}: gold {Gold}, unspent {Left}, maxHP {Health:0}, two weapons {Dual}, {Count} pieces: {Pieces}",
                level,
                characterClass.Name,
                gold,
                left,
                this.Attributes?[Stats.MaximumHealth] ?? 0f,
                MobaBotEconomy.DualWields(this),
                worn.Count,
                string.Join(" | ", worn.OrderBy(i => i.ItemSlot).Select(i => $"{i.ItemSlot}:{i.Definition?.Name}+{i.Level} {MobaShop.PriceOf(i)}")));
        }

        return worn.Count;
    }

    /// <summary>
    /// Wears the full tier 3 build of the class: the standard weapon, the primary set (helm to boots), the resistance shield (or the book) and
    /// the accessories (offensive ring, tank ring and resources pendant). What the champion wore in those slots is replaced.
    /// </summary>
    /// <returns>The number of pieces worn.</returns>
    public async ValueTask<int> EquipFullT3Async(MobaShopTier tier = MobaShopTier.T3)
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
        entries.AddRange(MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Weapons && OfFamily(e) && e.Tier == tier && e.Variant == MobaShopVariant.Standard && e.Quantity == 1));
        entries.AddRange(MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Sets && OfFamily(e) && e.Tier == tier && e.Variant == MobaShopVariant.Standard));
        var offhands = MobaShopCatalog.Entries.Where(e => e.Category == MobaShopCategory.Offhand && OfFamily(e) && e.Tier == tier && e.Quantity == 1).ToList();
        if ((offhands.FirstOrDefault(e => e.Variant == MobaShopVariant.Bulwark) ?? offhands.FirstOrDefault()) is { } offhand)
        {
            entries.Add(offhand);
        }

        foreach (var role in new[] { MobaShopVariant.Aggressive, MobaShopVariant.Bulwark, MobaShopVariant.Sustain })
        {
            if (MobaShopCatalog.Entries.FirstOrDefault(e => e.Category == MobaShopCategory.Accessories && e.Tier == tier && e.Variant == role) is { } accessory)
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
        if (this.DuelBuffing)
        {
            // Preparation: both duelists raise every buff before the first blow (nobody attacks while the other is still buffing).
            this.DuelBuffIdleTicks = await this.TickBuffsAsync(ctx).ConfigureAwait(false) ? 0 : this.DuelBuffIdleTicks + 1;
            return;
        }

        if (this.DuelDrinks)
        {
            var drunk = await MobaBotEconomy.DrinkInDuelAsync(this).ConfigureAwait(false);
            this.DuelHealthPotions += drunk == MobaBotEconomy.PotionDrunk.Health ? 1 : 0;
            this.DuelManaPotions += drunk == MobaBotEconomy.PotionDrunk.Mana ? 1 : 0;
        }

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
        var cast = armed || (this.Attributes?[Stats.CurrentMana] ?? 0f) < manaBefore;
        if (cast)
        {
            this.DuelCasts.AddOrUpdate(skill.Number, 1, (_, n) => n + 1);
        }

        return cast;
    }
}
