// <copyright file="MobaCombatLogPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Feeds <see cref="MobaCombatLog"/>: records every hit where the attacker or the
/// victim is a MOBA match participant (has a <see cref="MobaTeam"/>).
/// </summary>
[PlugIn]
[Display(Name = "MOBA: combat log", Description = "Records recent hits between MOBA participants for creep targeting.")]
[Guid("1C7E9A34-8B2F-4D50-9E61-5A0C2F8B7D46")]
public class MobaCombatLogPlugIn : IAttackableGotHitPlugIn
{
    /// <inheritdoc />
    public void AttackableGotHit(IAttackable attackable, IAttacker attacker, HitInfo hitInfo)
    {
        if (ReferenceEquals(attackable, attacker))
        {
            return;
        }

        if (MobaTeams.GetTeam(attackable) == MobaTeam.None && MobaTeams.GetTeam(attacker) == MobaTeam.None)
        {
            return;
        }

        MobaCombatLog.Record(attacker, attackable);
    }
}
