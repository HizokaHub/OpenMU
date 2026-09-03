// <copyright file="MobaVampAndSpecial.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.Interfaces;

/// <summary>
/// Applied on every deliberate champion hit (see <see cref="MobaPassives.OnHitResolved"/>):
/// heals the attacker for a fraction of the damage dealt (life steal / spell vamp), and
/// tacks on true damage as a fraction of the victim's HP for a few anti-tank skills.
/// </summary>
public static class MobaVampAndSpecial
{
    /// <summary>Applies life steal and special (true / %HP) damage for one resolved hit.</summary>
    /// <param name="attacker">The attacking champion.</param>
    /// <param name="victim">The victim.</param>
    /// <param name="skill">The skill used, or <c>null</c> for a basic attack.</param>
    /// <param name="hit">The resolved hit info.</param>
    public static void Apply(Player attacker, IAttackable victim, Skill? skill, HitInfo hit)
    {
        var dealt = hit.HealthDamage + hit.ShieldDamage;
        if (dealt == 0)
        {
            return;
        }

        // Life steal / spell vamp.
        var vamp = MobaCombatStats.VampOf(attacker, skill is not null);
        if (vamp > 0 && attacker.Attributes is { } a && attacker.IsAlive)
        {
            var heal = (float)(dealt * vamp);
            var max = a[Stats.MaximumHealth];
            var cur = a[Stats.CurrentHealth];
            if (cur < max)
            {
                var applied = Math.Min(max, cur + heal) - cur;
                a[Stats.CurrentHealth] = cur + applied;
                MobaTelemetry.NoteHeal(attacker, applied, skill is null ? "lifesteal(basic)" : $"vamp({skill.Name})");
            }
        }

        // Special damage: true damage vs the victim's HP, bypassing armour. Only for a
        // MOBA-champion victim and only for the listed skills.
        if (skill is { } s && victim is Player { IsMobaClone: true } champVictim && champVictim.IsAlive)
        {
            var (maxHpFrac, curHpFrac) = MobaCombatStats.SpecialDamageOf((short)s.Number);
            if (maxHpFrac > 0 || curHpFrac > 0)
            {
                var vAttr = champVictim.Attributes;
                var extra = (maxHpFrac * (vAttr?[Stats.MaximumHealth] ?? 0))
                            + (curHpFrac * (vAttr?[Stats.CurrentHealth] ?? 0));
                var trueDmg = (uint)Math.Max(1, extra);
                if (extra >= 1)
                {
                    _ = champVictim.ApplyPoisonDamageAsync(attacker, trueDmg);
                    champVictim.Logger.LogInformation(
                        "[MOBA-DMG] \"{Attacker}\" -> \"{Victim}\" | \"{Skill}#{Num}\" | true={True} (special)",
                        attacker.SelectedCharacter?.Name,
                        champVictim.SelectedCharacter?.Name,
                        s.Name,
                        s.Number,
                        trueDmg);
                }
            }
        }
    }
}
