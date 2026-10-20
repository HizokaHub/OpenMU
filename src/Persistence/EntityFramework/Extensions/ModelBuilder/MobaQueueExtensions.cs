// <copyright file="MobaQueueExtensions.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.EntityFramework.Extensions.ModelBuilder;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MUnique.OpenMU.Persistence.EntityFramework.Model;

/// <summary>
/// Extensions for MOBA queue-related <see cref="EntityTypeBuilder"/>s.
/// </summary>
internal static class MobaQueueExtensions
{
    /// <summary>
    /// Applies the settings for the <see cref="MobaQueuePenalty"/> entity: one row per account, removed with it.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<MobaQueuePenalty> builder)
    {
        builder.HasIndex(penalty => penalty.AccountId).IsUnique();
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(penalty => penalty.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    /// <summary>
    /// Applies the settings for the <see cref="MobaQueueIncident"/> entity: the record is removed with the account.
    /// </summary>
    /// <param name="builder">The builder.</param>
    public static void Apply(this EntityTypeBuilder<MobaQueueIncident> builder)
    {
        builder.HasIndex(incident => incident.AccountId);
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(incident => incident.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
