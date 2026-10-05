// <copyright file="MobaStructures.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.NPC;

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

    private static readonly ConditionalWeakTable<object, HitWindow> Windows = new();

    /// <summary>
    /// Champion damage against a structure is divided by the champion's level scale (<see cref="MobaProgression.DamageScaleFor"/>,
    /// 1x at level 1 to 15x at level 30): the structures' life is sized for level-1-scale hits, and what stays of the hit is
    /// the build's own bonus (stats / tree / items), so a late champion no longer deletes a turret in one or two hits.
    /// </summary>
    /// <param name="champion">The attacking champion.</param>
    /// <param name="damage">The hit before the normalisation.</param>
    /// <returns>The damage the structure takes.</returns>
    public static int NormalizeChampionDamage(Player champion, int damage)
        => damage <= 1 ? damage : Math.Max(1, (int)(damage / (Math.Max(1.0, MobaProgression.DamageScaleFor(champion)) * LevelExtraDivisor(champion.MobaLevel))));

    /// <summary>
    /// Extra divisor beyond the level scale: dividing by the level scale alone left the damage to structures growing ~16x between
    /// level 5 and 17 (140 -> 2,300 per hit, 340 -> 4,200 dps for two bots; basic attacks, skill ranks and the build bonus grow too),
    /// so a nexus fell in about a minute at level 17. (level / 5)^1.8 from level 5 flattens it back to a mild growth.
    /// </summary>
    /// <param name="level">The champion level.</param>
    /// <returns>The divisor, 1 up to level 5.</returns>
    public static double LevelExtraDivisor(int level) => Math.Pow(Math.Max(1.0, level / 5.0), 1.8);

    /// <summary>Accumulates the champion hits on a structure and writes a <c>[MOBA-STRUCT-DMG]</c> line every 10 s.</summary>
    /// <param name="structure">The structure.</param>
    /// <param name="champion">The attacker.</param>
    /// <param name="before">The damage before the normalisation.</param>
    /// <param name="after">The damage the structure takes.</param>
    public static void NoteChampionHit(Monster structure, Player champion, int before, int after)
    {
        var window = Windows.GetOrCreateValue(structure);
        lock (window)
        {
            window.Hits++;
            window.Before += before;
            window.After += after;
            window.MaxLevel = Math.Max(window.MaxLevel, champion.MobaLevel);
            var now = Environment.TickCount64;
            if (window.Start == 0)
            {
                window.Start = now;
            }

            if (now - window.Start < 10_000)
            {
                return;
            }

            var seconds = Math.Max(1.0, (now - window.Start) / 1000.0);
            champion.Logger.LogInformation(
                "[MOBA-STRUCT-DMG] {Type} {Hits} champion hits in {Sec:F0}s: raw {Before} -> taken {After} ({Dps:F0} dps) up to Lv{Lvl} | HP {Hp:F0}/{Max:F0}",
                GetStructureType(structure),
                window.Hits,
                seconds,
                window.Before,
                window.After,
                window.After / seconds,
                window.MaxLevel,
                structure.Attributes[Attributes.Stats.CurrentHealth],
                structure.Attributes[Attributes.Stats.MaximumHealth]);
            window.Hits = 0;
            window.Before = 0;
            window.After = 0;
            window.MaxLevel = 0;
            window.Start = now;
        }
    }

    private sealed class HitWindow
    {
        public int Hits;

        public long Before;

        public long After;

        public int MaxLevel;

        public long Start;
    }
}
