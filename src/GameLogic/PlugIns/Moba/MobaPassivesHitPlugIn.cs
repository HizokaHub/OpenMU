// <copyright file="MobaPassivesHitPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Routes every hit dealt by a MOBA champion clone to <see cref="MobaPassives"/>, which
/// applies the attacker's class passive (e.g. the Rage Fighter attack-speed ramp).
/// </summary>
[PlugIn]
[Display(Name = "MOBA: champion passives", Description = "Applies per-class champion passives on hit.")]
[Guid("2E9B7A41-6C3D-4F58-9A02-7B1C5E8D4F63")]
public class MobaPassivesHitPlugIn : IAttackableGotHitPlugIn
{
    /// <inheritdoc />
    public void AttackableGotHit(IAttackable attackable, IAttacker attacker, HitInfo hitInfo)
    {
        if (ReferenceEquals(attackable, attacker))
        {
            return;
        }

        if (attacker is Player { IsMobaClone: true } champion)
        {
            MobaPassives.OnChampionDealtHit(champion, attackable, hitInfo);
        }

        if (attackable is Player { IsMobaClone: true } victim)
        {
            MobaPassives.OnChampionGotHit(victim, attacker, hitInfo);
        }
    }
}
