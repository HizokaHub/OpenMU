// <copyright file="MobaShopCatalog.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The (provisional) MOBA shop catalog. Each class family gets a T1 / T2 / T3 progression
/// per category; the price of every item comes from <see cref="MobaShop.PriceOf"/>, never
/// from a hand-assigned value. Entries whose item definition doesn't exist are skipped.
/// </summary>
public static class MobaShopCatalog
{
    private const byte HelmGroup = 7;
    private const byte BootsGroup = 11;

    private static readonly MobaShopVariant[] SetVariantOrder =
    {
        MobaShopVariant.Standard, MobaShopVariant.Bulwark, MobaShopVariant.Aggressive, MobaShopVariant.Utility, MobaShopVariant.Sustain,
    };

    /// <summary>Units of a recovery potion sold per pack.</summary>
    public const byte PotionPackSize = 100;

    /// <summary>Item level of the recovery potions per tier (+7 / +8 / +9): each level adds recovery and shortens the recovery time.</summary>
    /// <param name="tier">The tier.</param>
    /// <returns>The item level.</returns>
    public static byte PotionLevelOf(MobaShopTier tier) => tier switch
    {
        MobaShopTier.T1 => 7,
        MobaShopTier.T2 => 8,
        _ => 9,
    };

    /// <summary>
    /// Gets the pages of the vendor menu, in menu order. A page is one merchant grid (8x15 = 120
    /// cells) and can share several catalog categories, each starting on its own row, top to
    /// bottom in the listed order. Cells used by the widest class (max over classes, plus the
    /// rows lost to starting each category on a fresh row): consumables + accessories + wings
    /// about 62, weapons + resource sets about 110, sets about 110.
    /// </summary>
    public static IReadOnlyList<MobaShopPage> Pages { get; } = new[]
    {
        new MobaShopPage("Consumibles, Accesorios y Alas", new[] { MobaShopCategory.Consumables, MobaShopCategory.Accessories, MobaShopCategory.Wings }),
        new MobaShopPage("Armas", new[] { MobaShopCategory.Weapons }),
        new MobaShopPage("Sets - Tier 1", new[] { MobaShopCategory.Sets }, MobaShopTier.T1),
        new MobaShopPage("Sets - Tier 2", new[] { MobaShopCategory.Sets }, MobaShopTier.T2),
        new MobaShopPage("Sets - Tier 3", new[] { MobaShopCategory.Sets }, MobaShopTier.T3),
        new MobaShopPage("Mascotas y Escudos", new[] { MobaShopCategory.Pets, MobaShopCategory.Offhand }),
    };

    /// <summary>The page of the first tier of sets (the pages of tier 2 and 3 follow).</summary>
    public const int FirstSetsPage = 2;

    /// <summary>The page which opens when the player talks to the vendor (the client starts on its "Para ti" tab, which needs any page open behind it).</summary>
    public const int DefaultPage = 0;

    /// <summary>The page of the weapons.</summary>
    public const int WeaponsPage = 1;

    /// <summary>The page of the wings (it shares the first page with the consumables and the accessories).</summary>
    public const int WingsPage = 0;

    /// <summary>The page of the shields and the pets.</summary>
    public const int OffhandPage = 5;

    /// <summary>Gets all entries of the catalog.</summary>
    public static IReadOnlyList<MobaShopEntry> Entries { get; } = Build();

    private static IReadOnlyList<MobaShopEntry> Build()
    {
        var entries = new List<MobaShopEntry>();

        // Variants only matter where a tier doesn't already take every option: T1 (3 of 6) and
        // T2 (4 of 6) get several mixes of the same piece, T3 (all 6) is a single entry.
        // Weapons: 3 mixes (standard / crit + speed / sustain) at T1 and T2. The T3 weapon may
        // have a second, genuinely different item ("alt") from the same drop-level band.
        void Weapon(MobaFamily family, byte group, short number, MobaShopTier tier, short? altNumber = null, byte? altGroup = null)
        {
            var variants = tier == MobaShopTier.T3
                ? new[] { MobaShopVariant.Standard }
                : new[] { MobaShopVariant.Standard, MobaShopVariant.Aggressive, MobaShopVariant.Sustain };
            foreach (var variant in variants)
            {
                entries.Add(new MobaShopEntry(MobaShopCategory.Weapons, new[] { family }, group, number, tier, Variant: variant));
            }

            if (altNumber is { } alt)
            {
                entries.Add(new MobaShopEntry(MobaShopCategory.Weapons, new[] { family }, altGroup ?? group, alt, tier));
            }
        }

        // Wings are sold "full option" (max level, luck, +4 option and their one wing option),
        // one entry per wing and tier - no variants.
        void Wing(MobaFamily family, byte group, short number, MobaShopTier tier)
            => entries.Add(new MobaShopEntry(MobaShopCategory.Wings, new[] { family }, group, number, tier));

        // Off-hand: one shield (or book, for the Summoner) per tier. The Elf (bow + arrows) and the
        // Rage Fighter (gloves) can't carry one.
        void Offhand(MobaFamily family, byte group, short number, MobaShopTier tier)
            => entries.Add(new MobaShopEntry(MobaShopCategory.Offhand, new[] { family }, group, number, tier));

        // A set = the pieces helm (7) .. boots (11) with the same number; missing pieces
        // (e.g. Magic Gladiator sets have no helm) are skipped when resolving.
        // Every set of a class is sold ONCE (2026-10-27, the user did not want the same set repeated with other options): the sets of a class and
        // tier are listed in order and take the option mixes in this order - Standard, Bulwark, Aggressive, Utility, Sustain. Standard / Bulwark /
        // Aggressive / Utility / Sustain all go on the "Sets - Tier N" page (2026-10-28: there is no page of resource sets any more; a set is 24 cells and the merchant grid holds
        // 120). Tier 3 lists two sets (Standard, Bulwark). The same set number is always in the same tier for every class that wears it.
        void Sets(MobaFamily family, MobaShopTier tier, params short[] setNumbers)
        {
            for (var index = 0; index < setNumbers.Length; index++)
            {
                var variant = SetVariantOrder[index];
                var category = MobaShopCategory.Sets;
                for (var group = HelmGroup; group <= BootsGroup; group++)
                {
                    entries.Add(new MobaShopEntry(category, new[] { family }, group, setNumbers[index], tier, Variant: variant));
                }
            }
        }

        // --- Weapons ---
        Weapon(MobaFamily.Wizard, 5, 5, MobaShopTier.T1);          // Legendary Staff
        Weapon(MobaFamily.Wizard, 5, 9, MobaShopTier.T2);          // Dragon Soul Staff
        Weapon(MobaFamily.Wizard, 5, 12, MobaShopTier.T3, altNumber: 33);   // Grand Viper Staff / Chromatic Staff
        Weapon(MobaFamily.Knight, 0, 16, MobaShopTier.T1);         // Sword of Destruction
        Weapon(MobaFamily.Knight, 0, 26, MobaShopTier.T2);         // Flamberge
        Weapon(MobaFamily.Knight, 0, 22, MobaShopTier.T3, altNumber: 20);   // Bone Blade / Knight Blade
        Weapon(MobaFamily.Elf, 4, 6, MobaShopTier.T1);             // Chaos Nature Bow
        Weapon(MobaFamily.Elf, 4, 17, MobaShopTier.T2);            // Celestial Bow
        Weapon(MobaFamily.Elf, 4, 24, MobaShopTier.T3, altNumber: 21);      // Air Lyn Bow / Sylph Wind Bow
        Weapon(MobaFamily.MagicGladiator, 0, 31, MobaShopTier.T1); // Rune Blade
        Weapon(MobaFamily.MagicGladiator, 0, 25, MobaShopTier.T2); // Sword Dancer
        Weapon(MobaFamily.MagicGladiator, 0, 23, MobaShopTier.T3, altNumber: 21); // Explosion Blade / Dark Reign Blade
        Weapon(MobaFamily.DarkLord, 2, 11, MobaShopTier.T1);       // Lord Scepter
        Weapon(MobaFamily.DarkLord, 2, 15, MobaShopTier.T2);       // Shining Scepter
        Weapon(MobaFamily.DarkLord, 2, 18, MobaShopTier.T3, altNumber: 14); // Stryker Scepter / Soleil Scepter
        Weapon(MobaFamily.Summoner, 5, 17, MobaShopTier.T1);       // Ancient Stick
        Weapon(MobaFamily.Summoner, 5, 18, MobaShopTier.T2);       // Demonic Stick
        Weapon(MobaFamily.Summoner, 5, 19, MobaShopTier.T3);       // Storm Blitz Stick
        Weapon(MobaFamily.RageFighter, 0, 33, MobaShopTier.T1);    // Storm Hard Glove
        Weapon(MobaFamily.RageFighter, 0, 34, MobaShopTier.T2);    // Piercing Blade Glove
        Weapon(MobaFamily.RageFighter, 0, 35, MobaShopTier.T3);    // Phoenix Soul Star
        entries.Add(new MobaShopEntry(MobaShopCategory.Weapons, new[] { MobaFamily.Elf }, 4, 15, MobaShopTier.T1, Level: 0, Quantity: 255)); // Arrows

        // --- Off-hand (shields / books) ---
        Offhand(MobaFamily.Wizard, 6, 14, MobaShopTier.T1);           // Legendary Shield
        Offhand(MobaFamily.Wizard, 6, 15, MobaShopTier.T2);           // Grand Soul Shield
        Offhand(MobaFamily.Wizard, 6, 20, MobaShopTier.T3);           // Guardian Shield
        Offhand(MobaFamily.Knight, 6, 11, MobaShopTier.T1);           // Serpent Shield
        Offhand(MobaFamily.Knight, 6, 13, MobaShopTier.T2);           // Dragon Shield
        Offhand(MobaFamily.Knight, 6, 17, MobaShopTier.T3);           // Crimson Glory
        Offhand(MobaFamily.MagicGladiator, 6, 14, MobaShopTier.T1);   // Legendary Shield
        Offhand(MobaFamily.MagicGladiator, 6, 13, MobaShopTier.T2);   // Dragon Shield
        Offhand(MobaFamily.MagicGladiator, 6, 18, MobaShopTier.T3);   // Salamander Shield
        Offhand(MobaFamily.DarkLord, 6, 6, MobaShopTier.T1);          // Skull Shield
        Offhand(MobaFamily.DarkLord, 6, 8, MobaShopTier.T2);          // Tower Shield
        Offhand(MobaFamily.DarkLord, 6, 21, MobaShopTier.T3);         // Cross Shield
        Offhand(MobaFamily.Summoner, 5, 21, MobaShopTier.T1);         // Book of Sahamutt
        Offhand(MobaFamily.Summoner, 5, 22, MobaShopTier.T2);         // Book of Neil
        Offhand(MobaFamily.Summoner, 5, 23, MobaShopTier.T3);         // Book of Lagle

        // --- Sets (by class and tier: primary, second set, then the extra ones; the borrowed ones are listed in MobaBorrowedSets) ---
        // Wizard: Legendary, Eclipse, Sphinx, Bone, Pad / Grand Soul, Dark Soul / Venom Mist, Hades
        Sets(MobaFamily.Wizard, MobaShopTier.T1, 3, 35, 7, 4, 2);
        Sets(MobaFamily.Wizard, MobaShopTier.T2, 18, 22);
        Sets(MobaFamily.Wizard, MobaShopTier.T3, 30, 52);
        // Knight: Dragon, Ashcrow, Plate, Brass, Scale / Dark Phoenix, Black Dragon, Great Dragon, Brave / Dragon Knight, Titan
        Sets(MobaFamily.Knight, MobaShopTier.T1, 1, 34, 9, 8, 6);
        Sets(MobaFamily.Knight, MobaShopTier.T2, 17, 16, 21, 46);
        Sets(MobaFamily.Knight, MobaShopTier.T3, 29, 45);
        // Elf: Guardian, Iris, Spirit, Wind, Silk / Divine, Red Spirit, Faith / Sylphid Ray, Seraphim
        Sets(MobaFamily.Elf, MobaShopTier.T1, 14, 36, 13, 12, 11);
        Sets(MobaFamily.Elf, MobaShopTier.T2, 19, 24, 50);
        Sets(MobaFamily.Elf, MobaShopTier.T3, 31, 49);
        // Magic Gladiator: Storm Crow, Legendary (Soul Master), Plate, Brass, Dragon (Blade Knight) / Thunder Hawk, Dark Phoenix (Blade Knight), Valiant, Destory, Phantom / Volcano, Venom Mist (Soul Master)
        Sets(MobaFamily.MagicGladiator, MobaShopTier.T1, 15, 3, 9, 8, 1);
        Sets(MobaFamily.MagicGladiator, MobaShopTier.T2, 20, 17, 37, 47, 48);
        Sets(MobaFamily.MagicGladiator, MobaShopTier.T3, 32, 30);
        // Dark Lord: Adamantine, Light Plate, Scale, Leather, Bronze / Dark Steel, Glorious, Dark Master / Sunlight, Paewang
        Sets(MobaFamily.DarkLord, MobaShopTier.T1, 26, 25, 6, 5, 0);
        Sets(MobaFamily.DarkLord, MobaShopTier.T2, 27, 38, 28);
        Sets(MobaFamily.DarkLord, MobaShopTier.T3, 33, 51);
        // Summoner: Ancient, Red Wing, Mistery, Legendary + Eclipse (Soul Master) / Black Rose, Lilium, Grand Soul + Dark Soul (Soul Master) / Aura, Venom Mist (Soul Master)
        Sets(MobaFamily.Summoner, MobaShopTier.T1, 41, 40, 39, 3, 35);
        Sets(MobaFamily.Summoner, MobaShopTier.T2, 42, 44, 18, 22);
        Sets(MobaFamily.Summoner, MobaShopTier.T3, 43, 30);
        // Rage Fighter: Storm Hard, Sacred, Plate, Brass, Leather / Piercing, Dark Phoenix + Black Dragon + Great Dragon (Blade Knight) / Phoenix Soul, Dragon Knight (Blade Knight)
        Sets(MobaFamily.RageFighter, MobaShopTier.T1, 60, 59, 9, 8, 5);
        Sets(MobaFamily.RageFighter, MobaShopTier.T2, 61, 17, 16, 21);
        Sets(MobaFamily.RageFighter, MobaShopTier.T3, 73, 29);

        // --- Wings ---
        Wing(MobaFamily.Wizard, 12, 1, MobaShopTier.T1);           // Wings of Heaven
        Wing(MobaFamily.Wizard, 12, 4, MobaShopTier.T2);           // Wings of Soul
        Wing(MobaFamily.Wizard, 12, 37, MobaShopTier.T3);          // Wing of Eternal
        Wing(MobaFamily.Knight, 12, 2, MobaShopTier.T1);           // Wings of Satan
        Wing(MobaFamily.Knight, 12, 5, MobaShopTier.T2);           // Wings of Dragon
        Wing(MobaFamily.Knight, 12, 36, MobaShopTier.T3);          // Wing of Storm
        Wing(MobaFamily.Elf, 12, 0, MobaShopTier.T1);              // Wings of Elf
        Wing(MobaFamily.Elf, 12, 3, MobaShopTier.T2);              // Wings of Spirits
        Wing(MobaFamily.Elf, 12, 38, MobaShopTier.T3);             // Wing of Illusion
        Wing(MobaFamily.MagicGladiator, 12, 2, MobaShopTier.T1);   // Wings of Satan
        Wing(MobaFamily.MagicGladiator, 12, 6, MobaShopTier.T2);   // Wings of Darkness
        Wing(MobaFamily.MagicGladiator, 12, 39, MobaShopTier.T3);  // Wing of Ruin
        Wing(MobaFamily.DarkLord, 13, 30, MobaShopTier.T1);        // Cape of Lord
        Wing(MobaFamily.DarkLord, 13, 30, MobaShopTier.T2);        // Cape of Lord (+7, options)
        Wing(MobaFamily.DarkLord, 12, 40, MobaShopTier.T3);        // Cape of Emperor
        Wing(MobaFamily.Summoner, 12, 41, MobaShopTier.T1);        // Wings of Curse
        Wing(MobaFamily.Summoner, 12, 42, MobaShopTier.T2);        // Wings of Despair
        Wing(MobaFamily.Summoner, 12, 43, MobaShopTier.T3);        // Wing of Dimension
        Wing(MobaFamily.RageFighter, 12, 49, MobaShopTier.T1);     // Cape of Fighter
        Wing(MobaFamily.RageFighter, 12, 49, MobaShopTier.T2);     // Cape of Fighter (+7, options)
        Wing(MobaFamily.RageFighter, 12, 50, MobaShopTier.T3);     // Cape of Overrule

        // --- Accessories (every class) ---
        void Accessory(short number, MobaShopTier tier)
            => entries.Add(new MobaShopEntry(MobaShopCategory.Accessories, null, 13, number, tier));

        Accessory(8, MobaShopTier.T1);   // Ring of Ice
        Accessory(12, MobaShopTier.T1);  // Pendant of Lightning
        Accessory(23, MobaShopTier.T2);  // Ring of Wind
        Accessory(13, MobaShopTier.T2);  // Pendant of Fire
        Accessory(24, MobaShopTier.T3);  // Ring of Magic
        Accessory(28, MobaShopTier.T3);  // Pendant of Ability

        // --- Pets and mounts (one pet slot: a champion picks one). Numbers of every pet are rewritten
        // to MOBA values by MobaPets. Dark Horse (Earthshake) and Dark Raven only qualify for the Dark
        // Lord and are sold at their maximum pet level. ---
        void Pet(short number, MobaShopTier tier, byte level = 0, MobaShopVariant variant = MobaShopVariant.Standard)
            => entries.Add(new MobaShopEntry(MobaShopCategory.Pets, null, 13, number, tier, Level: level, Variant: variant));

        Pet(0, MobaShopTier.T1);    // Guardian Angel: 5 % defense, 3 % life
        Pet(1, MobaShopTier.T1);    // Imp: 5 % damage
        Pet(2, MobaShopTier.T1);    // Horn of Uniria: speed
        Pet(123, MobaShopTier.T2);  // Pet Skeleton: 4 % damage, 5 % EXP
        Pet(80, MobaShopTier.T2);   // Pet Panda: 8 % EXP, 3 % defense
        Pet(106, MobaShopTier.T2);  // Pet Unicorn: 8 % gold, 3 % defense
        Pet(3, MobaShopTier.T2);    // Horn of Dinorant: speed, 6 % damage, 5 % defense
        Pet(4, MobaShopTier.T2, MobaPets.MaxPetLevel);   // Dark Horse (DL): Earthshake, ~15 % of the DL's damage
        Pet(64, MobaShopTier.T3);   // Demon: 8 % damage, attack speed
        Pet(65, MobaShopTier.T3);   // Spirit of Guardian: 8 % defense, 3 % life
        Pet(37, MobaShopTier.T3, variant: MobaShopVariant.Aggressive);  // Black Fenrir: 10 % damage
        Pet(37, MobaShopTier.T3, variant: MobaShopVariant.Standard);    // Blue Fenrir: 10 % defense
        Pet(37, MobaShopTier.T3, variant: MobaShopVariant.Sustain);     // Gold Fenrir: life / mana / damage
        Pet(5, MobaShopTier.T3, MobaPets.MaxPetLevel);   // Dark Raven (DL): attacks with its owner, ~10 % of the DL's damage

        // --- Buffs / consumables (every class) ---
        void Consumable(short number, MobaShopTier tier, byte level = 0, byte quantity = 1)
            => entries.Add(new MobaShopEntry(MobaShopCategory.Consumables, null, 14, number, tier, level, quantity));

        void Potion(short number, MobaShopTier tier)
            => Consumable(number, tier, PotionLevelOf(tier), PotionPackSize);

        // Recovery potions come in packs of PotionPackSize at a fixed level per tier (+7 / +8 / +9: more recovery, faster).
        Potion(1, MobaShopTier.T1);   // Small Healing Potion
        Potion(4, MobaShopTier.T1);   // Small Mana Potion
        Potion(35, MobaShopTier.T1);  // Small Shield Potion
        Consumable(8, MobaShopTier.T1, quantity: 3);    // Antidote
        Potion(2, MobaShopTier.T2);   // Medium Healing Potion
        Potion(5, MobaShopTier.T2);   // Medium Mana Potion
        Potion(39, MobaShopTier.T2);  // Medium Complex Potion
        Consumable(9, MobaShopTier.T2, quantity: 3);    // Ale (attack speed buff)
        Consumable(10, MobaShopTier.T2, quantity: 1);   // Town Portal Scroll: teleport to an allied minion (MobaTeleport)
        Potion(3, MobaShopTier.T3);   // Large Healing Potion
        Potion(6, MobaShopTier.T3);   // Large Mana Potion
        Potion(40, MobaShopTier.T3);  // Large Complex Potion
        Consumable(7, MobaShopTier.T3, level: 0, quantity: 3); // Potion of Bless (damage buff)
        Consumable(7, MobaShopTier.T3, level: 1, quantity: 3); // Potion of Soul (attack speed buff)

        return entries;
    }
}
