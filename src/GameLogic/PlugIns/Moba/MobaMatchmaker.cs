// <copyright file="MobaMatchmaker.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Which pool a queue entry belongs to.
/// </summary>
public enum MobaQueueMode : byte
{
    /// <summary>Solos and parties of 2 to 4 players; they mix until two teams of five are complete.</summary>
    Open = 0,

    /// <summary>A premade team of exactly five; it only meets another premade team.</summary>
    Premade = 1,
}

/// <summary>
/// A player in the queue.
/// </summary>
/// <param name="AccountId">The account identifier (one champion per account).</param>
/// <param name="Name">The character name, for messages.</param>
public sealed record MobaQueueMember(Guid AccountId, string Name);

/// <summary>
/// A solo player or a party waiting in the queue; its members always end up in the same team.
/// </summary>
public sealed class MobaQueueEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MobaQueueEntry"/> class.
    /// </summary>
    /// <param name="mode">The pool.</param>
    /// <param name="members">The members (one for a solo player).</param>
    /// <param name="joinedUtc">When the entry joined the queue; the older, the sooner it is matched.</param>
    public MobaQueueEntry(MobaQueueMode mode, IReadOnlyList<MobaQueueMember> members, DateTime joinedUtc)
    {
        this.Mode = mode;
        this.Members = members;
        this.JoinedUtc = joinedUtc;
    }

    /// <summary>Gets the identifier of the entry.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets the pool.</summary>
    public MobaQueueMode Mode { get; }

    /// <summary>Gets the members.</summary>
    public IReadOnlyList<MobaQueueMember> Members { get; }

    /// <summary>Gets when the entry joined the queue.</summary>
    public DateTime JoinedUtc { get; }

    /// <summary>Gets the number of players of the entry.</summary>
    public int Size => this.Members.Count;
}

/// <summary>
/// Two teams of five, formed from queue entries (an entry is never split).
/// </summary>
/// <param name="Blue">The entries of the blue team.</param>
/// <param name="Red">The entries of the red team.</param>
public sealed record MobaMatchProposal(IReadOnlyList<MobaQueueEntry> Blue, IReadOnlyList<MobaQueueEntry> Red)
{
    /// <summary>Gets every entry of the proposal.</summary>
    public IEnumerable<MobaQueueEntry> Entries => this.Blue.Concat(this.Red);

    /// <summary>Gets every player of the proposal.</summary>
    public IEnumerable<MobaQueueMember> Players => this.Entries.SelectMany(e => e.Members);
}

/// <summary>
/// Forms matches from the queue: the oldest entries first, parties always together, five against five.
/// </summary>
public static class MobaMatchmaker
{
    /// <summary>Players per team.</summary>
    public const int TeamSize = 5;

    /// <summary>The largest party allowed in the open pool (five-player teams use the premade pool).</summary>
    public const int MaxOpenEntrySize = 4;

    /// <summary>How many of the oldest entries are considered, to keep the search small.</summary>
    private const int SearchWindow = 40;

    /// <summary>
    /// Tries to form a match from the entries of one pool.
    /// </summary>
    /// <param name="pool">The entries of one pool, the oldest first.</param>
    /// <param name="mode">The pool.</param>
    /// <returns>A proposal, or <see langword="null"/> if the pool cannot form a match yet.</returns>
    public static MobaMatchProposal? TryForm(IReadOnlyList<MobaQueueEntry> pool, MobaQueueMode mode)
        => mode == MobaQueueMode.Premade ? FormPremade(pool) : FormOpen(pool);

    private static MobaMatchProposal? FormPremade(IReadOnlyList<MobaQueueEntry> pool)
    {
        var teams = pool.Where(e => e.Size == TeamSize).Take(2).ToList();
        return teams.Count == 2 ? new MobaMatchProposal([teams[0]], [teams[1]]) : null;
    }

    private static MobaMatchProposal? FormOpen(IReadOnlyList<MobaQueueEntry> pool)
    {
        var window = pool.Where(e => e.Size is >= 1 and <= MaxOpenEntrySize).Take(SearchWindow).ToList();
        if (window.Sum(e => e.Size) < 2 * TeamSize)
        {
            return null;
        }

        // The oldest entry which is part of any solution is the anchor; entries before it are in none, so they are skipped.
        for (var anchor = 0; anchor < window.Count; anchor++)
        {
            var chosen = new List<MobaQueueEntry> { window[anchor] };
            if (Search(window, anchor + 1, window[anchor].Size, chosen) && Split(chosen) is { } proposal)
            {
                return proposal;
            }
        }

        return null;
    }

    /// <summary>Adds entries after <paramref name="from"/> (oldest first) until the players add up to two teams.</summary>
    private static bool Search(List<MobaQueueEntry> window, int from, int total, List<MobaQueueEntry> chosen)
    {
        if (total == 2 * TeamSize)
        {
            return Split(chosen) is not null;
        }

        for (var i = from; i < window.Count; i++)
        {
            var size = window[i].Size;
            if (total + size > 2 * TeamSize)
            {
                continue;
            }

            chosen.Add(window[i]);
            if (Search(window, i + 1, total + size, chosen))
            {
                return true;
            }

            chosen.RemoveAt(chosen.Count - 1);
        }

        return false;
    }

    /// <summary>Splits the entries into two groups of five players; the group with the oldest entry is the blue team.</summary>
    private static MobaMatchProposal? Split(List<MobaQueueEntry> entries)
    {
        // Every subset of up to 10 entries: 1024 masks at most. The oldest entry is always in the blue team.
        var count = entries.Count;
        for (var mask = 0; mask < (1 << count); mask++)
        {
            if ((mask & 1) == 0)
            {
                continue;
            }

            var blueSize = 0;
            for (var i = 0; i < count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    blueSize += entries[i].Size;
                }
            }

            if (blueSize != TeamSize)
            {
                continue;
            }

            var blue = entries.Where((_, i) => (mask & (1 << i)) != 0).ToList();
            var red = entries.Where((_, i) => (mask & (1 << i)) == 0).ToList();
            return new MobaMatchProposal(blue, red);
        }

        return null;
    }
}
