// <copyright file="MobaQueue.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// How a player answered a ready check.
/// </summary>
public enum MobaReadyAnswer : byte
{
    /// <summary>No answer yet.</summary>
    Pending = 0,

    /// <summary>The player accepted the match.</summary>
    Accepted = 1,

    /// <summary>The player declined the match.</summary>
    Declined = 2,
}

/// <summary>
/// The state of a ready check.
/// </summary>
public enum MobaReadyState : byte
{
    /// <summary>Waiting for answers.</summary>
    Pending = 0,

    /// <summary>Every player accepted: the match starts.</summary>
    Started = 1,

    /// <summary>Somebody declined or did not answer in time: the match does not start.</summary>
    Failed = 2,
}

/// <summary>
/// The "match found, accept within 10 seconds" step of a proposed match.
/// </summary>
public sealed class MobaReadyCheck
{
    /// <summary>The time the players have to answer.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

    private readonly Dictionary<Guid, MobaReadyAnswer> _answers;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaReadyCheck"/> class.
    /// </summary>
    /// <param name="proposal">The proposed match.</param>
    /// <param name="startedUtc">When the check started.</param>
    public MobaReadyCheck(MobaMatchProposal proposal, DateTime startedUtc)
    {
        this.Proposal = proposal;
        this.StartedUtc = startedUtc;
        this.DeadlineUtc = startedUtc + Window;
        this._answers = proposal.Players.ToDictionary(p => p.AccountId, _ => MobaReadyAnswer.Pending);
    }

    /// <summary>Gets the identifier of the check.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets the proposed match.</summary>
    public MobaMatchProposal Proposal { get; }

    /// <summary>Gets when the check started.</summary>
    public DateTime StartedUtc { get; }

    /// <summary>Gets when the check ends.</summary>
    public DateTime DeadlineUtc { get; }

    /// <summary>Gets the state of the check.</summary>
    public MobaReadyState State { get; private set; }

    /// <summary>Gets the players who declined or did not answer in time (set when the check failed).</summary>
    public IReadOnlyCollection<Guid> Offenders { get; private set; } = Array.Empty<Guid>();

    /// <summary>Gets the number of players who accepted.</summary>
    public int AcceptedCount => this._answers.Values.Count(a => a == MobaReadyAnswer.Accepted);

    /// <summary>Gets the number of players of the match.</summary>
    public int PlayerCount => this._answers.Count;

    /// <summary>
    /// Gets how a player answered.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>The answer, or <see langword="null"/> if the account is not part of the check.</returns>
    public MobaReadyAnswer? AnswerOf(Guid account) => this._answers.TryGetValue(account, out var answer) ? answer : null;

    /// <summary>
    /// Records a player's answer. A declined check fails at once; the last acceptance starts the match.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="accepted">Whether the player accepts.</param>
    /// <returns>The state after the answer.</returns>
    public MobaReadyState Answer(Guid account, bool accepted)
    {
        if (this.State != MobaReadyState.Pending || !this._answers.TryGetValue(account, out var current) || current != MobaReadyAnswer.Pending)
        {
            return this.State;
        }

        this._answers[account] = accepted ? MobaReadyAnswer.Accepted : MobaReadyAnswer.Declined;
        if (!accepted)
        {
            this.Fail(blamePending: false);
        }
        else if (this._answers.Values.All(a => a == MobaReadyAnswer.Accepted))
        {
            this.State = MobaReadyState.Started;
        }

        return this.State;
    }

    /// <summary>
    /// Ends the check if its time ran out: whoever did not answer counts as having declined.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The state.</returns>
    public MobaReadyState Expire(DateTime nowUtc)
    {
        if (this.State == MobaReadyState.Pending && nowUtc >= this.DeadlineUtc)
        {
            this.Fail(blamePending: true);
        }

        return this.State;
    }

    /// <summary>Fails the check. Whoever declined is to blame; whoever did not answer only when the time ran out (an early decline does not blame the players who had no time to answer).</summary>
    private void Fail(bool blamePending)
    {
        this.State = MobaReadyState.Failed;
        this.Offenders = this._answers
            .Where(kv => kv.Value == MobaReadyAnswer.Declined || (blamePending && kv.Value == MobaReadyAnswer.Pending))
            .Select(kv => kv.Key)
            .ToList();
    }
}

/// <summary>
/// The result of asking to join the queue.
/// </summary>
public enum MobaQueueJoinResult : byte
{
    /// <summary>The entry is in the queue.</summary>
    Joined = 0,

    /// <summary>A member is already queued or in a ready check.</summary>
    AlreadyQueued = 1,

    /// <summary>The number of players does not fit the pool.</summary>
    InvalidSize = 2,
}

/// <summary>
/// Where an account stands in the queue.
/// </summary>
public enum MobaQueueStatus : byte
{
    /// <summary>Not in the queue.</summary>
    None = 0,

    /// <summary>Waiting for a match.</summary>
    Queued = 1,

    /// <summary>A match was found and the player is asked to accept.</summary>
    ReadyCheck = 2,
}

/// <summary>
/// Something which happened in the queue and that the host has to act on.
/// </summary>
/// <param name="Kind">What happened.</param>
/// <param name="Check">The ready check concerned.</param>
/// <param name="Requeued">For a failed check: the entries which went back to the queue.</param>
/// <param name="Dropped">For a failed check: the entries removed from the queue because a member did not accept.</param>
public sealed record MobaQueueEvent(MobaQueueEventKind Kind, MobaReadyCheck Check, IReadOnlyList<MobaQueueEntry> Requeued, IReadOnlyList<MobaQueueEntry> Dropped);

/// <summary>
/// What happened in the queue.
/// </summary>
public enum MobaQueueEventKind : byte
{
    /// <summary>A match was found: ask the players to accept.</summary>
    ReadyCheckStarted = 0,

    /// <summary>Everybody accepted: start the match.</summary>
    MatchReady = 1,

    /// <summary>The match did not start: warn the offenders, tell the others they are searching again.</summary>
    ReadyCheckFailed = 2,
}

/// <summary>
/// The matchmaking queue: two pools (open and premade), matches formed oldest first, a ready check
/// per match. It does no I/O: the host calls <see cref="Tick"/>, <see cref="Answer"/> and so on and
/// acts on the returned events. Several matches can be formed at the same time.
/// </summary>
public sealed class MobaQueue
{
    private readonly object _lock = new();
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<MobaQueueMode, List<MobaQueueEntry>> _pools = new()
    {
        [MobaQueueMode.Open] = new List<MobaQueueEntry>(),
        [MobaQueueMode.Premade] = new List<MobaQueueEntry>(),
    };

    private readonly List<MobaReadyCheck> _checks = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaQueue"/> class.
    /// </summary>
    /// <param name="clock">The clock (UTC); the real one if <see langword="null"/>.</param>
    public MobaQueue(Func<DateTime>? clock = null)
    {
        this._clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <summary>
    /// Puts a solo player or a party into a pool.
    /// </summary>
    /// <param name="mode">The pool.</param>
    /// <param name="members">The players; they stay together.</param>
    /// <param name="joinedUtc">When they first queued (to keep their place after a failed ready check); now if <see langword="null"/>.</param>
    /// <returns>The result.</returns>
    public MobaQueueJoinResult Join(MobaQueueMode mode, IReadOnlyList<MobaQueueMember> members, DateTime? joinedUtc = null)
    {
        var validSize = mode == MobaQueueMode.Premade
            ? members.Count == MobaMatchmaker.TeamSize
            : members.Count is >= 1 and <= MobaMatchmaker.MaxOpenEntrySize;
        if (!validSize || members.Select(m => m.AccountId).Distinct().Count() != members.Count)
        {
            return MobaQueueJoinResult.InvalidSize;
        }

        lock (this._lock)
        {
            if (members.Any(m => this.StatusOfCore(m.AccountId) != MobaQueueStatus.None))
            {
                return MobaQueueJoinResult.AlreadyQueued;
            }

            this.Insert(new MobaQueueEntry(mode, members, joinedUtc ?? this._clock()));
            return MobaQueueJoinResult.Joined;
        }
    }

    /// <summary>
    /// Takes an account's entry out of the queue (the whole party leaves with it). During a ready check
    /// this counts as declining.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>The events caused (a failed ready check), if any.</returns>
    public IReadOnlyList<MobaQueueEvent> Leave(Guid account)
    {
        lock (this._lock)
        {
            foreach (var pool in this._pools.Values)
            {
                if (pool.FirstOrDefault(e => e.Members.Any(m => m.AccountId == account)) is { } entry)
                {
                    pool.Remove(entry);
                    return Array.Empty<MobaQueueEvent>();
                }
            }

            var check = this._checks.FirstOrDefault(c => c.AnswerOf(account) is not null);
            return check is null ? Array.Empty<MobaQueueEvent>() : this.AnswerCore(check, account, false);
        }
    }

    /// <summary>
    /// Records a player's answer to the ready check he was asked.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <param name="accepted">Whether the player accepts.</param>
    /// <returns>The events caused (the match starts, or the check fails).</returns>
    public IReadOnlyList<MobaQueueEvent> Answer(Guid account, bool accepted)
    {
        lock (this._lock)
        {
            var check = this._checks.FirstOrDefault(c => c.AnswerOf(account) is not null);
            return check is null ? Array.Empty<MobaQueueEvent>() : this.AnswerCore(check, account, accepted);
        }
    }

    /// <summary>
    /// Where an account stands.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>The status.</returns>
    public MobaQueueStatus StatusOf(Guid account)
    {
        lock (this._lock)
        {
            return this.StatusOfCore(account);
        }
    }

    /// <summary>
    /// Gets the ready check an account is in.
    /// </summary>
    /// <param name="account">The account.</param>
    /// <returns>The check, or <see langword="null"/>.</returns>
    public MobaReadyCheck? ReadyCheckOf(Guid account)
    {
        lock (this._lock)
        {
            return this._checks.FirstOrDefault(c => c.AnswerOf(account) is not null);
        }
    }

    /// <summary>
    /// Gets how many players wait in a pool.
    /// </summary>
    /// <param name="mode">The pool.</param>
    /// <returns>The number of players.</returns>
    public int WaitingPlayers(MobaQueueMode mode)
    {
        lock (this._lock)
        {
            return this._pools[mode].Sum(e => e.Size);
        }
    }

    /// <summary>
    /// Forms the matches the pools allow and ends the ready checks whose time ran out.
    /// </summary>
    /// <returns>What happened, for the host to act on.</returns>
    public IReadOnlyList<MobaQueueEvent> Tick()
    {
        lock (this._lock)
        {
            var events = new List<MobaQueueEvent>();
            var now = this._clock();
            foreach (var check in this._checks.ToList())
            {
                if (check.Expire(now) == MobaReadyState.Failed)
                {
                    events.Add(this.Finish(check));
                }
            }

            foreach (var mode in new[] { MobaQueueMode.Open, MobaQueueMode.Premade })
            {
                while (MobaMatchmaker.TryForm(this._pools[mode], mode) is { } proposal)
                {
                    foreach (var entry in proposal.Entries)
                    {
                        this._pools[mode].Remove(entry);
                    }

                    var check = new MobaReadyCheck(proposal, now);
                    this._checks.Add(check);
                    events.Add(new MobaQueueEvent(MobaQueueEventKind.ReadyCheckStarted, check, Array.Empty<MobaQueueEntry>(), Array.Empty<MobaQueueEntry>()));
                }
            }

            return events;
        }
    }

    private IReadOnlyList<MobaQueueEvent> AnswerCore(MobaReadyCheck check, Guid account, bool accepted)
    {
        var state = check.Answer(account, accepted);
        return state == MobaReadyState.Pending ? Array.Empty<MobaQueueEvent>() : new[] { this.Finish(check) };
    }

    /// <summary>Closes a decided check. A failed one sends the entries of the players who accepted back to the queue, in their old place.</summary>
    private MobaQueueEvent Finish(MobaReadyCheck check)
    {
        this._checks.Remove(check);
        if (check.State == MobaReadyState.Started)
        {
            return new MobaQueueEvent(MobaQueueEventKind.MatchReady, check, Array.Empty<MobaQueueEntry>(), Array.Empty<MobaQueueEntry>());
        }

        var requeued = new List<MobaQueueEntry>();
        var dropped = new List<MobaQueueEntry>();
        foreach (var entry in check.Proposal.Entries)
        {
            if (entry.Members.Any(m => check.Offenders.Contains(m.AccountId)))
            {
                dropped.Add(entry);
            }
            else
            {
                requeued.Add(entry);
                this.Insert(entry);
            }
        }

        return new MobaQueueEvent(MobaQueueEventKind.ReadyCheckFailed, check, requeued, dropped);
    }

    /// <summary>Inserts an entry into its pool, oldest first.</summary>
    private void Insert(MobaQueueEntry entry)
    {
        var pool = this._pools[entry.Mode];
        var index = pool.FindIndex(e => e.JoinedUtc > entry.JoinedUtc);
        pool.Insert(index < 0 ? pool.Count : index, entry);
    }

    private MobaQueueStatus StatusOfCore(Guid account)
    {
        if (this._pools.Values.Any(pool => pool.Any(e => e.Members.Any(m => m.AccountId == account))))
        {
            return MobaQueueStatus.Queued;
        }

        return this._checks.Any(c => c.AnswerOf(account) is not null) ? MobaQueueStatus.ReadyCheck : MobaQueueStatus.None;
    }
}
