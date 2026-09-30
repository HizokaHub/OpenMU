// <copyright file="MobaCreepHold.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.GameLogic.NPC;

/// <summary>
/// Keeps a lane minion still for a while. The teleport scroll pins the minion a champion is channelling
/// toward, so the arrival point does not move under the champion's feet.
/// </summary>
public static class MobaCreepHold
{
    private static readonly ConditionalWeakTable<Monster, Holder> Holds = new();

    /// <summary>Makes the minion stand still for the given time.</summary>
    /// <param name="minion">The minion.</param>
    /// <param name="duration">How long it holds.</param>
    public static void Hold(Monster minion, TimeSpan duration) => Holds.GetOrCreateValue(minion).Until = DateTime.UtcNow + duration;

    /// <summary>Lets the minion move again.</summary>
    /// <param name="minion">The minion.</param>
    public static void Release(Monster minion)
    {
        if (Holds.TryGetValue(minion, out var holder))
        {
            holder.Until = DateTime.MinValue;
        }
    }

    /// <summary>Gets whether the minion is being held in place.</summary>
    /// <param name="minion">The minion.</param>
    /// <returns><see langword="true"/> while held.</returns>
    public static bool IsHeld(Monster minion) => Holds.TryGetValue(minion, out var holder) && holder.Until > DateTime.UtcNow;

    private sealed class Holder
    {
        public DateTime Until;
    }
}
