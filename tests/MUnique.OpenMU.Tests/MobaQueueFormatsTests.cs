// <copyright file="MobaQueueFormatsTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the queue of the other formats: 3v3 and 1v1, which have their own pools and team sizes.
/// </summary>
[TestFixture]
public class MobaQueueFormatsTests
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
    /// Six humans make a 3v3 at once.
    /// </summary>
    [Test]
    public void SixHumansMakeATrioAtOnce()
    {
        this.Join(MobaFormat.Trio, MobaQueueMode.WithBots, 5);
        Assert.That(this._queue.Tick(), Is.Empty);

        this.Join(MobaFormat.Trio, MobaQueueMode.WithBots, 1);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Format, Is.EqualTo(MobaFormat.Trio));
        Assert.That(proposal.Blue.Sum(e => e.Size), Is.EqualTo(3));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(3));
        Assert.That(proposal.Players.Count(p => p.IsBot), Is.EqualTo(0));
    }

    /// <summary>
    /// Before 10 minutes only a full match of humans starts; at 10 minutes four humans are enough and bots fill the two places.
    /// </summary>
    [Test]
    public void TrioWithFourHumansStartsAfterTenMinutesWithBots()
    {
        this.Join(MobaFormat.Trio, MobaQueueMode.WithBots, 4);

        this._now = this._now.AddMinutes(9).AddSeconds(59);
        Assert.That(this._queue.Tick(), Is.Empty);

        this._now = this._now.AddSeconds(1);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Players.Count(p => !p.IsBot), Is.EqualTo(4));
        Assert.That(proposal.Players.Count(p => p.IsBot), Is.EqualTo(2));
        Assert.That(proposal.Blue.Sum(e => e.Size), Is.EqualTo(3));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(3));
    }

    /// <summary>
    /// With fewer than four humans the 3v3 keeps waiting, however long it takes.
    /// </summary>
    [Test]
    public void TrioWithThreeHumansKeepsWaiting()
    {
        this.Join(MobaFormat.Trio, MobaQueueMode.WithBots, 3);

        this._now = this._now.AddHours(1);

        Assert.That(this._queue.Tick(), Is.Empty);
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.WithBots, MobaFormat.Trio), Is.EqualTo(3));
    }

    /// <summary>
    /// A party of three stays together in the 3v3.
    /// </summary>
    [Test]
    public void TrioPartyOfThreeFacesThreeSolos()
    {
        var party = this.Join(MobaFormat.Trio, MobaQueueMode.WithBots, 3, together: true);
        this.Join(MobaFormat.Trio, MobaQueueMode.WithBots, 3);

        var proposal = this._queue.Tick().Single().Check.Proposal;

        var partyTeam = proposal.Blue.SelectMany(e => e.Members).Any(m => party.Contains(m.AccountId)) ? proposal.Blue : proposal.Red;
        Assert.That(partyTeam.SelectMany(e => e.Members).Count(m => party.Contains(m.AccountId)), Is.EqualTo(3));
    }

    /// <summary>
    /// Two humans make a 1v1, one against one.
    /// </summary>
    [Test]
    public void TwoHumansMakeADuel()
    {
        this.Join(MobaFormat.Duel, MobaQueueMode.Open, 1);
        Assert.That(this._queue.Tick(), Is.Empty);

        this.Join(MobaFormat.Duel, MobaQueueMode.Open, 1);
        var proposal = this._queue.Tick().Single().Check.Proposal;

        Assert.That(proposal.Format, Is.EqualTo(MobaFormat.Duel));
        Assert.That(proposal.Blue.Sum(e => e.Size), Is.EqualTo(1));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(1));
    }

    /// <summary>
    /// A duel does not take parties.
    /// </summary>
    [Test]
    public void DuelRejectsParties()
    {
        Assert.That(this._queue.Join(MobaQueueMode.Open, Members(2), null, MobaFormat.Duel), Is.EqualTo(MobaQueueJoinResult.InvalidSize));
    }

    /// <summary>
    /// The formats do not mix: classic and 3v3 players never end up in the same match.
    /// </summary>
    [Test]
    public void FormatsDoNotMix()
    {
        this.Join(MobaFormat.Classic, MobaQueueMode.Open, 5);
        this.Join(MobaFormat.Trio, MobaQueueMode.WithBots, 3);

        Assert.That(this._queue.Tick(), Is.Empty);
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.Open), Is.EqualTo(5));
        Assert.That(this._queue.WaitingPlayers(MobaQueueMode.WithBots, MobaFormat.Trio), Is.EqualTo(3));
    }

    /// <summary>
    /// The cooperative match takes the team size of its format.
    /// </summary>
    [Test]
    public void CoopBotTeamHasTheFormatsSize()
    {
        var proposal = MobaMatchmaker.FormCoop(new MobaQueueEntry(MobaQueueMode.Coop, Members(1), this._now, MobaFormat.Trio), MobaFormat.Trio);

        Assert.That(proposal.Blue.Sum(e => e.Size), Is.EqualTo(3));
        Assert.That(proposal.Red.Sum(e => e.Size), Is.EqualTo(3));
        Assert.That(proposal.Red.SelectMany(e => e.Members).All(m => m.IsBot), Is.True);
    }

    /// <summary>
    /// The ARAM search works like the search with bots, in its own pool and format.
    /// </summary>
    [Test]
    public void AramStartsWithBotsAfterTheFirstStep()
    {
        this.Join(MobaFormat.Aram, MobaQueueMode.WithBots, 6);
        this.Join(MobaFormat.Classic, MobaQueueMode.WithBots, 1);

        this._now = this._now.AddSeconds(120);
        var proposals = this._queue.Tick().Select(e => e.Check.Proposal).ToList();

        var aram = proposals.Single(p => p.Format == MobaFormat.Aram);
        Assert.That(aram.Blue.Sum(e => e.Size), Is.EqualTo(5));
        Assert.That(aram.Players.Count(p => !p.IsBot), Is.EqualTo(6));
        Assert.That(proposals.Count(p => p.Format == MobaFormat.Classic), Is.EqualTo(0));
    }

    /// <summary>
    /// Only the formats which play the middle lane have walls: two along the corridor and one across each end, tile-exact.
    /// </summary>
    [Test]
    public void OnlyMiddleLaneFormatsHaveWalls()
    {
        Assert.That(MobaLaneWalls.RectsFor(MobaFormat.Classic), Is.Empty);
        Assert.That(MobaLaneWalls.RectsFor(MobaFormat.Trio), Is.Empty);

        var rects = MobaLaneWalls.RectsFor(MobaFormat.Duel);
        Assert.That(rects, Has.Count.EqualTo(4));
        Assert.That(MobaLaneWalls.RectsFor(MobaFormat.Aram), Is.EqualTo(rects));
        Assert.That(rects[0], Is.EqualTo(new MobaWallRect(104, 3, 2, 250)));
        Assert.That(rects[1], Is.EqualTo(new MobaWallRect(131, 3, 2, 250)));
    }

    /// <summary>
    /// The corridor is closed: the walls are solid on both sides and at both ends, and open inside.
    /// </summary>
    [Test]
    public void WallsEncloseTheCorridor()
    {
        Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 104, 100), Is.True);
        Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 132, 100), Is.True);
        Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 118, 3), Is.True);
        Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 118, 252), Is.True);
        Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 118, 100), Is.False);
        Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 103, 100), Is.False);
        Assert.That(MobaLaneWalls.IsWall(MobaFormat.Classic, 104, 100), Is.False);

        // No gap: every column of the corridor's width is covered at both ends, every row at both sides.
        for (var x = 104; x <= 132; x++)
        {
            Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, x, 4), Is.True, $"top, x={x}");
            Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, x, 251), Is.True, $"bottom, x={x}");
        }

        for (var y = 3; y <= 252; y++)
        {
            Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 105, y), Is.True, $"left, y={y}");
            Assert.That(MobaLaneWalls.IsWall(MobaFormat.Duel, 131, y), Is.True, $"right, y={y}");
        }
    }

    private static List<MobaQueueMember> Members(int count)
        => Enumerable.Range(1, count).Select(i => new MobaQueueMember(Guid.NewGuid(), "P" + i)).ToList();

    private List<Guid> Join(MobaFormat format, MobaQueueMode mode, int count, bool together = false)
    {
        var accounts = new List<Guid>();
        if (together)
        {
            var members = Members(count);
            Assert.That(this._queue.Join(mode, members, null, format), Is.EqualTo(MobaQueueJoinResult.Joined));
            accounts.AddRange(members.Select(m => m.AccountId));
            return accounts;
        }

        for (var i = 0; i < count; i++)
        {
            var member = Members(1);
            Assert.That(this._queue.Join(mode, member, null, format), Is.EqualTo(MobaQueueJoinResult.Joined));
            accounts.Add(member[0].AccountId);
            this._now = this._now.AddMilliseconds(1);
        }

        return accounts;
    }
}
