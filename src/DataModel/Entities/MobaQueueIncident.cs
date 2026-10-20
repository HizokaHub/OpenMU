// <copyright file="MobaQueueIncident.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// One failed ready check of an account in the MOBA match queue. It is only a record for moderation and statistics;
/// the game logic never reads it.
/// </summary>
[AggregateRoot]
public class MobaQueueIncident
{
    /// <summary>
    /// Gets or sets the identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the account which failed.
    /// </summary>
    public Guid AccountId { get; set; }

    /// <summary>
    /// Gets or sets the name of the character which was in the queue.
    /// </summary>
    public string CharacterName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when it happened.
    /// </summary>
    public DateTime OccurredUtc { get; set; }

    /// <summary>
    /// Gets or sets why the ready check failed for the account.
    /// </summary>
    public MobaQueueFailureReason Reason { get; set; }

    /// <summary>
    /// Gets or sets the number of warnings the account had after this incident.
    /// </summary>
    public int WarningsAfter { get; set; }

    /// <summary>
    /// Gets or sets the date and time until which the incident blocked the account; <see langword="null"/> if it did not block it.
    /// </summary>
    public DateTime? BlockedUntil { get; set; }
}
