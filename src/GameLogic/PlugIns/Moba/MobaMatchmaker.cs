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

    /// <summary>
    /// Solos and parties of 1 to 5 who accept bots: a wild card which completes the human-only matches of the other pools
    /// (also a premade team's opponents), and which gets a match with bots when it has waited long enough.
    /// </summary>
    WithBots = 2,

    /// <summary>A solo player or a party of up to five who play against five bots; the match starts at once.</summary>
    Coop = 3,
}

/// <summary>
/// A player in the queue.
/// </summary>
/// <param name="AccountId">The account identifier (one champion per account).</param>
/// <param name="Name">The character name, for messages.</param>
/// <param name="IsBot">Whether the place is filled by a bot (it has no session and accepts any ready check).</param>
public sealed record MobaQueueMember(Guid AccountId, string Name, bool IsBot = false);

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

    /// <summary>
    /// The steps of the wait in the bot pool: after the oldest entry waited this long, a match with bots needs this many humans.
    /// The player who searches with bots always gets a match: at the last step a single human is enough.
    /// </summary>
    public static readonly IReadOnlyList<(TimeSpan Wait, int Humans)> BotFillStages =
    [
        (TimeSpan.FromSeconds(120), 6),
        (TimeSpan.FromSeconds(180), 4),
        (TimeSpan.FromSeconds(240), 2),
        (TimeSpan.FromSeconds(360), 1),
    ];

    /// <summary>
    /// Gets how many humans a match with bots needs after the oldest bot-pool entry waited <paramref name="wait"/>.
    /// </summary>
    /// <param name="wait">How long the oldest entry has waited.</param>
    /// <returns>The humans needed; <see langword="null"/> if it is too early for a match with bots.</returns>
    public static int? HumansNeeded(TimeSpan wait)
    {
        int? needed = null;
        foreach (var (stage, humans) in BotFillStages)
        {
            if (wait >= stage)
            {
                needed = humans;
            }
        }

        return needed;
    }

    /// <summary>
    /// Describes the steps of the wait in the bot pool, for the players.
    /// </summary>
    /// <returns>The text.</returns>
    public static string DescribeBotStages()
        => string.Join(", ", BotFillStages.Select(s => $"{(int)s.Wait.TotalMinutes}:{s.Wait.Seconds:00} con {(s.Humans == 1 ? "cualquier n\u00famero de" : $"{s.Humans} o m\u00e1s")} humanos"));

    /// <summary>
    /// Tries to form a match of humans only. Entries of the bot pool (wild cards) take part: they complete the open pool and
    /// the opposing team of a premade team. Premade pairs go first, then a premade team against a team built from wild cards,
    /// then the open pool.
    /// </summary>
    /// <param name="open">The open pool, the oldest first.</param>
    /// <param name="premade">The premade pool, the oldest first.</param>
    /// <param name="wildCards">The bot pool, the oldest first.</param>
    /// <returns>A proposal, or <see langword="null"/> if no match can be formed yet.</returns>
    public static MobaMatchProposal? TryFormHumans(IReadOnlyList<MobaQueueEntry> open, IReadOnlyList<MobaQueueEntry> premade, IReadOnlyList<MobaQueueEntry> wildCards)
    {
        var teams = premade.Concat(wildCards.Where(e => e.Size == TeamSize)).Take(2).ToList();
        if (teams.Count == 2)
        {
            return new MobaMatchProposal([teams[0]], [teams[1]]);
        }

        if (premade.Count > 0
            && FindSubset(wildCards.Where(e => e.Size < TeamSize).Take(SearchWindow).ToList(), TeamSize) is { } opponents)
        {
            return new MobaMatchProposal([premade[0]], opponents);
        }

        var mixed = open.Concat(wildCards.Where(e => e.Size <= MaxOpenEntrySize)).OrderBy(e => e.JoinedUtc).ToList();
        return FormOpen(mixed);
    }

    /// <summary>
    /// Tries to form a match with bots from the bot pool: when its oldest entry has waited long enough for the humans at hand
    /// (see <see cref="BotFillStages"/>), the humans are spread over the two teams (parties stay together) and the empty places
    /// are filled with bots.
    /// </summary>
    /// <param name="pool">The bot pool, the oldest first.</param>
    /// <param name="nowUtc">The current time (UTC).</param>
    /// <returns>A proposal, or <see langword="null"/> if it is too early or there are too few humans.</returns>
    public static MobaMatchProposal? TryFormWithBots(IReadOnlyList<MobaQueueEntry> pool, DateTime nowUtc)
    {
        if (pool.Count == 0 || HumansNeeded(nowUtc - pool[0].JoinedUtc) is not { } needed)
        {
            return null;
        }

        var chosen = new List<MobaQueueEntry>();
        var humans = 0;
        foreach (var entry in pool.Take(SearchWindow))
        {
            if (humans + entry.Size > 2 * TeamSize)
            {
                continue;
            }

            chosen.Add(entry);
            if (Balance(chosen) is null)
            {
                chosen.RemoveAt(chosen.Count - 1);
                continue;
            }

            humans += entry.Size;
        }

        if (humans < needed || Balance(chosen) is not { } sides)
        {
            return null;
        }

        return new MobaMatchProposal(WithBotsFilled(sides.Blue, pool[0].JoinedUtc), WithBotsFilled(sides.Red, pool[0].JoinedUtc));
    }

    /// <summary>
    /// Forms the cooperative match of an entry: its humans (with bots up to five) against five bots.
    /// </summary>
    /// <param name="entry">The entry of the cooperative pool.</param>
    /// <returns>The proposal.</returns>
    public static MobaMatchProposal FormCoop(MobaQueueEntry entry)
        => new(WithBotsFilled([entry], entry.JoinedUtc), [BotEntry(TeamSize, entry.JoinedUtc)]);

    private static List<MobaQueueEntry> WithBotsFilled(IReadOnlyList<MobaQueueEntry> team, DateTime joinedUtc)
    {
        var list = team.ToList();
        var missing = TeamSize - team.Sum(e => e.Size);
        if (missing > 0)
        {
            list.Add(BotEntry(missing, joinedUtc));
        }

        return list;
    }

    private static MobaQueueEntry BotEntry(int count, DateTime joinedUtc)
        => new(MobaQueueMode.WithBots, Enumerable.Range(1, count).Select(i => new MobaQueueMember(Guid.NewGuid(), $"Bot {i}", IsBot: true)).ToList(), joinedUtc);

    /// <summary>Spreads the entries over two teams of at most five players, as even as possible; the oldest entry is in the blue team.</summary>
    private static (List<MobaQueueEntry> Blue, List<MobaQueueEntry> Red)? Balance(List<MobaQueueEntry> entries)
    {
        (List<MobaQueueEntry> Blue, List<MobaQueueEntry> Red)? best = null;
        var bestGap = int.MaxValue;
        var count = entries.Count;
        for (var mask = 1; mask < (1 << count); mask += 2)
        {
            var blueSize = 0;
            for (var i = 0; i < count; i++)
            {
                if ((mask & (1 << i)) != 0)
                {
                    blueSize += entries[i].Size;
                }
            }

            var redSize = entries.Sum(e => e.Size) - blueSize;
            if (blueSize > TeamSize || redSize > TeamSize || Math.Abs(blueSize - redSize) >= bestGap)
            {
                continue;
            }

            bestGap = Math.Abs(blueSize - redSize);
            best = (entries.Where((_, i) => (mask & (1 << i)) != 0).ToList(), entries.Where((_, i) => (mask & (1 << i)) == 0).ToList());
        }

        return best;
    }

    /// <summary>Finds entries (oldest first) whose players add up to exactly <paramref name="target"/>.</summary>
    private static List<MobaQueueEntry>? FindSubset(List<MobaQueueEntry> candidates, int target)
    {
        var chosen = new List<MobaQueueEntry>();
        return SubsetFrom(candidates, 0, target, chosen) ? chosen : null;
    }

    private static bool SubsetFrom(List<MobaQueueEntry> candidates, int from, int remaining, List<MobaQueueEntry> chosen)
    {
        if (remaining == 0)
        {
            return true;
        }

        for (var i = from; i < candidates.Count; i++)
        {
            if (candidates[i].Size > remaining)
            {
                continue;
            }

            chosen.Add(candidates[i]);
            if (SubsetFrom(candidates, i + 1, remaining - candidates[i].Size, chosen))
            {
                return true;
            }

            chosen.RemoveAt(chosen.Count - 1);
        }

        return false;
    }

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
