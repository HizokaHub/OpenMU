// <copyright file="MobaPassives.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using MUnique.OpenMU.DataModel.Configuration;

/// <summary>Champion class families for the MOBA mode (mirrors <see cref="MobaLoadouts"/>).</summary>
public enum MobaFamily
{
    /// <summary>Dark Wizard / Soul Master / Grand Master.</summary>
    Wizard,

    /// <summary>Dark Knight / Blade Knight / Blade Master.</summary>
    Knight,

    /// <summary>Fairy Elf / Muse Elf / High Elf.</summary>
    Elf,

    /// <summary>Magic Gladiator / Duel Master.</summary>
    MagicGladiator,

    /// <summary>Dark Lord / Lord Emperor.</summary>
    DarkLord,

    /// <summary>Summoner / Bloody Summoner / Dimension Master.</summary>
    Summoner,

    /// <summary>Rage Fighter / Fist Master.</summary>
    RageFighter,
}

/// <summary>
/// Per-class champion passives for the MOBA mode - one always-on effect per family
/// (see the design conversation). Built incrementally; this is the shared entry point:
/// family resolution, the single "a hit was resolved" dispatch, and the per-tick upkeep.
/// </summary>
public static class MobaPassives
{
    /// <summary>Resolves a champion's <see cref="MobaFamily"/> from its class.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The family.</returns>
    public static MobaFamily FamilyOf(Player champion)
        => FamilyOf(champion.SelectedCharacter?.CharacterClass?.Number ?? 0);

    /// <summary>Resolves a <see cref="MobaFamily"/> from a class number.</summary>
    /// <param name="classNumber">The character class number.</param>
    /// <returns>The family.</returns>
    public static MobaFamily FamilyOf(byte classNumber) => classNumber switch
    {
        0 or 2 or 3 => MobaFamily.Wizard,
        4 or 6 or 7 => MobaFamily.Knight,
        8 or 10 or 11 => MobaFamily.Elf,
        12 or 13 => MobaFamily.MagicGladiator,
        16 or 17 => MobaFamily.DarkLord,
        20 or 22 or 23 => MobaFamily.Summoner,
        24 or 25 => MobaFamily.RageFighter,
        _ => MobaFamily.Wizard,
    };

    /// <summary>
    /// Called from the hit path (<c>Player.HitAsync</c> / <c>AttackableNpcBase.HitAsync</c>)
    /// once a hit has landed. Routes to the passives of the attacking / defending champion.
    /// </summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="victim">The thing that got hit.</param>
    /// <param name="skill">The skill used, or <c>null</c> for a basic attack.</param>
    /// <param name="hit">The hit info.</param>
    public static void OnHitResolved(IAttacker attacker, IAttackable victim, Skill? skill, HitInfo hit)
    {
        // Only react to deliberate attacks / spells - not DoT ticks (burn, poison, bleed),
        // which also route through HitAsync and would otherwise chain the passives.
        if (hit.Attributes.HasFlag(DamageAttributes.Poison) || hit.Attributes.HasFlag(DamageAttributes.Reflected))
        {
            return;
        }

        if (attacker is Player { IsMobaClone: true } champion && !ReferenceEquals(attacker, victim))
        {
            // Universal LoL-style mechanics: life steal / spell vamp, and true / %HP
            // special damage on a few anti-tank skills.
            MobaVampAndSpecial.Apply(champion, victim, skill, hit);

            switch (FamilyOf(champion))
            {
                case MobaFamily.RageFighter:
                    // A skill hit may consume an armed stun before this hit adds a stack.
                    if (skill is not null)
                    {
                        MobaFrenzyPassive.OnSkillHit(champion, victim);
                    }

                    MobaFrenzyPassive.OnHit(champion);
                    break;
                case MobaFamily.Knight:
                    MobaSecondWindPassive.OnDealtHit(champion, hit);
                    break;
                case MobaFamily.Wizard when skill is not null:
                    MobaCombustionPassive.OnSpellHit(champion, victim, hit);
                    break;
                case MobaFamily.Elf when skill is null:
                    MobaHuntersMarkPassive.OnBasicHit(champion, victim);
                    break;
                case MobaFamily.MagicGladiator when skill is not null:
                    MobaHybridSurgePassive.OnSpellHit(champion);
                    break;
                case MobaFamily.MagicGladiator:
                    MobaHybridSurgePassive.OnBasicHit(champion, victim, hit);
                    break;
                case MobaFamily.Summoner when skill is not null:
                    MobaSpiritEchoPassive.OnSpellHit(champion, victim, hit);
                    break;
            }

            // Team-wide: an Elf's Hunter's Mark makes the whole team hit harder.
            MobaHuntersMarkPassive.OnAnyChampionHit(champion, victim, hit);
        }

        if (victim is Player { IsMobaClone: true } defender && !ReferenceEquals(attacker, victim))
        {
            switch (FamilyOf(defender))
            {
                case MobaFamily.Knight:
                    MobaSecondWindPassive.OnGotHit(defender);
                    break;
            }
        }
    }

    /// <summary>
    /// Per-match upkeep for the passives (expiring stacking buffs, ticking DoTs, the
    /// Dark Lord command aura). Cheap; meant to be called about once a second from
    /// <see cref="MobaMatchTickPlugIn"/>.
    /// </summary>
    /// <param name="gameContext">The game context (for the aura's proximity scan).</param>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        // Periodic ticks can overlap on the thread pool if one runs long; the passive
        // stores (ConditionalWeakTables, buff lists) are not built for concurrent use.
        if (Interlocked.Exchange(ref _tickRunning, 1) == 1)
        {
            return;
        }

        try
        {
            MobaFrenzyPassive.SweepExpired();
            MobaSecondWindPassive.SweepExpired();
            await MobaCombustionPassive.TickAsync().ConfigureAwait(false);
            await MobaCommandBannerPassive.TickAsync(gameContext).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _tickRunning, 0);
        }
    }

    private static int _tickRunning;
}
