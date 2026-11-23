// <copyright file="MobaRush.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// "Rush" (2026-11-22), the closing skill brought from Season 21 (S21 skill 813, a Blade Master master skill: "Rush to an enemy within 7 tiles and inflict a
/// powerful blow"): the champion jumps next to an enemy champion up to <see cref="RangeTiles"/> tiles away and hits it. It is the melee answer to the
/// range counters of the 1v1 (the diagnosis of 2026-11-22: GM beats BK 92 %, RF beats BK 92 %...).
/// <para>
/// PHASE 0: server only and without the database. The skill has no row in the skill table yet (F1), so the bots use it as part of their engage logic and
/// the hit is dealt with the skill entry of Lunge (20), which every Blade Knight learns, with <see cref="BlowFactor"/> on top. It is off by default
/// (<see cref="Enabled"/>, <c>MOBA_RUSH=1</c> or <c>/mobaf10 rush on</c>) until the duels validate it.
/// </para>
/// </summary>
public static class MobaRush
{
    /// <summary>The S21 skill number (virtual: it has no row in the configuration yet).</summary>
    public const short SkillNumber = 813;

    /// <summary>Lunge (the carrier of the hit in phase 0).</summary>
    private const short CarrierSkill = 20;

    /// <summary>The reach in tiles (the S21 tooltip says 7).</summary>
    public const int RangeTiles = 7;

    /// <summary>The closest the rush is worth it: inside this the champion is already in melee range.</summary>
    public const int MinTiles = 3;

    /// <summary>The cooldown in a match, in seconds (the game of S21 has 1 second, which would make it a permanent closing; 20 validated in duels of 10 s).</summary>
    public static double CooldownSeconds { get; set; } = 20;

    /// <summary>The "powerful blow": a factor on the damage of the carrier skill.</summary>
    public static double BlowFactor { get; set; } = 1.0;

    private static readonly ConditionalWeakTable<Player, Cooldown> Cooldowns = new();

    /// <summary>Gets or sets a value indicating whether the rush exists (default: environment variable <c>MOBA_RUSH=1</c>).</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("MOBA_RUSH") == "1";

    /// <summary>Whether the champion has the rush: the Knight family (Blade Knight; the Blade Master and the Rage Fighter are to be confirmed).</summary>
    /// <param name="champion">The champion.</param>
    /// <returns><see langword="true"/> if it can rush.</returns>
    public static bool Has(Player champion) => Enabled && champion.IsMobaClone && MobaPassives.FamilyOf(champion) == MobaFamily.Knight;

    /// <summary>Tries to rush at the target: jumps next to it and hits it. Does nothing (returns <see langword="false"/>) if it is not ready or not worth it.</summary>
    /// <param name="champion">The rushing champion.</param>
    /// <param name="target">The enemy.</param>
    /// <returns><see langword="true"/> if the champion rushed.</returns>
    public static async ValueTask<bool> TryRushAsync(Player champion, IAttackable target)
    {
        if (!Has(champion) || !champion.IsAlive || !target.IsAlive || champion.CurrentMap is not { } map || champion.Attributes is not { } a)
        {
            return false;
        }

        if (a[Stats.IsStunned] > 0 || a[Stats.IsFrozen] > 0 || a[Stats.IsAsleep] > 0)
        {
            return false;
        }

        var cooldown = Cooldowns.GetOrCreateValue(champion);
        var now = DateTime.UtcNow;
        if (now < cooldown.ReadyUtc)
        {
            return false;
        }

        var distance = target.GetDistanceTo(champion.Position);
        if (distance < MinTiles || distance > RangeTiles)
        {
            return false;
        }

        if (MobaArena.Of(map).IsPreMatch)
        {
            return false;
        }

        // The landing tile: on the line to the target, about a tile and a half short of it, on a walkable tile.
        var reach = Math.Max(1, (int)Math.Floor(distance - 1.5));
        if (!MobaBlink.TryClampTarget(champion, target.Position, reach, out var landing))
        {
            return false;
        }

        var team = MobaTeams.GetTeam(champion);
        var enemy = team == MobaTeam.Blue ? MobaTeam.Red : team == MobaTeam.Red ? MobaTeam.Blue : MobaTeam.None;
        if (MobaBaseAura.Contains(enemy, landing.X, landing.Y))
        {
            return false;
        }

        cooldown.ReadyUtc = now.AddSeconds(CooldownSeconds);
        await champion.MoveAsync(landing).ConfigureAwait(false);
        champion.Rotation = champion.Position.GetDirectionTo(target.Position);
        if (champion is MobaBotPlayer bot)
        {
            bot.DuelCasts.AddOrUpdate(SkillNumber, 1, (_, n) => n + 1);
        }

        var carrier = champion.SelectedCharacter?.LearnedSkills.FirstOrDefault(e => e.Skill?.Number == CarrierSkill);
        await target.AttackByAsync(champion, carrier, false, BlowFactor).ConfigureAwait(false);
        return true;
    }

    private sealed class Cooldown
    {
        public DateTime ReadyUtc;
    }
}
