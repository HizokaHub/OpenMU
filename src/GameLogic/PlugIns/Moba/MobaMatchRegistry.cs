// <copyright file="MobaMatchRegistry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;

/// <summary>
/// Process-wide, RAM-only registry of the accounts that are currently in a MOBA match.
/// </summary>
/// <remarks>
/// Survives a player's disconnect (it is not tied to the <see cref="Player"/> or its
/// persistence context), so on reconnect <c>SelectCharacterAction</c> can detect the
/// active match and drop the session back into it as the clone.
///
/// This first version only tracks membership; the clone is rebuilt fresh from the real
/// character on every (re)connect, so match-accumulated state (bought items, master
/// level) does not yet survive a disconnect. A match-scoped context that owns the clone
/// is a later topic.
/// </remarks>
public static class MobaMatchRegistry
{
    private static readonly ConcurrentDictionary<Guid, byte> AccountsInMatch = new();

    /// <summary>
    /// Marks the account as being in a MOBA match.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    public static void Enter(Guid accountId) => AccountsInMatch[accountId] = 1;

    /// <summary>
    /// Clears the account's MOBA match membership.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    public static void Leave(Guid accountId) => AccountsInMatch.TryRemove(accountId, out _);

    /// <summary>
    /// Determines whether the account is currently in a MOBA match.
    /// </summary>
    /// <param name="accountId">The account identifier.</param>
    /// <returns><see langword="true"/> if the account is in a match.</returns>
    public static bool IsInMatch(Guid accountId) => AccountsInMatch.ContainsKey(accountId);
}
