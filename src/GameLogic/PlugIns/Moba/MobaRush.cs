// <copyright file="MobaRush.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Views.World;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// "Rush" (2026-11-22), the closing skill brought from Season 21 (S21 skill 813, a Blade Master master skill: "Rush to an enemy within 7 tiles and inflict a
/// powerful blow"): the champion jumps next to an enemy champion up to <see cref="RangeTiles"/> tiles away and hits it. It is the melee answer to the
/// range counters of the 1v1 (the diagnosis of 2026-11-22: GM beats BK 92 %, RF beats BK 92 %...).
/// <para>
/// PHASE 1: the skill has its row (638) in the database (update <c>AddRushSkillPlugIn</c>); the bots use it as part of their engage logic, a champion that
/// learns it casts it through the generic skill packet, and the hit is dealt with the skill entry of Lunge (20), which every Blade Knight learns, with <see cref="BlowFactor"/> on top. It is on by default
/// (<see cref="Enabled"/>): on by default since the duels validated it (2026-11-22); <c>MOBA_RUSH=0</c> or <c>/mobaf10 rush off</c> turns it off.
/// </para>
/// </summary>
public static class MobaRush
{
    /// <summary>The skill number of the row in the configuration (S21 has it as 813, which does not fit the client table of 650).</summary>
    public const short SkillNumber = 638;

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

    /// <summary>Gets or sets a value indicating whether the rush exists (default: on; <c>MOBA_RUSH=0</c> turns it off).</summary>
    public static bool Enabled { get; set; } = Environment.GetEnvironmentVariable("MOBA_RUSH") != "0";

    /// <summary>
    /// Whether the champion has the rush: the Knight family (Blade Knight / Blade Master; the Rage Fighter is to be confirmed). A player needs the master
    /// tree node (<see cref="LevelOf"/> at least 1, F3); the bots have it from the start so the F10 calibration stays as it was.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <returns><see langword="true"/> if it can rush.</returns>
    public static bool Has(Player champion)
        => Enabled && champion.IsMobaClone && MobaPassives.FamilyOf(champion) == MobaFamily.Knight && (champion is MobaBotPlayer || LevelOf(champion) > 0);

    /// <summary>The level (rank 1..5) of the champion's Rush master node, 0 if it has not learned it.</summary>
    /// <param name="champion">The champion.</param>
    /// <returns>The node level.</returns>
    public static int LevelOf(Player champion)
        => champion.SelectedCharacter?.LearnedSkills.FirstOrDefault(e => e.Skill?.Number == SkillNumber)?.Level ?? 0;

    /// <summary>Tries to rush at the target: jumps next to it and hits it. Does nothing (returns <see langword="false"/>) if it is not ready or not worth it.</summary>
    /// <param name="champion">The rushing champion.</param>
    /// <param name="target">The enemy.</param>
    /// <param name="consume">For a player that cast the skill: pays the cast (champion point, cooldown by rank) once every check has passed and returns <see langword="false"/> if it can't be paid; <see langword="null"/> for the bots, which use <see cref="CooldownSeconds"/>.</param>
    /// <returns><see langword="true"/> if the champion rushed.</returns>
    public static async ValueTask<bool> TryRushAsync(Player champion, IAttackable target, Func<ValueTask<bool>>? consume = null)
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
        if (consume is null && now < cooldown.ReadyUtc)
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

        if (consume is not null && !await consume().ConfigureAwait(false))
        {
            return false;
        }

        cooldown.ReadyUtc = now.AddSeconds(CooldownSeconds);

        // The echo first (the client plays the pose and the flash where the champion stands), then the jump itself.
        var rushSkill = champion.GameContext.Configuration.Skills.FirstOrDefault(s => s.Number == SkillNumber);
        if (rushSkill is not null)
        {
            await champion.ForEachWorldObserverAsync<IShowSkillAnimationPlugIn>(p => p.ShowSkillAnimationAsync(champion, target, rushSkill, true), true).ConfigureAwait(false);
        }

        if (champion is not MobaBotPlayer)
        {
            champion.Logger.LogInformation("[MOBA-RUSH] \"{Name}\" rushes at {Distance:F1} tiles: {From} -> {To}", champion.SelectedCharacter?.Name, distance, champion.Position, landing);
        }

        await champion.MoveAsync(landing).ConfigureAwait(false);
        champion.Rotation = champion.Position.GetDirectionTo(target.Position);
        if (champion is MobaBotPlayer bot)
        {
            bot.DuelCasts.AddOrUpdate(SkillNumber, 1, (_, n) => n + 1);
        }

        // A player's blow is its own Rush entry (its rank, the damage table has the same numbers as Lunge); the bots lend Lunge's.
        var learned = champion.SelectedCharacter?.LearnedSkills;
        var ownEntry = learned?.FirstOrDefault(e => e.Skill?.Number == SkillNumber);
        var lungeEntry = learned?.FirstOrDefault(e => e.Skill?.Number == CarrierSkill);
        var carrier = consume is null ? lungeEntry ?? ownEntry : ownEntry ?? lungeEntry;
        await target.AttackByAsync(champion, carrier, false, BlowFactor).ConfigureAwait(false);
        return true;
    }

    private sealed class Cooldown
    {
        public DateTime ReadyUtc;
    }
}
