// <copyright file="MobaHybridSurgePassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// Magic Gladiator passive "Impulso híbrido": alternating attack types feed each other.
/// After a spell hit, the next basic attack within <see cref="Window"/> deals
/// <see cref="EmpoweredBasicBonus"/> extra damage; after a basic hit, the next skill
/// within <see cref="Window"/> costs <see cref="SpellManaMultiplier"/> of its mana.
/// </summary>
/// <remarks>Magnitudes are first-pass (balance pass).</remarks>
public static class MobaHybridSurgePassive
{
    /// <summary>Extra fraction of health damage on the empowered basic attack.</summary>
    private const float EmpoweredBasicBonus = 0.40f;

    /// <summary>Mana-cost multiplier for the discounted skill (0.70 = -30%).</summary>
    private const float SpellManaMultiplier = 0.70f;

    private static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private static readonly ConditionalWeakTable<Player, State> States = new();

    /// <summary>Arms the empowered basic after a Magic Gladiator spell hit.</summary>
    /// <param name="gladiator">The Magic Gladiator champion.</param>
    public static void OnSpellHit(Player gladiator)
    {
        if (!gladiator.IsMobaClone)
        {
            return;
        }

        States.GetOrCreateValue(gladiator).EmpoweredBasicUntil = DateTime.UtcNow + Window;
    }

    /// <summary>Consumes the empowered basic (if armed) and arms the cheap-spell window.</summary>
    /// <param name="gladiator">The Magic Gladiator champion.</param>
    /// <param name="victim">The victim of the basic attack.</param>
    /// <param name="hit">The hit info.</param>
    public static void OnBasicHit(Player gladiator, IAttackable victim, HitInfo hit)
    {
        if (!gladiator.IsMobaClone)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var state = States.GetOrCreateValue(gladiator);

        if (hit.HealthDamage > 0
            && now < state.EmpoweredBasicUntil
            && victim.IsAlive
            && !MobaTeams.AreAllies(gladiator, victim))
        {
            state.EmpoweredBasicUntil = default;
            var bonus = (uint)Math.Max(1, hit.HealthDamage * EmpoweredBasicBonus);
            _ = victim.ApplyPoisonDamageAsync(gladiator, bonus);
        }

        state.CheapSpellUntil = now + Window;
    }

    /// <summary>
    /// Returns the mana-cost multiplier for the champion's next skill, consuming the
    /// cheap-spell window if it is active. 1.0 when there is no discount.
    /// </summary>
    /// <param name="champion">The champion about to cast.</param>
    /// <returns>0.70 while the window is open, otherwise 1.0.</returns>
    public static float ConsumeSpellManaMultiplier(Player champion)
    {
        if (!States.TryGetValue(champion, out var state) || DateTime.UtcNow >= state.CheapSpellUntil)
        {
            return 1f;
        }

        state.CheapSpellUntil = default;
        return SpellManaMultiplier;
    }

    private sealed class State
    {
        public DateTime EmpoweredBasicUntil;

        public DateTime CheapSpellUntil;
    }
}
