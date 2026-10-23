// <copyright file="MobaPets.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Pet;
using MUnique.OpenMU.GameLogic.PlayerActions.Items;
using MUnique.OpenMU.GameLogic.Views.World;

/// <summary>
/// MOBA balance of pets, mounts and the Dark Lord's Dark Raven.
/// <para>
/// <b>Utility pets:</b> the native MU pet bonuses (Imp +30 % attack, Guardian Angel -20 % damage
/// taken...) are far too big for the MOBA, so <see cref="EnsureConfigured"/> rewrites the
/// base power-ups of the pet definitions once, in memory, to the numbers of the design (a pet is
/// worth roughly 3-11 % of one stat). The combat pipeline already multiplies by the
/// <c>AttackDamageIncrease</c> / <c>DamageReceiveDecrement</c> stats, so nothing else is needed.
/// </para>
/// <para>
/// <b>Dark Lord pets:</b> the Dark Horse gives the Earthshake skill and the Dark Raven attacks on
/// its own (see <see cref="TickAsync"/> and <c>RavenCommandManager</c>). Together they are meant to
/// be 25 % of the Dark Lord's damage: about 15 % Earthshake, 10 % Raven (see
/// <see cref="DarkLordPetShare"/>).
/// </para>
/// </summary>
public static class MobaPets
{
    /// <summary>The Raven hits as a Dark Lord basic attack times this factor.</summary>
    public const double RavenDamageFactor = 0.37;

    /// <summary>Seconds between two Raven attacks.</summary>
    public const double RavenAttackSeconds = 1.5;

    /// <summary>The pet level Dark Horse / Dark Raven are sold at (their maximum).</summary>
    public const int MaxPetLevel = 50;

    /// <summary>Movement speed (native unit) of mounts: 10 % above the 15 of the wings.</summary>
    private const float MountSpeed = 16.5f;

    private const byte PetSlotOfHorse = 8;
    private const byte PetSlotOfRaven = 1;
    private const short HorseNumber = 4;
    private const short RavenNumber = 5;
    private const short EarthshakeSkill = 62;

    private static readonly object ConfigLock = new();

    private static readonly ConditionalWeakTable<Item, object> ActivatedRavens = new();

    /// <summary>The Dark Lord's damaging skills (without the pets').</summary>
    private static readonly short[] DarkLordKitSkills = { 60, 61, 66, 65, 78, 74, 19, 20, 21, 22, 23 };

    private static readonly ConditionalWeakTable<Player, ActivePets> ActiveByPlayer = new();

    private static bool _configured;

    /// <summary>What a Dark Lord has switched on from its inventory.</summary>
    private sealed class ActivePets
    {
        public Item? Horse { get; set; }

        public IElement? HorseSpeed { get; set; }

        public IElement? HorseSpeedUnderwater { get; set; }

        public Skill? HorseSkill { get; set; }

        public Item? Raven { get; set; }
    }

    /// <summary>
    /// Gets the pet experience a pet needs to be at <paramref name="level"/> (same formula as the
    /// pet definitions: <c>level^3 * 100 * (level + 10)</c>).
    /// </summary>
    /// <param name="level">The pet level.</param>
    /// <returns>The experience.</returns>
    public static int PetExperienceOfLevel(int level) => (int)Math.Min(int.MaxValue, (long)level * level * level * 100 * (level + 10));

    /// <summary>
    /// Model of how much of a Dark Lord's sustained damage the two pets deliver, as
    /// (Earthshake share, Raven share) of the total, for a champion at the given level with the
    /// given skill rank on all skills. Sustained damage = flat damage per cooldown of every
    /// damaging skill of the DL kit, scaled by <paramref name="kitEfficiency"/> (a champion can't
    /// cast everything on cooldown), plus the pets.
    /// </summary>
    /// <param name="level">The champion level (drives the basic attack the Raven copies).</param>
    /// <param name="rank">The skill rank 1..5.</param>
    /// <param name="kitEfficiency">The fraction of the kit's cooldown-limited damage a player really delivers.</param>
    /// <returns>The two shares, each 0..1.</returns>
    public static (double Earthshake, double Raven) DarkLordPetShare(int level, int rank, double kitEfficiency = 0.6)
    {
        var kit = DarkLordKitSkills.Sum(s => MobaSkillDamage.FlatDamageOf(s, rank) / MobaCooldowns.SecondsOf(s, rank)) * kitEfficiency;
        var earthshake = MobaSkillDamage.FlatDamageOf(62, rank) / MobaCooldowns.SecondsOf(62, rank);
        var raven = MobaSkillDamage.BasicAttackFlatOf(level) * RavenDamageFactor / RavenAttackSeconds;
        var total = kit + earthshake + raven;
        return (earthshake / total, raven / total);
    }

    /// <summary>
    /// Rewrites the pet definitions' base power-ups to the MOBA numbers. Idempotent; the change
    /// only affects attribute systems built afterwards, so call it before champions are built.
    /// </summary>
    /// <param name="configuration">The game configuration.</param>
    public static void EnsureConfigured(GameConfiguration configuration)
    {
        if (_configured)
        {
            return;
        }

        lock (ConfigLock)
        {
            if (_configured)
            {
                return;
            }

            void Set(short number, AttributeDefinition attribute, float value, AggregateType aggregate)
            {
                if (configuration.Items.FirstOrDefault(d => d.Group == 13 && d.Number == number) is not { } definition
                    || configuration.Attributes.FirstOrDefault(a => a.Id == attribute.Id) is not { } target)
                {
                    return;
                }

                if (definition.BasePowerUpAttributes.FirstOrDefault(p => p.TargetAttribute?.Id == attribute.Id) is { } existing)
                {
                    existing.BaseValue = value;
                    existing.AggregateType = aggregate;
                }
            }

            // The persistence collections only accept persistence-model entries, so a power-up a pet
            // doesn't have yet is made by re-targeting one of its (now useless) MU weapon-damage entries.
            void Repurpose(short number, AttributeDefinition donor, AttributeDefinition attribute, float value, AggregateType aggregate)
            {
                if (configuration.Items.FirstOrDefault(d => d.Group == 13 && d.Number == number) is not { } definition
                    || configuration.Attributes.FirstOrDefault(a => a.Id == attribute.Id) is not { } target
                    || definition.BasePowerUpAttributes.FirstOrDefault(p => p.TargetAttribute?.Id == donor.Id) is not { } entry)
                {
                    return;
                }

                entry.TargetAttribute = target;
                entry.BaseValue = value;
                entry.AggregateType = aggregate;
            }

            void Clear(short number, params AttributeDefinition[] attributes)
            {
                if (configuration.Items.FirstOrDefault(d => d.Group == 13 && d.Number == number) is not { } definition)
                {
                    return;
                }

                foreach (var toRemove in definition.BasePowerUpAttributes.Where(p => attributes.Any(a => a.Id == p.TargetAttribute?.Id)).ToList())
                {
                    definition.BasePowerUpAttributes.Remove(toRemove);
                }
            }

            // Guardian Angel: 5 % less damage taken, +3 % life.
            Set(0, Stats.DamageReceiveDecrement, 0.95f, AggregateType.Multiplicate);
            Set(0, Stats.MaximumHealth, 75f, AggregateType.AddRaw);

            // Imp: 5 % more damage.
            Set(1, Stats.AttackDamageIncrease, 1.05f, AggregateType.Multiplicate);

            // Horn of Uniria: speed only.
            Set(2, Stats.MovementSpeed, MountSpeed, AggregateType.Maximum);
            Set(2, Stats.MovementSpeedUnderwater, MountSpeed, AggregateType.Maximum);

            // Dinorant: speed, 6 % damage, 5 % less damage taken.
            Set(3, Stats.MovementSpeed, MountSpeed, AggregateType.Maximum);
            Set(3, Stats.MovementSpeedUnderwater, MountSpeed, AggregateType.Maximum);
            Set(3, Stats.DamageReceiveDecrement, 0.95f, AggregateType.Multiplicate);
            Set(3, Stats.AttackDamageIncrease, 1.06f, AggregateType.Multiplicate);

            // Dark Horse: speed (its Earthshake skill is the real value).
            Set(4, Stats.MovementSpeed, MountSpeed, AggregateType.Maximum);
            Set(4, Stats.MovementSpeedUnderwater, MountSpeed, AggregateType.Maximum);

            // Fenrir: speed; the colour options (black +10 % damage, blue -10 % damage taken) stay as they are.
            Set(37, Stats.MovementSpeed, MountSpeed, AggregateType.Maximum);
            Set(37, Stats.MovementSpeedUnderwater, MountSpeed, AggregateType.Maximum);

            // Demon: 8 % damage, +5 attack speed (instead of the +40 % weapon damage of MU).
            Repurpose(64, Stats.MinimumPhysBaseDmg, Stats.AttackDamageIncrease, 1.08f, AggregateType.Multiplicate);
            Clear(64, Stats.MaximumPhysBaseDmg, Stats.MinimumWizBaseDmg, Stats.MaximumWizBaseDmg, Stats.MinimumCurseBaseDmg, Stats.MaximumCurseBaseDmg);
            Set(64, Stats.AttackSpeedAny, 5f, AggregateType.AddRaw);

            // Spirit of Guardian: 8 % less damage taken, +3 % life.
            Set(65, Stats.DamageReceiveDecrement, 0.92f, AggregateType.Multiplicate);
            Set(65, Stats.MaximumHealth, 75f, AggregateType.AddRaw);

            // Panda: +8 % EXP, 3 % less damage taken.
            Set(80, Stats.BonusExperienceRate, 0.08f, AggregateType.AddRaw);
            Repurpose(80, Stats.DefenseFinal, Stats.DamageReceiveDecrement, 0.97f, AggregateType.Multiplicate);

            // Unicorn: +8 % gold, 3 % less damage taken.
            Set(106, Stats.MoneyAmountRate, 1.08f, AggregateType.Multiplicate);
            Repurpose(106, Stats.DefenseFinal, Stats.DamageReceiveDecrement, 0.97f, AggregateType.Multiplicate);

            // Skeleton: 4 % damage, +5 % EXP.
            Repurpose(123, Stats.MinimumPhysBaseDmg, Stats.AttackDamageIncrease, 1.04f, AggregateType.Multiplicate);
            Clear(123, Stats.MaximumPhysBaseDmg, Stats.MinimumWizBaseDmg, Stats.MaximumWizBaseDmg, Stats.MinimumCurseBaseDmg, Stats.MaximumCurseBaseDmg, Stats.AttackSpeedAny);
            Set(123, Stats.BonusExperienceRate, 0.05f, AggregateType.AddRaw);

            _configured = true;
        }
    }

    /// <summary>
    /// The appearance of a champion with the pets it switched on from the inventory: the Dark Horse takes the place of the item in
    /// the pet slot and the Dark Raven the one of the left hand (the shield), as if they were equipped.
    /// </summary>
    /// <param name="player">The champion.</param>
    /// <param name="equipped">The appearance of the equipped items.</param>
    /// <returns>The appearance to show.</returns>
    public static IEnumerable<ItemAppearance> ApplyActivePets(Player player, List<ItemAppearance> equipped)
    {
        if (!ActiveByPlayer.TryGetValue(player, out var active))
        {
            return equipped;
        }

        void Place(Item? item, byte slot)
        {
            if (item is null)
            {
                return;
            }

            equipped.RemoveAll(a => a.ItemSlot == slot);
            var appearance = item.GetAppearance();
            appearance.ItemSlot = slot;
            equipped.Add(appearance);
        }

        Place(active.Horse, PetSlotOfHorse);
        Place(active.Raven, PetSlotOfRaven);
        return equipped;
    }

    /// <summary>
    /// Whether the item is a Dark Horse / Dark Raven of a MOBA champion which the appearance packets must treat as worn: the slot it
    /// shows in and whether it is switched on now.
    /// </summary>
    /// <param name="player">The champion the packet is about.</param>
    /// <param name="item">The item of the packet.</param>
    /// <param name="slot">The slot the pet shows in.</param>
    /// <param name="active">Whether it is switched on.</param>
    /// <returns><see langword="true"/> for a pet which is not equipped.</returns>
    public static bool TryGetShownSlot(Player player, Item item, out byte slot, out bool active)
    {
        slot = 0;
        active = false;
        if (!player.IsMobaClone
            || item.Definition is not { Group: 13 } definition
            || (definition.Number != HorseNumber && definition.Number != RavenNumber)
            || player.Inventory is not { } inventory
            || inventory.EquippedItems.Contains(item))
        {
            return false;
        }

        slot = definition.Number == HorseNumber ? PetSlotOfHorse : PetSlotOfRaven;
        active = ActiveByPlayer.TryGetValue(player, out var pets) && (definition.Number == HorseNumber ? pets.Horse == item : pets.Raven == item);
        return true;
    }

    /// <summary>Whether the Dark Horse of the champion is switched on (from the inventory).</summary>
    /// <param name="player">The champion.</param>
    /// <returns><see langword="true"/> while it works.</returns>
    public static bool IsHorseActive(Player player) => ActiveByPlayer.TryGetValue(player, out var active) && active.Horse is not null;

    /// <summary>Whether the Dark Raven of the champion is switched on (from the inventory).</summary>
    /// <param name="player">The champion.</param>
    /// <returns><see langword="true"/> while it works.</returns>
    public static bool IsRavenActive(Player player) => ActiveByPlayer.TryGetValue(player, out var active) && active.Raven is not null;

    /// <summary>
    /// The right click of a Dark Horse or Dark Raven in the inventory: the pet works from the inventory (it keeps no equipment
    /// slot, so the Dark Lord can wear a shield and another pet) until the player clicks it again. The Horse gives its speed and
    /// the Earthshake skill; the Raven attacks with its owner.
    /// </summary>
    /// <param name="player">The champion.</param>
    /// <param name="slot">The inventory slot of the pet.</param>
    /// <returns>A task.</returns>
    public static async ValueTask ToggleFromInventoryAsync(Player player, byte slot)
    {
        var slotItem = player.Inventory?.GetItem(slot);
        player.Logger.LogInformation(
            "[MOBA-PET] \"{Name}\" right-clicked slot {Slot}: item {Item}, champion {Clone}",
            player.SelectedCharacter?.Name,
            slot,
            slotItem is null ? "none" : $"{slotItem.Definition?.Name} ({slotItem.Definition?.Group},{slotItem.Definition?.Number})",
            player.IsMobaClone);

        if (!player.IsMobaClone || player.Inventory is not { } inventory || player.Attributes is not { } attributes)
        {
            return;
        }

        if (inventory.GetItem(slot) is not { Definition: { Group: 13 } definition } item
            || (definition.Number != HorseNumber && definition.Number != RavenNumber))
        {
            return;
        }

        if (MobaPassives.FamilyOf(player) != MobaFamily.DarkLord)
        {
            await player.ShowBlueMessageAsync("[MOBA] Solo el Dark Lord puede usar esta mascota.").ConfigureAwait(false);
            return;
        }

        if (inventory.EquippedItems.Contains(item))
        {
            await player.ShowBlueMessageAsync("[MOBA] Ya la llevas equipada: guárdala en el inventario y actívala con clic derecho para liberar el slot.").ConfigureAwait(false);
            return;
        }

        var active = ActiveByPlayer.GetOrCreateValue(player);
        if (definition.Number == HorseNumber)
        {
            if (active.Horse is not null)
            {
                await DeactivateHorseAsync(player, active).ConfigureAwait(false);
                await ShowAppearanceAsync(player, item).ConfigureAwait(false);
                await player.ShowBlueMessageAsync("[MOBA] Dark Horse desactivado.").ConfigureAwait(false);
                return;
            }

            active.Horse = item;
            active.HorseSpeed = new SimpleElement(MountSpeed, AggregateType.Maximum);
            active.HorseSpeedUnderwater = new SimpleElement(MountSpeed, AggregateType.Maximum);
            attributes.AddElement(active.HorseSpeed, Stats.MovementSpeed);
            attributes.AddElement(active.HorseSpeedUnderwater, Stats.MovementSpeedUnderwater);
            active.HorseSkill = definition.Skill ?? player.GameContext.Configuration.Skills.FirstOrDefault(s => s.Number == EarthshakeSkill);
            if (active.HorseSkill is { } skill && player.SkillList is SkillList skillList)
            {
                await skillList.AddPetSkillAsync(skill).ConfigureAwait(false);
            }

            await ShowAppearanceAsync(player, item).ConfigureAwait(false);
            await player.ShowBlueMessageAsync("[MOBA] Dark Horse activado: más velocidad y la skill Terremoto. Clic derecho otra vez para desactivarlo.").ConfigureAwait(false);
            return;
        }

        if (active.Raven is not null)
        {
            player.RemovePetCommandManager();
            active.Raven = null;
            await ShowAppearanceAsync(player, item).ConfigureAwait(false);
            await player.ShowBlueMessageAsync("[MOBA] Dark Raven desactivado.").ConfigureAwait(false);
            return;
        }

        active.Raven = item;
        await ShowAppearanceAsync(player, item).ConfigureAwait(false);
        var manager = new Pet.RavenCommandManager(player, item);
        player.UsePetCommandManager(manager);
        await manager.SetBehaviourAsync(PetBehaviour.AttackWithOwner, null).ConfigureAwait(false);
        await player.ShowBlueMessageAsync("[MOBA] Dark Raven activado: ataca junto a ti. Clic derecho otra vez para desactivarlo.").ConfigureAwait(false);
    }

    /// <summary>
    /// A pet switched on from the inventory stops working when its item leaves the inventory (sold, dropped, equipped).
    /// </summary>
    /// <param name="player">The champion.</param>
    /// <returns>A task.</returns>
    private static async ValueTask SyncActiveAsync(Player player)
    {
        if (!ActiveByPlayer.TryGetValue(player, out var active) || player.Inventory is not { } inventory)
        {
            return;
        }

        if (active.Horse is { } horse && (!inventory.Items.Contains(horse) || inventory.EquippedItems.Contains(horse)))
        {
            await DeactivateHorseAsync(player, active).ConfigureAwait(false);
            await ShowAppearanceAsync(player, horse).ConfigureAwait(false);
            await player.ShowBlueMessageAsync("[MOBA] Dark Horse desactivado.").ConfigureAwait(false);
        }

        if (active.Raven is { } raven && (!inventory.Items.Contains(raven) || inventory.EquippedItems.Contains(raven)))
        {
            player.RemovePetCommandManager();
            active.Raven = null;
            await ShowAppearanceAsync(player, raven).ConfigureAwait(false);
            await player.ShowBlueMessageAsync("[MOBA] Dark Raven desactivado.").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Tells everybody around (and the owner) how the pet slot of the champion looks now: the pet when it is switched on, otherwise
    /// the item which is really equipped there (or nothing).
    /// </summary>
    private static async ValueTask ShowAppearanceAsync(Player player, Item pet)
    {
        player.OnAppearanceChanged();
        var isHorse = pet.Definition?.Number == HorseNumber;
        var slot = isHorse ? PetSlotOfHorse : PetSlotOfRaven;
        var isActive = isHorse ? IsHorseActive(player) : IsRavenActive(player);

        // The pet itself (switched on: it is shown; off: its slot is cleared) ...
        await player.ForEachWorldObserverAsync<IAppearanceChangedPlugIn>(p => p.AppearanceChangedAsync(player, pet, isActive), true).ConfigureAwait(false);

        // ... and, when it is off, what is really equipped in that slot comes back.
        if (!isActive && player.Inventory?.GetItem(slot) is { } real && player.Inventory.EquippedItems.Contains(real))
        {
            await player.ForEachWorldObserverAsync<IAppearanceChangedPlugIn>(p => p.AppearanceChangedAsync(player, real, true), true).ConfigureAwait(false);
        }
    }

    private static async ValueTask DeactivateHorseAsync(Player player, ActivePets active)
    {
        if (player.Attributes is { } attributes)
        {
            if (active.HorseSpeed is { } speed)
            {
                attributes.RemoveElement(speed, Stats.MovementSpeed);
            }

            if (active.HorseSpeedUnderwater is { } underwater)
            {
                attributes.RemoveElement(underwater, Stats.MovementSpeedUnderwater);
            }
        }

        if (active.HorseSkill is { } skill)
        {
            await player.SkillList!.RemoveItemSkillAsync(skill.Number.ToUnsigned()).ConfigureAwait(false);
        }

        active.Horse = null;
        active.HorseSpeed = null;
        active.HorseSpeedUnderwater = null;
        active.HorseSkill = null;
    }

    /// <summary>
    /// Match tick: makes every Dark Lord with an equipped Dark Raven attack with its owner (the
    /// client would have to send the pet command otherwise).
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        try
        {
            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            foreach (var champion in players.Where(p => p.IsMobaClone))
            {
                await SyncActiveAsync(champion).ConfigureAwait(false);
            }

            foreach (var champion in players.Where(p => p.IsMobaClone && p.IsAlive))
            {
                if (champion.Inventory?.GetItem(InventoryConstants.RightHandSlot) is { } pet
                    && pet.IsTrainablePet()
                    && !ActivatedRavens.TryGetValue(pet, out _)
                    && champion.PetCommandManager is { } manager)
                {
                    ActivatedRavens.Add(pet, new object());
                    champion.Logger.LogInformation("[MOBA-RAVEN] {name}: Raven (lv {level}, slot {slot}) equipped, switching it to attack-with-owner.", champion.SelectedCharacter?.Name, pet.Level, pet.ItemSlot);
                    await manager.SetBehaviourAsync(PetBehaviour.AttackWithOwner, null).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // best effort
        }
    }
}
