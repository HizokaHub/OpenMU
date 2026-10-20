// <copyright file="MobaQueuePenaltyTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence.InMemory;

/// <summary>
/// Tests the warnings and blocks of the MOBA queue: the rules, and that the penalty is stored per account.
/// </summary>
[TestFixture]
public class MobaQueuePenaltyTests
{
    private static readonly DateTime Start = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// The first two failures only warn.
    /// </summary>
    [Test]
    public void TwoFailuresOnlyWarn()
    {
        var penalty = new MobaQueuePenalty();

        var first = MobaQueuePenaltyRules.RegisterFailure(penalty, Start);
        var second = MobaQueuePenaltyRules.RegisterFailure(penalty, Start.AddMinutes(1));

        Assert.That(first, Is.EqualTo(new MobaPenaltyOutcome(1, null, null)));
        Assert.That(second, Is.EqualTo(new MobaPenaltyOutcome(2, null, null)));
        Assert.That(MobaQueuePenaltyRules.BlockRemaining(penalty, Start.AddMinutes(1)), Is.Null);
    }

    /// <summary>
    /// The third failure blocks for 5 minutes and the warnings stay at 3.
    /// </summary>
    [Test]
    public void ThirdFailureBlocksForFiveMinutes()
    {
        var penalty = new MobaQueuePenalty();
        Fail(penalty, 2);

        var outcome = MobaQueuePenaltyRules.RegisterFailure(penalty, Start);

        Assert.That(outcome.Warnings, Is.EqualTo(3));
        Assert.That(outcome.BlockedFor, Is.EqualTo(TimeSpan.FromMinutes(5)));
        Assert.That(outcome.BlockedUntil, Is.EqualTo(Start.AddMinutes(5)));
        Assert.That(MobaQueuePenaltyRules.BlockRemaining(penalty, Start.AddMinutes(2)), Is.EqualTo(TimeSpan.FromMinutes(3)));
        Assert.That(MobaQueuePenaltyRules.BlockRemaining(penalty, Start.AddMinutes(5)), Is.Null);
    }

    /// <summary>
    /// Every failure after a served block blocks again, longer each time, up to 12 hours.
    /// </summary>
    [Test]
    public void BlocksGetLongerUpToTheLimit()
    {
        var penalty = new MobaQueuePenalty();
        Fail(penalty, 2);
        var expected = new[] { 5, 15, 30, 60, 120, 300, 720, 720 };
        var now = Start;

        foreach (var minutes in expected)
        {
            var outcome = MobaQueuePenaltyRules.RegisterFailure(penalty, now);
            Assert.That(outcome.BlockedFor, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
            Assert.That(outcome.Warnings, Is.EqualTo(3));
            now = outcome.BlockedUntil!.Value.AddMinutes(1);
        }
    }

    /// <summary>
    /// Warnings expire one by one every 2 days, and with none left the block level starts over.
    /// </summary>
    [Test]
    public void WarningsExpireEveryTwoDays()
    {
        var penalty = new MobaQueuePenalty();
        Fail(penalty, 3);

        MobaQueuePenaltyRules.Expire(penalty, Start.AddDays(1.9));
        Assert.That(penalty.Warnings, Is.EqualTo(3));

        MobaQueuePenaltyRules.Expire(penalty, Start.AddDays(2));
        Assert.That(penalty.Warnings, Is.EqualTo(2));
        Assert.That(penalty.BlockLevel, Is.EqualTo(1));

        MobaQueuePenaltyRules.Expire(penalty, Start.AddDays(5));
        Assert.That(penalty.Warnings, Is.EqualTo(1));

        MobaQueuePenaltyRules.Expire(penalty, Start.AddDays(6));
        Assert.That(penalty.Warnings, Is.EqualTo(0));
        Assert.That(penalty.BlockLevel, Is.EqualTo(0));
        Assert.That(penalty.LastWarningUtc, Is.Null);
    }

    /// <summary>
    /// After warnings expired from 3 to 2, the next failure blocks again (and longer than the first block).
    /// </summary>
    [Test]
    public void FailureAfterPartialExpiryBlocksAgain()
    {
        var penalty = new MobaQueuePenalty();
        Fail(penalty, 3);

        var outcome = MobaQueuePenaltyRules.RegisterFailure(penalty, Start.AddDays(2));

        Assert.That(outcome.Warnings, Is.EqualTo(3));
        Assert.That(outcome.BlockedFor, Is.EqualTo(TimeSpan.FromMinutes(15)));
    }

    /// <summary>
    /// Accepting a match after serving a block clears everything; before the block ended, or without any block, it changes nothing.
    /// </summary>
    [Test]
    public void AcceptingAfterAServedBlockClearsTheSlate()
    {
        var penalty = new MobaQueuePenalty();
        Fail(penalty, 3);

        Assert.That(MobaQueuePenaltyRules.RegisterAcceptance(penalty, Start.AddMinutes(1)), Is.False);
        Assert.That(penalty.Warnings, Is.EqualTo(3));

        Assert.That(MobaQueuePenaltyRules.RegisterAcceptance(penalty, Start.AddMinutes(6)), Is.True);
        Assert.That(penalty.Warnings, Is.EqualTo(0));
        Assert.That(penalty.BlockLevel, Is.EqualTo(0));
        Assert.That(penalty.BlockedUntil, Is.Null);

        var withoutBlock = new MobaQueuePenalty();
        Fail(withoutBlock, 2);
        Assert.That(MobaQueuePenaltyRules.RegisterAcceptance(withoutBlock, Start.AddHours(1)), Is.False);
        Assert.That(withoutBlock.Warnings, Is.EqualTo(2));
    }

    /// <summary>
    /// The durations are written the way the players read them.
    /// </summary>
    [Test]
    public void DurationsAreReadable()
    {
        Assert.That(MobaQueuePenalties.FormatDuration(TimeSpan.FromSeconds(20)), Is.EqualTo("20 s"));
        Assert.That(MobaQueuePenalties.FormatDuration(TimeSpan.FromMinutes(5)), Is.EqualTo("5 min"));
        Assert.That(MobaQueuePenalties.FormatDuration(TimeSpan.FromMinutes(90)), Is.EqualTo("1 h 30 min"));
        Assert.That(MobaQueuePenalties.FormatDuration(TimeSpan.FromHours(12)), Is.EqualTo("12 h"));
    }

    /// <summary>
    /// The penalty and the incident are stored, and found again by account.
    /// </summary>
    [Test]
    public async Task PenaltyIsStoredPerAccount()
    {
        var provider = new InMemoryPersistenceContextProvider();
        var configuration = new GameConfiguration();
        var account = Guid.NewGuid();
        using (var context = provider.CreateNewPlayerContext(configuration))
        {
            var penalty = context.CreateNew<MobaQueuePenalty>();
            penalty.AccountId = account;
            MobaQueuePenaltyRules.RegisterFailure(penalty, Start);
            var incident = context.CreateNew<MobaQueueIncident>();
            incident.AccountId = account;
            incident.Reason = MobaQueueFailureReason.TimedOut;
            await context.SaveChangesAsync();
        }

        using var other = provider.CreateNewPlayerContext(configuration);
        var found = await other.GetMobaQueuePenaltyAsync(account);
        var missing = await other.GetMobaQueuePenaltyAsync(Guid.NewGuid());

        Assert.That(found, Is.Not.Null);
        Assert.That(found!.Warnings, Is.EqualTo(1));
        Assert.That(missing, Is.Null);
    }

    private static void Fail(MobaQueuePenalty penalty, int times)
    {
        for (var i = 0; i < times; i++)
        {
            MobaQueuePenaltyRules.RegisterFailure(penalty, Start);
        }
    }
}
