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

    /// <summary>Gets the display names of the categories (menu order = enum order).</summary>
    public static IReadOnlyList<string> CategoryNames { get; } = new[]
    {
        "Armas",
        "Sets",
        "Sets (recursos)",
        "Alas",
        "Accesorios",
        "Buffs / Consumibles",
    };

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

        // A set = the pieces helm (7) .. boots (11) with the same number; missing pieces
        // (e.g. Magic Gladiator sets have no helm) are skipped when resolving.
        // Sets: 2 mixes (standard tanky / aggressive reflect) at T1 and T2, one at T3, on the
        // "Sets" page; the resource mix of T1/T2 goes on its own "Sets (recursos)" page (a
        // set is 22 cells and the merchant grid holds 120, so 3 mixes x 2 tiers + T3 don't fit).
        void Set(MobaFamily family, short setNumber, MobaShopTier tier)
        {
            var variants = tier == MobaShopTier.T3
                ? new[] { MobaShopVariant.Standard }
                : new[] { MobaShopVariant.Standard, MobaShopVariant.Aggressive };
            foreach (var variant in variants)
            {
                for (var group = HelmGroup; group <= BootsGroup; group++)
                {
                    entries.Add(new MobaShopEntry(MobaShopCategory.Sets, new[] { family }, group, setNumber, tier, Variant: variant));
                }
            }

            if (tier != MobaShopTier.T3)
            {
                for (var group = HelmGroup; group <= BootsGroup; group++)
                {
                    entries.Add(new MobaShopEntry(MobaShopCategory.SetsSustain, new[] { family }, group, setNumber, tier, Variant: MobaShopVariant.Sustain));
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

        // --- Sets ---
        Set(MobaFamily.Wizard, 3, MobaShopTier.T1);                // Legendary
        Set(MobaFamily.Wizard, 18, MobaShopTier.T2);               // Grand Soul
        Set(MobaFamily.Wizard, 30, MobaShopTier.T3);               // Venom Mist
        Set(MobaFamily.Knight, 1, MobaShopTier.T1);                // Dragon
        Set(MobaFamily.Knight, 17, MobaShopTier.T2);               // Dark Phoenix
        Set(MobaFamily.Knight, 29, MobaShopTier.T3);               // Dragon Knight
        Set(MobaFamily.Elf, 14, MobaShopTier.T1);                  // Guardian
        Set(MobaFamily.Elf, 19, MobaShopTier.T2);                  // Divine
        Set(MobaFamily.Elf, 31, MobaShopTier.T3);                  // Sylphid Ray
        Set(MobaFamily.MagicGladiator, 15, MobaShopTier.T1);       // Storm Crow
        Set(MobaFamily.MagicGladiator, 20, MobaShopTier.T2);       // Thunder Hawk
        Set(MobaFamily.MagicGladiator, 32, MobaShopTier.T3);       // Volcano
        Set(MobaFamily.DarkLord, 26, MobaShopTier.T1);             // Adamantine
        Set(MobaFamily.DarkLord, 27, MobaShopTier.T2);             // Dark Steel
        Set(MobaFamily.DarkLord, 33, MobaShopTier.T3);             // Sunlight
        Set(MobaFamily.Summoner, 41, MobaShopTier.T1);             // Ancient
        Set(MobaFamily.Summoner, 42, MobaShopTier.T2);             // Black Rose
        Set(MobaFamily.Summoner, 43, MobaShopTier.T3);             // Aura
        Set(MobaFamily.RageFighter, 60, MobaShopTier.T1);          // Storm Hard
        Set(MobaFamily.RageFighter, 61, MobaShopTier.T2);          // Piercing
        Set(MobaFamily.RageFighter, 73, MobaShopTier.T3);          // Phoenix Soul

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

        // --- Buffs / consumables (every class) ---
        void Consumable(short number, MobaShopTier tier, byte level = 0, byte quantity = 1)
            => entries.Add(new MobaShopEntry(MobaShopCategory.Consumables, null, 14, number, tier, level, quantity));

        Consumable(1, MobaShopTier.T1, quantity: 10);   // Small Healing Potion
        Consumable(4, MobaShopTier.T1, quantity: 10);   // Small Mana Potion
        Consumable(35, MobaShopTier.T1, quantity: 10);  // Small Shield Potion
        Consumable(8, MobaShopTier.T1, quantity: 3);    // Antidote
        Consumable(2, MobaShopTier.T2, quantity: 10);   // Medium Healing Potion
        Consumable(5, MobaShopTier.T2, quantity: 10);   // Medium Mana Potion
        Consumable(39, MobaShopTier.T2, quantity: 10);  // Medium Complex Potion
        Consumable(9, MobaShopTier.T2, quantity: 3);    // Ale (attack speed buff)
        Consumable(3, MobaShopTier.T3, quantity: 10);   // Large Healing Potion
        Consumable(6, MobaShopTier.T3, quantity: 10);   // Large Mana Potion
        Consumable(40, MobaShopTier.T3, quantity: 10);  // Large Complex Potion
        Consumable(7, MobaShopTier.T3, level: 0, quantity: 3); // Potion of Bless (damage buff)
        Consumable(7, MobaShopTier.T3, level: 1, quantity: 3); // Potion of Soul (attack speed buff)

        return entries;
    }
}
