// <copyright file="MobaBotPlayer.Buffs.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;

/// <summary>
/// The buffs of the bots (2026-10-25): they cast the buffs of their class like a human would, through the same skill handler
/// (mana, cooldown, range, effect and animation), instead of only attacking.
/// </summary>
public sealed partial class MobaBotPlayer
{
    /// <summary>A bot keeps its long buffs up while it has at least this much mana (they are expensive).</summary>
    private const float BuffUpkeepMana = 0.6f;

    /// <summary>A bot casts its fight buffs while it has at least this much mana.</summary>
    private const float FightBuffMana = 0.3f;

    /// <summary>The range of the buffs that go to a teammate (the Elf's skills reach 6 tiles).</summary>
    private const double AllyBuffTiles = 6;

    /// <summary>An ally with less life than this gets healed.</summary>
    private const float HealBelowHp = 0.6f;

    /// <summary>An ally with less shield than this gets Recovery.</summary>
    private const float RecoveryBelowShield = 0.4f;

    /// <summary>Berserker costs 40 % of the life: only above this life fraction.</summary>
    private const float BerserkerMinHp = 0.6f;

    /// <summary>Defense (the Blade Knight's 50 % cut, which lasts 4 s) is raised below this life fraction in a fight.</summary>
    private const float DefenseBelowHp = 0.65f;

    private static readonly TargetedSkillDefaultPlugin DefaultSkillPlugin = new();

    /// <summary>Buffs that last minutes (Infinity Arrow 10, Expansion of Wizardry 30): kept up out of a fight.</summary>
    private static readonly short[] UpkeepBuffs = { 77, 233 };

    /// <summary>Self buffs raised when enemies are near or the bot is fighting: Soul Barrier, Swell Life, Increase Critical Damage, Damage Reflection, the Rage Fighter's three and the Rune Wizard's Burst and Haste.</summary>
    private static readonly short[] FightBuffs = { 16, 48, 64, 217, 266, 267, 268, 633, 634 };

    private static readonly TimeSpan BuffRetry = TimeSpan.FromSeconds(4);

    private readonly Dictionary<short, DateTime> _buffRetryUtc = new();

    /// <summary>
    /// Casts at most one buff per call: the class buffs on itself and, for the Elf, on its teammates in reach.
    /// </summary>
    /// <param name="c">The context of the tick.</param>
    /// <returns><see langword="true"/> if a buff was cast.</returns>
    private async ValueTask<bool> TickBuffsAsync(BotContext c)
    {
        if (!this.IsAlive || this.SkillList is null || this.Attributes is not { } a || MobaRecall.IsChanneling(this))
        {
            return false;
        }

        var mana = Math.Clamp(a[Stats.CurrentMana] / Math.Max(1f, a[Stats.MaximumMana]), 0f, 1f);
        var fight = c.InCombat || c.EnemyChampsNear.Count > 0;

        // Healing first: a teammate (or the bot itself) in trouble beats keeping a buff up.
        if (mana >= FightBuffMana && this.Has(26))
        {
            var hurt = this.AlliesInReach(c).Where(p => LifeOf(p) < HealBelowHp).OrderBy(LifeOf).FirstOrDefault();
            if (hurt is not null && await this.CastBuffAsync(26, hurt, c.Now).ConfigureAwait(false))
            {
                return true;
            }
        }

        if (mana >= FightBuffMana && this.Has(234))
        {
            var drained = this.AlliesInReach(c).Where(p => ShieldOf(p) < RecoveryBelowShield).OrderBy(ShieldOf).FirstOrDefault();
            if (drained is not null && await this.CastBuffAsync(234, drained, c.Now).ConfigureAwait(false))
            {
                return true;
            }
        }

        if (fight && mana >= FightBuffMana)
        {
            if (this.Has(18) && c.HpPct < DefenseBelowHp && await this.CastBuffAsync(18, this, c.Now).ConfigureAwait(false))
            {
                return true;
            }

            foreach (var number in FightBuffs)
            {
                if (this.Has(number) && await this.CastBuffAsync(number, this, c.Now).ConfigureAwait(false))
                {
                    return true;
                }
            }

            if (this.Has(218) && c.HpPct >= BerserkerMinHp && await this.CastBuffAsync(218, this, c.Now).ConfigureAwait(false))
            {
                return true;
            }

            // The Elf's Greater Damage / Greater Defense: itself and the teammates in reach.
            foreach (var number in new short[] { 28, 27 })
            {
                if (!this.Has(number))
                {
                    continue;
                }

                foreach (var who in this.AlliesInReach(c))
                {
                    if (await this.CastBuffAsync(number, who, c.Now).ConfigureAwait(false))
                    {
                        return true;
                    }
                }
            }
        }

        if (!fight && mana >= BuffUpkeepMana)
        {
            foreach (var number in UpkeepBuffs)
            {
                if (this.Has(number) && await this.CastBuffAsync(number, this, c.Now).ConfigureAwait(false))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static float LifeOf(Player p)
        => p.Attributes is { } a ? a[Stats.CurrentHealth] / Math.Max(1f, a[Stats.MaximumHealth]) : 1f;

    private static float ShieldOf(Player p)
        => p.Attributes is { } a && a[Stats.MaximumShield] > 0 ? a[Stats.CurrentShield] / a[Stats.MaximumShield] : 1f;

    private static bool HasEffect(IAttackable who, Skill skill)
    {
        if (skill.MagicEffectDef is not { } effect)
        {
            return false;
        }

        try
        {
            return who.MagicEffectList.ActiveEffects.ContainsKey(effect.Number);
        }
        catch (InvalidOperationException)
        {
            // The expiry timers mutate the list: a torn read counts as "active", the next tick retries.
            return true;
        }
    }

    private bool Has(short skillNumber) => this.SkillList?.GetSkill((ushort)skillNumber)?.Skill is not null;

    /// <summary>The bot itself and the living teammates within reach of the Elf's buffs.</summary>
    private IEnumerable<Player> AlliesInReach(BotContext c)
        => c.AllAllies.Where(p => p.IsAlive && p.GetDistanceTo(c.Pos) <= AllyBuffTiles).Prepend(this);

    private async ValueTask<bool> CastBuffAsync(short number, Player target, DateTime now)
    {
        if (this.SkillList?.GetSkill((ushort)number) is not { Skill: { } skill }
            || this._buffRetryUtc.TryGetValue(number, out var retryAt) && now < retryAt
            || MobaCooldowns.IsOnCooldown(this, number, now)
            || !target.IsActive())
        {
            return false;
        }

        // A buff that leaves an effect is only recast when the target lacks it (Heal and Recovery act at once).
        if (skill.MagicEffectDef is not null && skill.SkillType == SkillType.Buff && HasEffect(target, skill))
        {
            return false;
        }

        var manaBefore = this.Attributes?[Stats.CurrentMana] ?? 0f;
        var strategy = this.GameContext.PlugInManager.GetStrategy<short, ITargetedSkillPlugin>(number) ?? DefaultSkillPlugin;
        await strategy.PerformSkillAsync(this, target, (ushort)number).ConfigureAwait(false);

        var cast = (this.Attributes?[Stats.CurrentMana] ?? 0f) < manaBefore;
        if (!cast)
        {
            // Refused (safezone, range, vision, mana): do not hammer the handler every second.
            this._buffRetryUtc[number] = now + BuffRetry;
        }
        else
        {
            this.Logger.LogInformation(
                "[MOBA-BOT-BUFF] \"{Name}\" cast {Skill}#{Number} on {Target}.",
                this.Name,
                skill.Name,
                number,
                ReferenceEquals(target, this) ? "itself" : target.Name);
        }

        return cast;
    }
}
