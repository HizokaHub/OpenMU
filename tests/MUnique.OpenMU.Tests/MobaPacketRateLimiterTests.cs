// <copyright file="MobaPacketRateLimiterTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameServer.MessageHandler;

/// <summary>
/// Tests the per-player limit of the custom D5 packets of the MOBA (security for the closed beta).
/// </summary>
[TestFixture]
public class MobaPacketRateLimiterTests
{
    /// <summary>The limit drops the packets over the window and recovers in the next window.</summary>
    [Test]
    public void DropsTheExcessAndRecoversInTheNextWindow()
    {
        var counter = new PacketRateLimiter.Counter();
        var accepted = 0;
        for (var i = 0; i < PacketRateLimiter.MaxPerWindow + 40; i++)
        {
            if (PacketRateLimiter.Allow(counter, 5000 + i))
            {
                accepted++;
            }
        }

        Assert.That(accepted, Is.EqualTo(PacketRateLimiter.MaxPerWindow));
        Assert.That(PacketRateLimiter.Allow(counter, 5000 + PacketRateLimiter.WindowTicks + 10), Is.True);
    }

    /// <summary>A normal client (a few packets per second) is never limited.</summary>
    [Test]
    public void ANormalClientIsNeverLimited()
    {
        var counter = new PacketRateLimiter.Counter();
        for (var i = 0; i < 600; i++)
        {
            Assert.That(PacketRateLimiter.Allow(counter, 1000 + (i * 100)), Is.True, $"packet {i}");
        }
    }
}
