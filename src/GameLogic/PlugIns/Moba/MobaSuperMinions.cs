// <copyright file="MobaSuperMinions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The super-minions: when an inhibitor is down the enemy team sends a big, tough creep with every wave down that lane
/// (see <see cref="MobaInhibitors.SuperMinionsFor"/>). They are lane creeps with <see cref="HealthFactor"/> times the life, <see cref="DamageFactor"/> times
/// the damage and <see cref="DefenseFactor"/> times the defense of a normal creep of the same moment, drawn <see cref="SizePercent"/> % as big, and worth
/// <see cref="RewardFactor"/> creeps when they die.
/// </summary>
public static class MobaSuperMinions
{
    /// <summary>Life factor over a normal creep.</summary>
    public const float HealthFactor = 5f;

    /// <summary>Damage factor over a normal creep.</summary>
    public const float DamageFactor = 2.5f;

    /// <summary>Defense factor over a normal creep.</summary>
    public const float DefenseFactor = 2f;

    /// <summary>Visual size, in percent of the normal size of the model (the clients scale it with the packet of the growing dragons).</summary>
    public const byte SizePercent = 135;

    /// <summary>How many creeps a super-minion is worth when it dies (EXP, gold and summon points).</summary>
    public const int RewardFactor = 3;

    /// <summary>Monster number (Giant) of the super-minions of the blue team.</summary>
    public const short BlueModel = 7;

    /// <summary>Monster number (Death Knight) of the super-minions of the red team.</summary>
    public const short RedModel = 40;

    private static readonly ConditionalWeakTable<Monster, object> Marks = new();

    private static readonly Point ArenaCentre = new(128, 128);

    /// <summary>The monster number of the super-minion of a team.</summary>
    /// <param name="team">The team that sends it.</param>
    /// <returns>The monster number.</returns>
    public static short ModelOf(MobaTeam team) => team == MobaTeam.Red ? RedModel : BlueModel;

    /// <summary>Whether a monster number is the model of a super-minion.</summary>
    /// <param name="number">The monster number.</param>
    /// <returns><c>true</c> for the blue or the red model.</returns>
    public static bool IsSuperModel(short number) => number is BlueModel or RedModel;

    /// <summary>Marks a monster as a super-minion.</summary>
    /// <param name="monster">The monster.</param>
    public static void Mark(Monster monster) => Marks.AddOrUpdate(monster, true);

    /// <summary>Whether a monster is a super-minion.</summary>
    /// <param name="monster">The monster (or anything).</param>
    /// <returns><c>true</c> if it was marked.</returns>
    public static bool IsSuper(object? monster) => monster is Monster m && Marks.TryGetValue(m, out _);

    /// <summary>The living super-minions of a map with their size in percent, for the clients (the packet of the growing dragons carries it).</summary>
    /// <param name="map">The map.</param>
    /// <returns>Object id and size percent.</returns>
    public static IReadOnlyList<(ushort Key, byte Percent)> Sizes(GameMap map)
        => map.GetAttackablesInRange(ArenaCentre, 255)
            .OfType<Monster>()
            .Where(m => m.IsAlive && IsSuper(m))
            .Select(m => (m.Id, SizePercent))
            .ToList();
}
