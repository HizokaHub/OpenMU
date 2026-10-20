// <copyright file="MobaQueuePenalty.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// The warnings and the block of an account in the MOBA match queue. There is at most one row per account;
/// the rules which change it are in the game logic (<c>MobaQueuePenaltyRules</c>).
/// </summary>
[AggregateRoot]
public class MobaQueuePenalty
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the penalized account.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Gets or sets the current number of warnings (3 at most; they expire one by one).
    /// </summary>
    public int Warnings { get; set; }

    /// <summary>
    /// Gets or sets how many blocks in a row the account got; it chooses the length of the next block.
    /// </summary>
    public int BlockLevel { get; set; }

    /// <summary>
    /// Gets or sets the date and time until which the account may not enter the queue.
    /// </summary>
    public DateTime? BlockedUntil { get; set; }

    /// <summary>
    /// Gets or sets the date and time of the last warning, from which the warnings expire.
    /// </summary>
    public DateTime? LastWarningUtc { get; set; }
}
