// <copyright file="MobaRespawn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Respawn rules of a MOBA champion (humans and bots alike): the wait grows linearly with
/// the match clock, and the champion comes back next to its team's shop instead of the
/// map's default spawn gate.
/// </summary>
[PlugIn]
[Display(Name = nameof(MobaRespawn), Description = "Respawns MOBA champions next to their team's shop.")]
[Guid("5D2B7E94-1C6A-4F38-A0E5-8B9C3D4F6A12")]
public class MobaRespawn : IPlayerSpawnGateSelectionPlugIn
{
    /// <summary>The shortest wait, in seconds (champion level 1, and the floor of the first-death discount).</summary>
    public const double BaseSeconds = 6;

    /// <summary>Seconds added to the wait for every champion level above 1.</summary>
    public const double SecondsPerLevel = 1.2;

    /// <summary>Match minute after which the time factor starts growing.</summary>
    public const double TimeFactorStartMinute = 15;

    /// <summary>Growth of the time factor per match minute after <see cref="TimeFactorStartMinute"/>.</summary>
    public const double TimeFactorPerMinute = 0.01;

    /// <summary>Highest value of the time factor's bonus (+25 %).</summary>
    public const double TimeFactorMaxBonus = 0.25;

    /// <summary>Share of the wait a champion pays for its first death of the match.</summary>
    public const double FirstDeathShare = 0.5;

    /// <summary>The longest possible wait, in seconds.</summary>
    public const double MaxSeconds = 60;

    /// <summary>
    /// Gets the respawn wait of a death, LoL style: it grows with the champion level and, late in the
    /// match, with the match clock. The first death of a champion only pays half (never below the minimum).
    /// </summary>
    /// <param name="championLevel">The level of the champion that died (1..30).</param>
    /// <param name="matchElapsed">The time since the match started.</param>
    /// <param name="deathsBefore">How many times the champion died before this death.</param>
    /// <returns>The wait.</returns>
    public static TimeSpan DelayFor(int championLevel, TimeSpan matchElapsed, int deathsBefore)
    {
        var byLevel = BaseSeconds + (SecondsPerLevel * (Math.Max(championLevel, 1) - 1));
        var timeBonus = Math.Clamp((matchElapsed.TotalMinutes - TimeFactorStartMinute) * TimeFactorPerMinute, 0, TimeFactorMaxBonus);
        var seconds = byLevel * (1 + timeBonus);
        if (deathsBefore <= 0)
        {
            seconds = Math.Max(BaseSeconds, seconds * FirstDeathShare);
        }

        return TimeSpan.FromSeconds(Math.Min(seconds, MaxSeconds));
    }

    /// <summary>
    /// Works out the wait of the death that is happening now and remembers it on the champion
    /// (so the notice shows the same number). Call before the champion's death is counted.
    /// </summary>
    /// <param name="champion">The champion that died.</param>
    /// <returns>The wait.</returns>
    public static TimeSpan BeginRespawn(Player champion)
    {
        var delay = DelayFor(champion.MobaLevel, MobaArena.Of(champion).Elapsed, champion.MobaDeaths);
        champion.MobaState.LastRespawnDelay = delay;
        champion.Logger.LogInformation(
            "[MOBA-RESPAWN] {Name} ({Team}, {Kind}) died: Lv{Level}, death #{Deaths}, match {Minutes:F1} min -> waits {Seconds:F1} s",
            champion.SelectedCharacter?.Name,
            MobaTeams.GetTeam(champion),
            champion is Offline.OfflinePlayer ? "bot" : "human",
            champion.MobaLevel,
            champion.MobaDeaths + 1,
            MobaArena.Of(champion).Elapsed.TotalMinutes,
            delay.TotalSeconds);
        return delay;
    }

    /// <summary>
    /// Gets the wait for a ghost whose human disconnected while dead: the full wait, as if it was not a first death.
    /// </summary>
    /// <param name="champion">The champion.</param>
    /// <param name="arena">The arena of the match.</param>
    /// <returns>The wait.</returns>
    public static TimeSpan FullDelayOf(MobaChampionState champion, MobaArena arena)
        => DelayFor(champion.Level, arena.Elapsed, 1);

    /// <inheritdoc />
    public ValueTask SelectSpawnGateAsync(Player player, SpawnGateSelectionArgs args)
    {
        if (args.Gate is not null
            || !player.IsMobaClone
            || player.CurrentMap?.Definition is not { } map
            || map.Number != MobaCloneFactory.ArenaMapNumber
            || MobaRecall.BaseOf(player) is not { } point)
        {
            return ValueTask.CompletedTask;
        }

        player.Logger.LogInformation("[MOBA-RESPAWN] {Name} respawns at the base of {Team} ({X},{Y})", player.SelectedCharacter?.Name, MobaTeams.GetTeam(player), point.X, point.Y);
        args.Gate = new ExitGate
        {
            Map = map,
            X1 = (byte)point.X,
            X2 = (byte)point.X,
            Y1 = (byte)point.Y,
            Y2 = (byte)point.Y,
        };

        return ValueTask.CompletedTask;
    }
}
