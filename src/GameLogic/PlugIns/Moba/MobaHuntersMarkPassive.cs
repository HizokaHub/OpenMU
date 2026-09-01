// <copyright file="MobaHuntersMarkPassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// Elf passive "Marca del cazador": the Elf's basic attack marks an enemy for
/// <see cref="Duration"/>; while marked, every champion on the Elf's team that hits it
/// deals <see cref="BonusFraction"/> extra damage (applied as a small follow-up hit).
/// </summary>
/// <remarks>
/// The bonus is a follow-up <c>ApplyPoisonDamageAsync</c> rather than a true damage
/// multiplier (cosmetically a small extra tick). Magnitudes are first-pass.
/// </remarks>
public static class MobaHuntersMarkPassive
{
    /// <summary>Extra fraction of the hit's health damage dealt to a marked target.</summary>
    private const float BonusFraction = 0.08f;

    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(4);

    private static readonly ConditionalWeakTable<IAttackable, Mark> Marks = new();

    /// <summary>Marks an enemy hit by the Elf's basic attack.</summary>
    /// <param name="elf">The Elf champion.</param>
    /// <param name="victim">The enemy that got hit.</param>
    public static void OnBasicHit(Player elf, IAttackable victim)
    {
        var team = MobaTeams.GetTeam(elf);
        if (team == MobaTeam.None || ReferenceEquals(elf, victim) || MobaTeams.AreAllies(elf, victim) || !victim.IsAlive)
        {
            return;
        }

        var mark = Marks.GetOrCreateValue(victim);
        mark.Team = team;
        mark.ExpiresUtc = DateTime.UtcNow + Duration;
    }

    /// <summary>
    /// Applies the marked-target bonus for any champion on the marking team. Call for
    /// every champion hit, regardless of class.
    /// </summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <param name="victim">The victim.</param>
    /// <param name="hit">The hit info.</param>
    public static void OnAnyChampionHit(Player attacker, IAttackable victim, HitInfo hit)
    {
        if (hit.HealthDamage == 0 || !victim.IsAlive)
        {
            return;
        }

        if (!Marks.TryGetValue(victim, out var mark)
            || mark.ExpiresUtc <= DateTime.UtcNow
            || MobaTeams.GetTeam(attacker) != mark.Team)
        {
            return;
        }

        var bonus = (uint)Math.Max(1, hit.HealthDamage * BonusFraction);
        _ = victim.ApplyPoisonDamageAsync(attacker, bonus);
    }

    private sealed class Mark
    {
        public MobaTeam Team;

        public DateTime ExpiresUtc;
    }
}
