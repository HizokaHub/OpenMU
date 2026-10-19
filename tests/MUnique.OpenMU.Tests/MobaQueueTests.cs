// <copyright file="MobaQueueTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the MOBA queue: the matchmaker, the ready check and the queue itself.
/// </summary>
[TestFixture]
public class MobaQueueTests
{
    private DateTime _now;
    private MobaQueue _queue = null!;

    /// <summary>
    /// Creates a queue with a clock the tests move by hand.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this._now = new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc);
        this._queue = new MobaQueue(() => this._now);
    }

    /// <summary>
    /// Ten solo players make one match of five against five, and the first nine do not.
    /// </summary>
    [Test]
    public void TenSolosFormOneMatch()
    {
        var accounts = this.JoinSolos(9);
        Assert.That(this._queue.Tick(), Is.Empty);

        accounts.Add(this.JoinSolo());
        var events = this._queue.Tick();

        Assert.That(events, Has.Count.EqualTo(1));
        Assert.That(events[0].Kind, Is.EqualTo(MobaQueueEventKind.ReadyCheckStarted));
        var proposal = events[0].Check.Proposal;
        Assert.That(proposal.Blue.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(this._queue.StatusOf(accounts[0]), Is.EqualTo(MobaQueueStatus.ReadyCheck));
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.Open), Is.EqualTo(0));
    }

    /// <summary>
    /// Parties are never split: a party of three and one of two against a four and a solo.
    /// </summary>
    [Test]
    public void PartiesStayTogether()
    {
        var three = this.JoinParty(3);
        var two = this.JoinParty(2);
        var four = this.JoinParty(4);
        var solo = this.JoinParty(1);

        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Blue.Select(e => e.Size).OrderBy(s => s), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(proposal.Red.Select(e => e.Size).OrderBy(s => s), Is.EqualTo(new[] { 1, 4 }));
        Assert.That(three.Concat(two).All(a => proposal.Blue.Any(e => e.Members.Any(m => m.AccountId == a))), Is.True);
        Assert.That(four.Concat(solo).All(a => proposal.Red.Any(e => e.Members.Any(m => m.AccountId == a))), Is.True);
    }

    /// <summary>
    /// Two parties of four cannot play together without the right fillers; the entries which do not fit wait.
    /// </summary>
    [Test]
    public void EntriesWhichCannotBeSplitIntoFiveAndFiveWait()
    {
        this.JoinParty(4);
        this.JoinParty(4);
        this.JoinParty(2);
        Assert.That(this._queue.Tick(), Is.Empty, "4 + 4 + 2 cannot be split 5 / 5");

        this.JoinParty(1);
        this.JoinParty(1);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Players.Count(), Is.EqualTo(10));
        Assert.That(proposal.Entries.Count(e => e.Size == 2), Is.EqualTo(0), "the pair is the one left out");
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.Open), Is.EqualTo(2));
    }

    /// <summary>
    /// The oldest entries are matched first.
    /// </summary>
    [Test]
    public void OldestPlayersGoFirst()
    {
        var accounts = this.JoinSolos(12);

        var proposal = this._queue.Tick().Single().Check.Proposal;

        var matched = proposal.Players.Select(p => p.AccountId).ToHashSet();
        Assert.That(accounts.Take(10).All(matched.Contains), Is.True);
        Assert.That(this._queue.StatusOf(accounts[10]), Is.EqualTo(MobaQueueStatus.Queued));
        Assert.That(this._queue.StatusOf(accounts[11]), Is.EqualTo(MobaQueueStatus.Queued));
    }

    /// <summary>
    /// Twenty players make two matches at the same time.
    /// </summary>
    [Test]
    public void SeveralMatchesFormAtOnce()
    {
        this.JoinSolos(20);

        var events = this._queue.Tick();

        Assert.That(events, Has.Count.EqualTo(2));
        Assert.That(events.SelectMany(e => e.Check.Proposal.Players).Select(p => p.AccountId).Distinct().Count(), Is.EqualTo(20));
    }

    /// <summary>
    /// Premade teams only meet premade teams; five solos never mix into that pool.
    /// </summary>
    [Test]
    public void PremadePoolIsSeparate()
    {
        this.JoinSolos(5);
        this.JoinPremade();
        Assert.That(this._queue.Tick(), Is.Empty, "a premade team does not mix with five solos");

        this.JoinPremade();
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Blue.Single().Size, Is.EqualTo(5));
        Assert.That(proposal.Red.Single().Size, Is.EqualTo(5));
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.Open), Is.EqualTo(5));
    }

    /// <summary>
    /// The size rules and the one-entry-per-account rule.
    /// </summary>
    [Test]
    public void JoinRulesAreChecked()
    {
        var member = new MobaQueueMember(Guid.NewGuid(), "a");
        Assert.That(this._queue.Join(MobaQueueMode.Open, [member]), Is.EqualTo(MobaQueueJoinResult.Joined));
        Assert.That(this._queue.Join(MobaQueueMode.Open, [member]), Is.EqualTo(MobaQueueJoinResult.AlreadyQueued));
        Assert.That(this._queue.Join(MobaQueueMode.Open, this.Members(5)), Is.EqualTo(MobaQueueJoinResult.InvalidSize));
        Assert.That(this._queue.Join(MobaQueueMode.Premade, this.Members(4)), Is.EqualTo(MobaQueueJoinResult.InvalidSize));
        Assert.That(this._queue.Join(MobaQueueMode.Open, []), Is.EqualTo(MobaQueueJoinResult.InvalidSize));
    }

    /// <summary>
    /// Leaving takes the whole party out of the queue.
    /// </summary>
    [Test]
    public void LeavingRemovesTheWholeParty()
    {
        var party = this.JoinParty(3);

        this._queue.Leave(party[1]);

        Assert.That(party.All(a => this._queue.StatusOf(a) == MobaQueueStatus.None), Is.True);
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.Open), Is.EqualTo(0));
    }

    /// <summary>
    /// When all ten accept, the match is ready.
    /// </summary>
    [Test]
    public void EveryoneAcceptingStartsTheMatch()
    {
        var accounts = this.JoinSolos(10);
        var check = this._queue.Tick().Single().Check;

        IReadOnlyList<MobaQueueEvent> last = [];
        foreach (var account in accounts)
        {
            Assert.That(last, Is.Empty);
            last = this._queue.Answer(account, true);
        }

        Assert.That(last.Single().Kind, Is.EqualTo(MobaQueueEventKind.MatchReady));
        Assert.That(check.State, Is.EqualTo(MobaReadyState.Started));
        Assert.That(accounts.All(a => this._queue.StatusOf(a) == MobaQueueStatus.None), Is.True);
    }

    /// <summary>
    /// A decline ends the check at once: the decliner is dropped and the others go back to the queue in their old place.
    /// </summary>
    [Test]
    public void DeclineDropsTheOffenderAndRequeuesTheRest()
    {
        var accounts = this.JoinSolos(10);
        this._queue.Tick();
        this._queue.Answer(accounts[0], true);
        this._now += TimeSpan.FromSeconds(3);
        var newcomer = this.JoinSolo();

        var failed = this._queue.Answer(accounts[4], false).Single();

        Assert.That(failed.Kind, Is.EqualTo(MobaQueueEventKind.ReadyCheckFailed));
        Assert.That(failed.Check.Offenders, Is.EquivalentTo(new[] { accounts[4] }));
        Assert.That(failed.Dropped.Single().Members.Single().AccountId, Is.EqualTo(accounts[4]));
        Assert.That(failed.Requeued, Has.Count.EqualTo(9));
        Assert.That(this._queue.StatusOf(accounts[4]), Is.EqualTo(MobaQueueStatus.None));
        Assert.That(this._queue.StatusOf(accounts[0]), Is.EqualTo(MobaQueueStatus.Queued));

        // The nine plus the newcomer are ten: the newcomer is the youngest and the replacement of the decliner.
        var next = this._queue.Tick().Single().Check.Proposal.Players.Select(p => p.AccountId).ToHashSet();
        Assert.That(next, Does.Contain(newcomer));
        Assert.That(accounts.Where((_, i) => i != 4).All(next.Contains), Is.True);
    }

    /// <summary>
    /// Whoever does not answer in 10 seconds counts as having declined; the ones who accepted are not blamed.
    /// </summary>
    [Test]
    public void TimeoutBlamesOnlyThoseWhoDidNotAnswer()
    {
        var accounts = this.JoinSolos(10);
        this._queue.Tick();
        foreach (var account in accounts.Take(8))
        {
            this._queue.Answer(account, true);
        }

        this._now += MobaReadyCheck.Window - TimeSpan.FromMilliseconds(1);
        Assert.That(this._queue.Tick(), Is.Empty);

        this._now += TimeSpan.FromMilliseconds(1);
        var failed = this._queue.Tick().Single();

        Assert.That(failed.Kind, Is.EqualTo(MobaQueueEventKind.ReadyCheckFailed));
        Assert.That(failed.Check.Offenders, Is.EquivalentTo(accounts.Skip(8)));
        Assert.That(failed.Requeued, Has.Count.EqualTo(8));
    }

    /// <summary>
    /// If a member of a party fails the ready check the whole party leaves the queue.
    /// </summary>
    [Test]
    public void PartyOfAnOffenderIsDropped()
    {
        var party = this.JoinParty(3);
        var others = this.JoinSolos(7);
        this._queue.Tick();

        var failed = this._queue.Answer(party[2], false).Single();

        Assert.That(failed.Dropped.Single().Size, Is.EqualTo(3));
        Assert.That(failed.Check.Offenders, Is.EquivalentTo(new[] { party[2] }));
        Assert.That(party.All(a => this._queue.StatusOf(a) == MobaQueueStatus.None), Is.True);
        Assert.That(others.All(a => this._queue.StatusOf(a) == MobaQueueStatus.Queued), Is.True);
    }

    /// <summary>
    /// Leaving during a ready check counts as declining.
    /// </summary>
    [Test]
    public void LeavingDuringReadyCheckFailsIt()
    {
        var accounts = this.JoinSolos(10);
        this._queue.Tick();

        var failed = this._queue.Leave(accounts[3]).Single();

        Assert.That(failed.Kind, Is.EqualTo(MobaQueueEventKind.ReadyCheckFailed));
        Assert.That(failed.Check.Offenders, Is.EquivalentTo(new[] { accounts[3] }));
    }

    private List<Guid> JoinSolos(int count)
    {
        var accounts = new List<Guid>();
        for (var i = 0; i < count; i++)
        {
            accounts.Add(this.JoinSolo());
        }

        return accounts;
    }

    private Guid JoinSolo() => this.JoinParty(1)[0];

    private List<Guid> JoinParty(int size)
    {
        var members = this.Members(size);
        Assert.That(this._queue.Join(MobaQueueMode.Open, members), Is.EqualTo(MobaQueueJoinResult.Joined));
        this._now += TimeSpan.FromSeconds(1);
        return members.Select(m => m.AccountId).ToList();
    }

    private void JoinPremade()
    {
        Assert.That(this._queue.Join(MobaQueueMode.Premade, this.Members(5)), Is.EqualTo(MobaQueueJoinResult.Joined));
        this._now += TimeSpan.FromSeconds(1);
    }

    private List<MobaQueueMember> Members(int count)
        => Enumerable.Range(0, count).Select(i => new MobaQueueMember(Guid.NewGuid(), "p" + i)).ToList();
}
