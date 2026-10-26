// <copyright file="MobaBorrowedSets.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Configuration.Items;

/// <summary>
/// The sets a class borrows from a related class (2026-10-25, decided with the user) so that every class is offered two sets in every tier: the
/// Magic Gladiator has a set of its own in each tier and, besides, a set of the Soul Master (to be a mage) or of the Blade Knight (to be a tank);
/// the Summoner (a mage) completes its third tier with a set of the Soul Master, and the Rage Fighter (a fighter) completes its second and third tiers
/// with sets of the Blade Knight. The borrowed set is the second set of the tier (the attack set, see <see cref="MobaArchetype"/>).
/// <para>
/// The items stay the ones of the lender: <see cref="EnsureConfigured"/> adds the classes of the borrower to the classes which may wear them (the
/// Magic Gladiator has no helm, so it only wears the other four pieces).
/// </para>
/// </summary>
public static class MobaBorrowedSets
{
    private const byte HelmGroup = 7;

    /// <summary>The borrowed sets: the class family that wears it, the set number (the same for the five pieces) and its tier.</summary>
    public static readonly IReadOnlyList<(MobaFamily Borrower, short SetNumber, MobaShopTier Tier)> All = new[]
    {
        (MobaFamily.MagicGladiator, (short)3, MobaShopTier.T1),   // Legendary (Soul Master): the mage Magic Gladiator
        (MobaFamily.MagicGladiator, (short)17, MobaShopTier.T2),  // Dark Phoenix (Blade Knight): the tank Magic Gladiator
        (MobaFamily.MagicGladiator, (short)30, MobaShopTier.T3),  // Venom Mist (Soul Master)
        (MobaFamily.Summoner, (short)30, MobaShopTier.T3),        // Venom Mist (Soul Master)
        (MobaFamily.RageFighter, (short)17, MobaShopTier.T2),     // Dark Phoenix (Blade Knight)
        (MobaFamily.RageFighter, (short)29, MobaShopTier.T3),     // Dragon Knight (Blade Knight)
    };

    private static readonly object ConfigLock = new();

    private static GameConfiguration? _configuredFor;

    /// <summary>
    /// Lets the classes of the borrowers wear the borrowed pieces (in the configuration in memory), once per configuration.
    /// </summary>
    /// <param name="configuration">The game configuration.</param>
    public static void EnsureConfigured(GameConfiguration configuration)
    {
        if (ReferenceEquals(_configuredFor, configuration))
        {
            return;
        }

        lock (ConfigLock)
        {
            if (ReferenceEquals(_configuredFor, configuration))
            {
                return;
            }

            foreach (var (borrower, setNumber, _) in All)
            {
                var classes = configuration.CharacterClasses.Where(c => MobaPassives.FamilyOf(c.Number) == borrower).ToList();
                for (var group = HelmGroup; group <= MobaArchetype.LastPieceGroup; group++)
                {
                    if (borrower == MobaFamily.MagicGladiator && group == HelmGroup)
                    {
                        continue;
                    }

                    if (configuration.Items.FirstOrDefault(d => d.Group == group && d.Number == setNumber) is { } definition)
                    {
                        Allow(definition, classes);
                    }
                }
            }

            _configuredFor = configuration;
        }
    }

    private static void Allow(ItemDefinition definition, IEnumerable<CharacterClass> classes)
    {
        foreach (var characterClass in classes)
        {
            if (!definition.QualifiedCharacters.Contains(characterClass))
            {
                definition.QualifiedCharacters.Add(characterClass);
            }
        }
    }
}
