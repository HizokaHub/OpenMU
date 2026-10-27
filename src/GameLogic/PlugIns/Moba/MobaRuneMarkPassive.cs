// <copyright file="MobaRuneMarkPassive.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// Rune Wizard passive "Marca rúnica": every spell hit leaves a rune on the target; the <see cref="HitsToDetonate"/>th spell hit on the
/// same target within <see cref="MarkWindowSeconds"/> seconds makes the runes explode for extra magic damage.
/// </summary>
/// <remarks>
/// The explosion is a follow-up <c>ApplyPoisonDamageAsync</c> (like the Summoner's echo), which the passive dispatcher ignores, so it never chains.
/// Magnitudes are first-pass (balance pass).
/// </remarks>
public static class MobaRuneMarkPassive
{
    /// <summary>Spell hits on the same target which make the runes explode.</summary>
    public const int HitsToDetonate = 3;

    /// <summary>Seconds without a spell hit on the target after which its runes fade.</summary>
    public const double MarkWindowSeconds = 5;

    /// <summary>Explosion damage as a fraction of the triggering hit's health damage.</summary>
    private const float DetonationFraction = 0.55f;

    private static readonly ConditionalWeakTable<Player, Mark> Marks = new();

    /// <summary>Registers a Rune Wizard's spell hit and makes the runes explode on the third.</summary>
    /// <param name="runeWizard">The Rune Wizard champion.</param>
    /// <param name="victim">The enemy that got hit.</param>
    /// <param name="hit">The hit info.</param>
    public static void OnSpellHit(Player runeWizard, IAttackable victim, HitInfo hit)
    {
        if (!runeWizard.IsMobaClone || hit.HealthDamage == 0 || !victim.IsAlive
            || ReferenceEquals(runeWizard, victim) || MobaTeams.AreAllies(runeWizard, victim))
        {
            return;
        }

        if (!NextHitDetonates(runeWizard, victim, DateTime.UtcNow))
        {
            return;
        }

        var explosion = (uint)Math.Max(1, hit.HealthDamage * DetonationFraction);
        _ = victim.ApplyPoisonDamageAsync(runeWizard, explosion);
    }

    /// <summary>
    /// Counts a spell hit on a target and tells whether it is the one which makes the runes explode (the counter then starts again).
    /// </summary>
    /// <param name="runeWizard">The Rune Wizard champion.</param>
    /// <param name="victim">The enemy that got hit.</param>
    /// <param name="now">The time of the hit.</param>
    /// <returns><see langword="true"/> if the runes explode.</returns>
    internal static bool NextHitDetonates(Player runeWizard, IAttackable victim, DateTime now)
    {
        var mark = Marks.GetOrCreateValue(runeWizard);
        if (!ReferenceEquals(mark.Target, victim) || (now - mark.LastHit).TotalSeconds > MarkWindowSeconds)
        {
            mark.Target = victim;
            mark.Count = 0;
        }

        mark.LastHit = now;
        mark.Count++;
        if (mark.Count < HitsToDetonate)
        {
            return false;
        }

        mark.Target = null;
        mark.Count = 0;
        return true;
    }

    private sealed class Mark
    {
        public IAttackable? Target;

        public int Count;

        public DateTime LastHit;
    }
}
