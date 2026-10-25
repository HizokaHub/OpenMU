// <copyright file="MobaPartyBuffs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Attributes;

/// <summary>
/// The buffs that reach "the party" in MU (Swell Life, Increase Critical Damage, the Rage Fighter's...) reach the whole
/// <b>team</b> in a match: the clones of a match form no party, so without this they only ever helped their caster.
/// Distance is respected like in a party: only the teammates within <see cref="RangeTiles"/> of the caster, alive.
/// </summary>
public static class MobaPartyBuffs
{
    /// <summary>How far from the caster a teammate still gets a party buff (about the viewport a party member needs to be in).</summary>
    public const int RangeTiles = 10;

    /// <summary>
    /// The targets of a party buff: the caster and the living teammates in range.
    /// </summary>
    /// <param name="caster">The champion who casts the buff.</param>
    /// <returns>The caster first, then its teammates in range.</returns>
    public static IList<IAttackable> TargetsOf(Player caster)
    {
        var targets = new List<IAttackable> { caster };
        if (caster.CurrentMap is { } map)
        {
            foreach (var other in map.GetAttackablesInRange(caster.Position, RangeTiles).OfType<Player>())
            {
                if (IsPartnerOf(caster, other))
                {
                    targets.Add(other);
                }
            }
        }

        // What Swell Life reads from a real party (+ life per nearby member).
        if (caster.Attributes is { } attributes)
        {
            attributes[Stats.NearbyPartyMemberCount] = targets.Count - 1;
        }

        return targets;
    }

    /// <summary>Whether the player counts as a party member of the caster: a living, different champion of the same team.</summary>
    /// <param name="caster">The champion who casts the buff.</param>
    /// <param name="other">The other player.</param>
    /// <returns><see langword="true"/> for a living teammate.</returns>
    public static bool IsPartnerOf(Player caster, Player other)
        => !ReferenceEquals(caster, other)
           && other.IsAlive
           && other.IsMobaClone
           && other is not MobaBotPlayer { IsDummy: true }
           && MobaTeams.AreAllies(caster, other);
}
