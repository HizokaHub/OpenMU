// <copyright file="MobaQueueBotsTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the queue modes with bots: the bot pool as a wild card for human matches, the stepped wait before a match with
/// bots, and the cooperative match.
/// </summary>
[TestFixture]
public class MobaQueueBotsTests
{
    private DateTime _now;
    private MobaQueue _queue = null!;

    /// <summary>
    /// Creates a queue with a clock the tests move by hand.
    /// </summary>
    [SetUp]
    public void SetUp()
    {
        this._now = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        this._queue = new MobaQueue(() => this._now);
    }

    /// <summary>
    /// A lone player who accepts bots waits for the first step, and with nobody else only the last step starts the match.
    /// </summary>
    [Test]
    public void LoneBotSearcherStartsOnlyAtTheLastStep()
    {
        var account = this.Join(MobaQueueMode.WithBots, 1)[0];

        this._now = this._now.AddSeconds(359);
        Assert.That(this._queue.Tick(), Is.Empty);
        Assert.That(this._queue.StatusOf(account), Is.EqualTo(MobaQueueStatus.Queued));

        this._now = this._now.AddSeconds(1);
        var events = this._queue.Tick();

        Assert.That(events, Has.Count.EqualTo(1));
        var proposal = events[0].Check.Proposal;
        Assert.That(proposal.Blue.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(proposal.Players.Count(p => !p.IsBot), Is.EqualTo(1));
        Assert.That(this._queue.StatusOf(account), Is.EqualTo(MobaQueueStatus.ReadyCheck));
    }

    /// <summary>
    /// Six humans in the bot pool start after the first step, spread three and three, the bots filling the rest.
    /// </summary>
    [Test]
    public void SixHumansStartAfterTheFirstStepSpreadOverBothTeams()
    {
        this.Join(MobaQueueMode.WithBots, 6);

        this._now = this._now.AddSeconds(119);
        Assert.That(this._queue.Tick(), Is.Empty);

        this._now = this._now.AddSeconds(1);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Blue.SelectMany(e => e.Members).Count(m => !m.IsBot), Is.EqualTo(3));
        Assert.That(proposal.Red.SelectMany(e => e.Members).Count(m => !m.IsBot), Is.EqualTo(3));
        Assert.That(proposal.Blue.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(5));
    }

    /// <summary>
    /// Three humans have to wait for the 4 and 2 human steps: they do not start at the 6 human step but do at 4:00 (2 or more).
    /// </summary>
    [Test]
    public void FewHumansWaitForTheMatchingStep()
    {
        this.Join(MobaQueueMode.WithBots, 3);

        this._now = this._now.AddSeconds(239);
        Assert.That(this._queue.Tick(), Is.Empty);

        this._now = this._now.AddSeconds(1);
        Assert.That(this._queue.Tick(), Has.Count.EqualTo(1));
    }

    /// <summary>
    /// The bot pool completes the open pool at once: no bots when ten humans are around.
    /// </summary>
    [Test]
    public void WildCardsCompleteAnOpenMatchWithoutBots()
    {
        this.Join(MobaQueueMode.Open, 8);
        Assert.That(this._queue.Tick(), Is.Empty);

        this.Join(MobaQueueMode.WithBots, 2);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Players.Count(p => p.IsBot), Is.EqualTo(0));
        Assert.That(proposal.Players.Count(), Is.EqualTo(10));
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.WithBots), Is.EqualTo(0));
    }

    /// <summary>
    /// Five solos from the bot pool become the opponents of a premade team, so the match has humans only.
    /// </summary>
    [Test]
    public void WildCardsFaceAPremadeTeam()
    {
        var premade = this.Join(MobaQueueMode.Premade, 5, together: true);
        this.Join(MobaQueueMode.WithBots, 5);

        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Players.Count(p => p.IsBot), Is.EqualTo(0));
        Assert.That(proposal.Blue.SelectMany(e => e.Members).Select(m => m.AccountId), Is.EquivalentTo(premade));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(5));
    }

    /// <summary>
    /// Players of the strict pools are never put into a match with bots; only the bot pool is.
    /// </summary>
    [Test]
    public void StrictPlayersAreNotDraggedIntoBotMatches()
    {
        var strict = this.Join(MobaQueueMode.Open, 1)[0];
        this.Join(MobaQueueMode.WithBots, 1);

        this._now = this._now.AddSeconds(400);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Players.Count(p => !p.IsBot), Is.EqualTo(1));
        Assert.That(this._queue.StatusOf(strict), Is.EqualTo(MobaQueueStatus.Queued));
    }

    /// <summary>
    /// A party in the bot pool stays in one team.
    /// </summary>
    [Test]
    public void PartiesStayTogetherInBotMatches()
    {
        var party = this.Join(MobaQueueMode.WithBots, 3, together: true);
        this.Join(MobaQueueMode.WithBots, 3);

        this._now = this._now.AddSeconds(120);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        var partyTeam = proposal.Blue.SelectMany(e => e.Members).Count(m => party.Contains(m.AccountId)) is 3 ? proposal.Blue : proposal.Red;
        Assert.That(partyTeam.SelectMany(e => e.Members).Count(m => party.Contains(m.AccountId)), Is.EqualTo(3));
        var other = partyTeam == proposal.Blue ? proposal.Red : proposal.Blue;
        Assert.That(other.SelectMany(e => e.Members).Count(m => party.Contains(m.AccountId)), Is.EqualTo(0));
    }

    /// <summary>
    /// The cooperative match starts at once: the humans (completed with bots) against five bots; the bots answer on their own.
    /// </summary>
    [Test]
    public void CoopMatchStartsAtOnceAgainstFiveBots()
    {
        var humans = this.Join(MobaQueueMode.Coop, 2, together: true);

        var check = this._queue.Tick().Single().Check;

        Assert.That(check.Proposal.Blue.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(check.Proposal.Red.SelectMany(e => e.Members).All(m => m.IsBot), Is.True);
        Assert.That(check.Proposal.Red.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(check.AcceptedCount, Is.EqualTo(8));
        Assert.That(this._queue.Answer(humans[0], true), Is.Empty);
        Assert.That(this._queue.Answer(humans[1], true).Single().Kind, Is.EqualTo(MobaQueueEventKind.MatchReady));
    }

    /// <summary>
    /// A failed ready check never sends the bots back to the queue; the humans who accepted go back.
    /// </summary>
    [Test]
    public void FailedCheckRequeuesOnlyHumans()
    {
        var humans = this.Join(MobaQueueMode.WithBots, 2);
        this._now = this._now.AddSeconds(360);
        this._queue.Tick();

        var failed = this._queue.Answer(humans[0], false).Single();

        Assert.That(failed.Kind, Is.EqualTo(MobaQueueEventKind.ReadyCheckFailed));
        Assert.That(failed.Dropped.SelectMany(e => e.Members).Select(m => m.AccountId), Is.EquivalentTo(new[] { humans[0] }));
        Assert.That(failed.Requeued.SelectMany(e => e.Members).Select(m => m.AccountId), Is.EquivalentTo(new[] { humans[1] }));
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.WithBots), Is.EqualTo(1));
    }

    /// <summary>
    /// The size of every pool is checked.
    /// </summary>
    [Test]
    public void PoolSizesAreChecked()
    {
        Assert.That(this._queue.Join(MobaQueueMode.WithBots, Members(5, true)), Is.EqualTo(MobaQueueJoinResult.Joined));
        Assert.That(this._queue.Join(MobaQueueMode.Coop, Members(6, true)), Is.EqualTo(MobaQueueJoinResult.InvalidSize));
        Assert.That(this._queue.Join(MobaQueueMode.Open, Members(5, true)), Is.EqualTo(MobaQueueJoinResult.InvalidSize));
    }

    /// <summary>
    /// The steps are written for the players.
    /// </summary>
    [Test]
    public void StagesAreDescribed()
    {
        Assert.That(MobaMatchmaker.HumansNeeded(TimeSpan.FromSeconds(119)), Is.Null);
        Assert.That(MobaMatchmaker.HumansNeeded(TimeSpan.FromSeconds(120)), Is.EqualTo(6));
        Assert.That(MobaMatchmaker.HumansNeeded(TimeSpan.FromSeconds(181)), Is.EqualTo(4));
        Assert.That(MobaMatchmaker.HumansNeeded(TimeSpan.FromHours(1)), Is.EqualTo(1));
        Assert.That(MobaMatchmaker.DescribeBotStages(), Does.Contain("2:00").And.Contain("6:00"));
    }

    private static List<MobaQueueMember> Members(int count, bool _)
        => Enumerable.Range(1, count).Select(i => new MobaQueueMember(Guid.NewGuid(), "P" + i)).ToList();

    /// <summary>Joins <paramref name="count"/> players: as one party, or each alone.</summary>
    private List<Guid> Join(MobaQueueMode mode, int count, bool together = false)
    {
        var accounts = new List<Guid>();
        if (together)
        {
            var members = Members(count, true);
            Assert.That(this._queue.Join(mode, members), Is.EqualTo(MobaQueueJoinResult.Joined));
            accounts.AddRange(members.Select(m => m.AccountId));
            return accounts;
        }

        for (var i = 0; i < count; i++)
        {
            var member = Members(1, true);
            Assert.That(this._queue.Join(mode, member), Is.EqualTo(MobaQueueJoinResult.Joined));
            accounts.Add(member[0].AccountId);
            this._now = this._now.AddMilliseconds(1);
        }

        return accounts;
    }
}
