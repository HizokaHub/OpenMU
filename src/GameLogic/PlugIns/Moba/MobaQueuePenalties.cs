// <copyright file="MobaQueuePenalties.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Reads and writes the warnings and blocks of the MOBA queue (<see cref="MobaQueuePenalty"/>) and the record of the
/// failed ready checks (<see cref="MobaQueueIncident"/>) in the database, each with its own short-lived context so it also
/// works for players who already disconnected. The rules themselves are in <see cref="MobaQueuePenaltyRules"/>.
/// </summary>
public static class MobaQueuePenalties
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static IGameContext? _gameContext;
    private static ILogger? _logger;

    /// <summary>
    /// Tells the store which game context to take the database from (any of the game servers will do).
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public static void Bind(IGameContext gameContext)
    {
        _gameContext ??= gameContext;
        _logger ??= (gameContext as GameContext)?.LoggerFactory.CreateLogger(typeof(MobaQueuePenalties));
    }

    /// <summary>
    /// Gets how long an account is still blocked from the queue.
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The remaining time; <see langword="null"/> if the account may enter the queue (or the store is not available).</returns>
    public static async ValueTask<TimeSpan?> GetBlockRemainingAsync(Guid accountId)
    {
        if (_gameContext is not { } gameContext)
        {
            return null;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = gameContext.PersistenceContextProvider.CreateNewPlayerContext(gameContext.Configuration);
            var penalty = await context.GetMobaQueuePenaltyAsync(accountId).ConfigureAwait(false);
            return MobaQueuePenaltyRules.BlockRemaining(penalty, DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MOBA-QUEUE] Could not read the penalty of account {Account}.", accountId);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Registers a failed ready check: the warning, the block it may cause, and the record of the incident.
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <param name="characterName">The name of the character which was in the queue.</param>
    /// <param name="reason">Why the ready check failed for the account.</param>
    /// <returns>What the failure caused; <see langword="null"/> if it could not be saved.</returns>
    public static async ValueTask<MobaPenaltyOutcome?> RegisterFailureAsync(Guid accountId, string characterName, MobaQueueFailureReason reason)
    {
        if (_gameContext is not { } gameContext)
        {
            return null;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = gameContext.PersistenceContextProvider.CreateNewPlayerContext(gameContext.Configuration);
            var penalty = await GetOrCreateAsync(context, accountId).ConfigureAwait(false);
            var now = DateTime.UtcNow;
            var outcome = MobaQueuePenaltyRules.RegisterFailure(penalty, now);

            var incident = context.CreateNew<MobaQueueIncident>();
            incident.AccountId = accountId;
            incident.CharacterName = characterName;
            incident.OccurredUtc = now;
            incident.Reason = reason;
            incident.WarningsAfter = outcome.Warnings;
            incident.BlockedUntil = outcome.BlockedUntil;

            await context.SaveChangesAsync().ConfigureAwait(false);
            _logger?.LogInformation(
                "[MOBA-QUEUE] {Character} failed the ready check ({Reason}): {Warnings} warning(s){Block}.",
                characterName,
                reason,
                outcome.Warnings,
                outcome.BlockedFor is { } block ? $", blocked for {block}" : string.Empty);
            return outcome;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MOBA-QUEUE] Could not save the penalty of account {Account}.", accountId);
            return null;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Registers that an account accepted a match; after a served block this clears its warnings.
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <returns><see langword="true"/> if the warnings were cleared.</returns>
    public static async ValueTask<bool> RegisterAcceptanceAsync(Guid accountId)
    {
        if (_gameContext is not { } gameContext)
        {
            return false;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = gameContext.PersistenceContextProvider.CreateNewPlayerContext(gameContext.Configuration);
            var penalty = await context.GetMobaQueuePenaltyAsync(accountId).ConfigureAwait(false);
            if (penalty is null || !MobaQueuePenaltyRules.RegisterAcceptance(penalty, DateTime.UtcNow))
            {
                return false;
            }

            await context.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MOBA-QUEUE] Could not clear the penalty of account {Account}.", accountId);
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Describes the warnings and the block of an account (for the GM command).
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <returns>The text.</returns>
    public static async ValueTask<string> DescribeAsync(Guid accountId)
    {
        if (_gameContext is not { } gameContext)
        {
            return "sin datos";
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = gameContext.PersistenceContextProvider.CreateNewPlayerContext(gameContext.Configuration);
            var penalty = await context.GetMobaQueuePenaltyAsync(accountId).ConfigureAwait(false);
            if (penalty is null)
            {
                return "0 advertencias, sin bloqueo.";
            }

            MobaQueuePenaltyRules.Expire(penalty, DateTime.UtcNow);
            var block = MobaQueuePenaltyRules.BlockRemaining(penalty, DateTime.UtcNow);
            return $"{penalty.Warnings} advertencia(s), nivel de bloqueo {penalty.BlockLevel}, "
                + (block is { } left ? $"bloqueado {FormatDuration(left)} más." : "sin bloqueo.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MOBA-QUEUE] Could not read the penalty of account {Account}.", accountId);
            return "error al leer la BD";
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Clears the warnings and the block of an account (GM).
    /// </summary>
    /// <param name="accountId">The account.</param>
    /// <returns><see langword="true"/> if there was something to clear.</returns>
    public static async ValueTask<bool> PardonAsync(Guid accountId)
    {
        if (_gameContext is not { } gameContext)
        {
            return false;
        }

        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            using var context = gameContext.PersistenceContextProvider.CreateNewPlayerContext(gameContext.Configuration);
            var penalty = await context.GetMobaQueuePenaltyAsync(accountId).ConfigureAwait(false);
            if (penalty is null)
            {
                return false;
            }

            penalty.Warnings = 0;
            penalty.BlockLevel = 0;
            penalty.BlockedUntil = null;
            penalty.LastWarningUtc = null;
            await context.SaveChangesAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[MOBA-QUEUE] Could not pardon account {Account}.", accountId);
            return false;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Writes a duration the way the players read it ("2 h 30 min", "45 s").
    /// </summary>
    /// <param name="duration">The duration.</param>
    /// <returns>The text.</returns>
    public static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes < 1)
        {
            return $"{Math.Max(1, (int)Math.Ceiling(duration.TotalSeconds))} s";
        }

        var totalMinutes = (int)Math.Ceiling(duration.TotalMinutes);
        var hours = totalMinutes / 60;
        var minutes = totalMinutes % 60;
        return hours == 0 ? $"{minutes} min" : minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} min";
    }

    private static async ValueTask<MobaQueuePenalty> GetOrCreateAsync(IPlayerContext context, Guid accountId)
    {
        if (await context.GetMobaQueuePenaltyAsync(accountId).ConfigureAwait(false) is { } existing)
        {
            return existing;
        }

        var created = context.CreateNew<MobaQueuePenalty>();
        created.AccountId = accountId;
        return created;
    }
}
