// <copyright file="MobaDamageDirection.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Tells a human champion where the damage it takes comes from (<c>C1 08 D5 34 attackerX attackerY percent flags</c>), so the client draws a red wedge
/// around the champion pointing at the attacker. The notices of one champion are at least <see cref="MinimumGapMilliseconds"/> apart: the damage that
/// lands in between is added to the next one.
/// </summary>
public static class MobaDamageDirection
{
    /// <summary>Least time between two notices to the same champion, in milliseconds.</summary>
    public const int MinimumGapMilliseconds = 120;

    /// <summary>Flag: the damage has no direction (poison, burning...).</summary>
    public const byte FlagNoDirection = 1;

    /// <summary>Flag: only the shield took the damage.</summary>
    public const byte FlagShieldOnly = 2;

    private static readonly ConditionalWeakTable<Player, Pending> States = new();

    /// <summary>Notes a hit that landed on a champion and, when due, tells the champion.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="victim">The champion that was hit.</param>
    /// <param name="hit">The hit.</param>
    /// <returns>A task.</returns>
    public static async ValueTask NoteAsync(IAttacker attacker, Player victim, HitInfo hit)
    {
        if (!victim.IsMobaClone || victim is MobaBotPlayer)
        {
            return;
        }

        var total = (double)hit.HealthDamage + hit.ShieldDamage;
        if (total <= 0)
        {
            return;
        }

        var maximum = victim.Attributes?[Stats.MaximumHealth] ?? 0f;
        var percent = maximum > 0 ? 100.0 * total / maximum : 1.0;
        var state = States.GetOrCreateValue(victim);
        var origin = attacker as ILocateable;
        bool send;
        byte flags;
        double due;
        lock (state)
        {
            state.Percent += percent;
            state.ShieldOnly = state.ShieldOnly && hit.HealthDamage <= 0;
            state.NoDirection = state.NoDirection && ((hit.Attributes & DamageAttributes.Poison) != 0 || origin is null);
            send = (DateTime.UtcNow - state.LastSentUtc).TotalMilliseconds >= MinimumGapMilliseconds;
            due = state.Percent;
            flags = (byte)((state.NoDirection ? FlagNoDirection : 0) | (state.ShieldOnly ? FlagShieldOnly : 0));
            if (send)
            {
                state.LastSentUtc = DateTime.UtcNow;
                state.Percent = 0;
                state.ShieldOnly = true;
                state.NoDirection = true;
            }
        }

        if (!send)
        {
            return;
        }

        var position = origin?.Position ?? victim.Position;
        var amount = (byte)Math.Clamp((int)Math.Ceiling(due), 1, 100);
        await victim.InvokeViewPlugInAsync<IMobaDamageDirectionPlugIn>(p => p.ShowDamageAsync(position.X, position.Y, amount, flags)).ConfigureAwait(false);
    }

    private sealed class Pending
    {
        public DateTime LastSentUtc = DateTime.MinValue;

        public double Percent;

        public bool ShieldOnly = true;

        public bool NoDirection = true;
    }
}
