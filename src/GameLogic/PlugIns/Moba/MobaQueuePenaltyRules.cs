// <copyright file="MobaQueuePenaltyRules.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// What a failed ready check did to an account.
/// </summary>
/// <param name="Warnings">The warnings after the failure.</param>
/// <param name="BlockedFor">How long the account is blocked now; <see langword="null"/> if this failure did not block it.</param>
/// <param name="BlockedUntil">Until when the account is blocked; <see langword="null"/> if it is not.</param>
public readonly record struct MobaPenaltyOutcome(int Warnings, TimeSpan? BlockedFor, DateTime? BlockedUntil);

/// <summary>
/// The rules of the warnings and blocks of the MOBA queue, on a <see cref="MobaQueuePenalty"/>. Pure logic (no I/O):
/// <list type="bullet">
/// <item>A failed ready check (declined, no answer in time, left the queue or disconnected during it) is one warning, 3 at most.</item>
/// <item>At 3 warnings the account is blocked from the queue; the warnings stay at 3, so any further failure blocks again,
/// each block longer than the one before (<see cref="BlockDurations"/>, the last one is the limit).</item>
/// <item>Warnings expire one by one, every <see cref="WarningLifetime"/> since the last one; with none left the block level starts over.</item>
/// <item>Accepting a match after serving a block clears everything. Without a served block, accepting changes nothing.</item>
/// </list>
/// </summary>
public static class MobaQueuePenaltyRules
{
    /// <summary>The warnings which make a block.</summary>
    public const int WarningsForBlock = 3;

    /// <summary>The time after which one warning expires.</summary>
    public static readonly TimeSpan WarningLifetime = TimeSpan.FromDays(2);

    /// <summary>The length of the 1st, 2nd, ... block; the last one is also the length of every block after it.</summary>
    public static readonly IReadOnlyList<TimeSpan> BlockDurations =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(30),
        TimeSpan.FromHours(1),
        TimeSpan.FromHours(2),
        TimeSpan.FromHours(5),
        TimeSpan.FromHours(12),
    ];

    /// <summary>
    /// Lets the warnings which are old enough expire.
    /// </summary>
    /// <param name="penalty">The penalty.</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    public static void Expire(MobaQueuePenalty penalty, DateTime nowUtc)
    {
        if (penalty.Warnings <= 0 || penalty.LastWarningUtc is not { } last)
        {
            return;
        }

        var periods = (int)Math.Floor((nowUtc - last).TotalDays / WarningLifetime.TotalDays);
        if (periods <= 0)
        {
            return;
        }

        penalty.Warnings = Math.Max(0, penalty.Warnings - periods);
        if (penalty.Warnings == 0)
        {
            penalty.LastWarningUtc = null;
            penalty.BlockLevel = 0;
        }
        else
        {
            penalty.LastWarningUtc = last + (periods * WarningLifetime);
        }
    }

    /// <summary>
    /// Gets how long the account is still blocked.
    /// </summary>
    /// <param name="penalty">The penalty; <see langword="null"/> for an account without any.</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <returns>The remaining time, or <see langword="null"/> if the account may enter the queue.</returns>
    public static TimeSpan? BlockRemaining(MobaQueuePenalty? penalty, DateTime nowUtc)
        => penalty?.BlockedUntil is { } until && until > nowUtc ? until - nowUtc : null;

    /// <summary>
    /// Registers a failed ready check: one more warning and, from the 3rd on, a block.
    /// </summary>
    /// <param name="penalty">The penalty.</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <returns>What the failure caused.</returns>
    public static MobaPenaltyOutcome RegisterFailure(MobaQueuePenalty penalty, DateTime nowUtc)
    {
        Expire(penalty, nowUtc);
        penalty.Warnings = Math.Min(WarningsForBlock, penalty.Warnings + 1);
        penalty.LastWarningUtc = nowUtc;
        if (penalty.Warnings < WarningsForBlock)
        {
            return new MobaPenaltyOutcome(penalty.Warnings, null, null);
        }

        var duration = BlockDurations[Math.Clamp(penalty.BlockLevel, 0, BlockDurations.Count - 1)];
        penalty.BlockLevel++;
        penalty.BlockedUntil = nowUtc + duration;
        return new MobaPenaltyOutcome(penalty.Warnings, duration, penalty.BlockedUntil);
    }

    /// <summary>
    /// Registers that the account accepted a match: after a served block, the slate is cleaned.
    /// </summary>
    /// <param name="penalty">The penalty.</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <returns><see langword="true"/> if the warnings and the block level were cleared.</returns>
    public static bool RegisterAcceptance(MobaQueuePenalty penalty, DateTime nowUtc)
    {
        Expire(penalty, nowUtc);
        if (penalty.BlockedUntil is not { } until || until > nowUtc)
        {
            return false;
        }

        penalty.Warnings = 0;
        penalty.BlockLevel = 0;
        penalty.BlockedUntil = null;
        penalty.LastWarningUtc = null;
        return true;
    }
}
