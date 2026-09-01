// <copyright file="MobaSpiritEchoPassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// Summoner passive "Eco espiritual": every <see cref="EveryNthSpell"/> spell hit fires
/// a spectral bolt at the same target for extra magic damage.
/// </summary>
/// <remarks>
/// Counts spell hits (≈ casts for the single-target Summoner kit). The bolt is a
/// follow-up <c>ApplyPoisonDamageAsync</c>. Magnitudes are first-pass (balance pass).
/// </remarks>
public static class MobaSpiritEchoPassive
{
    /// <summary>Number of spell hits between bolts.</summary>
    private const int EveryNthSpell = 4;

    /// <summary>Bolt damage as a fraction of the triggering hit's health damage.</summary>
    private const float BoltFraction = 0.60f;

    private static readonly ConditionalWeakTable<Player, Counter> Counters = new();

    /// <summary>Registers a Summoner champion's spell hit and fires the bolt on every 4th.</summary>
    /// <param name="summoner">The Summoner champion.</param>
    /// <param name="victim">The enemy that got hit.</param>
    /// <param name="hit">The hit info.</param>
    public static void OnSpellHit(Player summoner, IAttackable victim, HitInfo hit)
    {
        if (!summoner.IsMobaClone || hit.HealthDamage == 0 || !victim.IsAlive
            || ReferenceEquals(summoner, victim) || MobaTeams.AreAllies(summoner, victim))
        {
            return;
        }

        var counter = Counters.GetOrCreateValue(summoner);
        counter.Count++;
        if (counter.Count % EveryNthSpell != 0)
        {
            return;
        }

        var bolt = (uint)Math.Max(1, hit.HealthDamage * BoltFraction);
        _ = victim.ApplyPoisonDamageAsync(summoner, bolt);
    }

    private sealed class Counter
    {
        public int Count;
    }
}
