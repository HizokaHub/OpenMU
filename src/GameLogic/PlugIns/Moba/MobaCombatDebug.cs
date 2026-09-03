// <copyright file="MobaCombatDebug.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Verbose combat tracing for MOBA balance testing: logs every champion-involved hit -
/// what skill, how much damage, or why nothing landed. Toggle with <see cref="Enabled"/>
/// (on by default while the mode is being built).
/// </summary>
public static class MobaCombatDebug
{
    /// <summary>Gets or sets a value indicating whether the trace is written.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Logs a hit that landed on a MOBA participant.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="victim">The victim (a champion).</param>
    /// <param name="skill">The skill used, or <c>null</c> for a basic attack.</param>
    /// <param name="hit">The resulting hit info.</param>
    /// <param name="isCombo">Whether the hit was part of a combo.</param>
    public static void LogHit(IAttacker attacker, Player victim, Skill? skill, HitInfo hit, bool isCombo = false)
    {
        if (!Enabled || !IsMobaInvolved(attacker, victim))
        {
            return;
        }

        victim.Logger.LogInformation(
            "[MOBA-DMG] {Attacker} -> {Victim} | {Skill} | hp-dmg={HpDmg} sd-dmg={SdDmg} {Attrs}",
            Name(attacker),
            victim.Name,
            skill is null ? "basic" : $"{skill.Name}#{skill.Number}",
            hit.HealthDamage,
            hit.ShieldDamage,
            hit.Attributes);

        // Rich PvP trace ([MOBA-DMG+], [MOBA-FIGHT], [MOBA-BURST], running totals).
        MobaTelemetry.NoteHit(attacker, victim, skill, hit, isCombo);
    }

    /// <summary>Logs an attack on a champion that produced no damage, with the reason.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="victim">The victim (a champion).</param>
    /// <param name="skill">The skill used, or <c>null</c>.</param>
    /// <param name="reason">Why it did nothing (safezone, pvp-disabled, miss, ...).</param>
    public static void LogNoDamage(IAttacker attacker, Player victim, Skill? skill, string reason)
    {
        if (!Enabled || !IsMobaInvolved(attacker, victim))
        {
            return;
        }

        victim.Logger.LogInformation(
            "[MOBA-DMG] {Attacker} -> {Victim} | {Skill} | NO DAMAGE ({Reason})",
            Name(attacker),
            victim.Name,
            skill is null ? "basic" : $"{skill.Name}#{skill.Number}",
            reason);
    }

    private static bool IsMobaInvolved(IAttacker attacker, Player victim)
        => victim.IsMobaClone || (attacker as Player)?.IsMobaClone == true;

    private static string Name(IAttacker attacker)
    {
        try
        {
            return attacker.GetName();
        }
        catch
        {
            return attacker.GetType().Name;
        }
    }
}
