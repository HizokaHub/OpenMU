// <copyright file="MobaStructures.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// The kind of a MOBA structure.
/// </summary>
public enum MobaStructureType
{
    /// <summary>Not a structure.</summary>
    None = 0,

    /// <summary>A lane turret.</summary>
    Turret = 1,

    /// <summary>The team nexus - destroying it wins the match.</summary>
    Nexus = 2,
}

/// <summary>
/// Process-wide, RAM-only marker for which match monsters are structures (turrets /
/// the nexus). Lane-creep targeting treats a structure as the lowest-priority target
/// and, once locked onto one, does not let champion-aggro pull it off.
/// </summary>
/// <remarks>
/// Backed by a <see cref="ConditionalWeakTable{TKey,TValue}"/> so entries vanish when
/// the structure monster is collected. A dedicated match context owning this comes later.
/// </remarks>
public static class MobaStructures
{
    private static readonly ConditionalWeakTable<object, object> TypeByMonster = new();

    /// <summary>Marks a monster as a structure of the given type.</summary>
    /// <param name="monster">The structure monster.</param>
    /// <param name="type">The structure type.</param>
    public static void Mark(object monster, MobaStructureType type) => TypeByMonster.AddOrUpdate(monster, type);

    /// <summary>Removes the structure marker from a monster.</summary>
    /// <param name="monster">The monster.</param>
    public static void Unmark(object monster) => TypeByMonster.Remove(monster);

    /// <summary>Gets the structure type of a monster, or <see cref="MobaStructureType.None"/>.</summary>
    /// <param name="monster">The monster.</param>
    /// <returns>The structure type.</returns>
    public static MobaStructureType GetStructureType(object? monster)
    {
        if (monster is not null && TypeByMonster.TryGetValue(monster, out var boxed) && boxed is MobaStructureType type)
        {
            return type;
        }

        return MobaStructureType.None;
    }

    /// <summary>Whether the monster is any kind of MOBA structure.</summary>
    /// <param name="monster">The monster.</param>
    /// <returns><see langword="true"/> if it is a turret or the nexus.</returns>
    public static bool IsStructure(object? monster) => GetStructureType(monster) != MobaStructureType.None;
}
