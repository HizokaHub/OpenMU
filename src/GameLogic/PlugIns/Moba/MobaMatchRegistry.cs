// <copyright file="MobaMatchRegistry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Process-wide, RAM-only registry of the accounts that are currently in a MOBA match
/// and the ephemeral clone character each one is playing.
/// </summary>
/// <remarks>
/// The clone object is kept here (not on the <see cref="Player"/> or its persistence
/// context), so it survives the player's disconnect with its current state - position,
/// inventory, master level. On reconnect <c>SelectCharacterAction</c> looks the clone
/// up and drops the session straight back into it.
///
/// This still holds a single clone per account and never persists it. A proper
/// match-scoped context that owns all of a match's clones (and their lifetime) is a
/// later topic; for now the clone is discarded on <c>/mobaleave</c>.
/// </remarks>
public static class MobaMatchRegistry
{
    private static readonly ConcurrentDictionary<Guid, Character> ClonesByAccount = new();

    /// <summary>
    /// Registers the clone the account is playing in its MOBA match.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="clone">The ephemeral clone character.</param>
    public static void Enter(Guid accountId, Character clone) => ClonesByAccount[accountId] = clone;

    /// <summary>
    /// Clears the account's MOBA match membership and returns its clone, if any.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns>The clone that was registered, or <see langword="null"/>.</returns>
    public static Character? Leave(Guid accountId) => ClonesByAccount.TryRemove(accountId, out var clone) ? clone : null;

    /// <summary>
    /// Gets the clone the account is currently playing, if it is in a match.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <param name="clone">The registered clone.</param>
    /// <returns><see langword="true"/> if the account is in a match.</returns>
    public static bool TryGetClone(Guid accountId, out Character? clone) => ClonesByAccount.TryGetValue(accountId, out clone);

    /// <summary>
    /// Determines whether the account is currently in a MOBA match.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns><see langword="true"/> if the account is in a match.</returns>
    public static bool IsInMatch(Guid accountId) => ClonesByAccount.ContainsKey(accountId);
}
