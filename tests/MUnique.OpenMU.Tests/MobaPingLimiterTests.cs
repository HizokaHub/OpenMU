// <copyright file="MobaPingLimiterTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The ping limit: five pings per five seconds, then six seconds without pings.
/// </summary>
[TestFixture]
public class MobaPingLimiterTests
{
    /// <summary>
    /// Five pings go through, the sixth is refused and starts the lockout.
    /// </summary>
    [Test]
    public void TheSixthPingInTheWindowIsRefused()
    {
        var limiter = new MobaPingLimiter();
        var start = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < MobaPingLimiter.MaxPings; i++)
        {
            Assert.That(limiter.TryAcquire(start.AddMilliseconds(100 * i)), Is.True, $"ping {i + 1}");
        }

        Assert.That(limiter.TryAcquire(start.AddMilliseconds(600)), Is.False);
    }

    /// <summary>
    /// While locked out nothing goes through; after the lockout it works again.
    /// </summary>
    [Test]
    public void TheLockoutLastsSixSeconds()
    {
        var limiter = new MobaPingLimiter();
        var start = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i <= MobaPingLimiter.MaxPings; i++)
        {
            limiter.TryAcquire(start);
        }

        Assert.That(limiter.TryAcquire(start.AddSeconds(MobaPingLimiter.LockoutSeconds - 0.5)), Is.False);
        Assert.That(limiter.TryAcquire(start.AddSeconds(MobaPingLimiter.LockoutSeconds + 0.5)), Is.True);
    }

    /// <summary>
    /// Pings spread over more than the window never hit the limit.
    /// </summary>
    [Test]
    public void SlowPingsAreNeverLimited()
    {
        var limiter = new MobaPingLimiter();
        var start = new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 30; i++)
        {
            Assert.That(limiter.TryAcquire(start.AddSeconds(2 * i)), Is.True, $"ping {i + 1}");
        }
    }

    /// <summary>
    /// The ping kinds are part of the packet: their numbers must not move.
    /// </summary>
    [Test]
    public void PingKindNumbersAreStable()
    {
        Assert.That((byte)MobaPingKind.Generic, Is.EqualTo(1));
        Assert.That((byte)MobaPingKind.Retreat, Is.EqualTo(3));
        Assert.That((byte)MobaPingKind.EnemyVision, Is.EqualTo(10));
        Assert.That(MobaPing.IsKnownKind(0), Is.False);
        Assert.That(MobaPing.IsKnownKind(10), Is.True);
        Assert.That(MobaPing.IsKnownKind(11), Is.False);
    }
}
