// <copyright file="MobaBuffTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the duration and cooldown tables of the buffs and the aura of the base.
/// </summary>
[TestFixture]
public class MobaBuffTests
{
    /// <summary>
    /// Every buff lasts between 45 and 130 seconds (decided with the user).
    /// </summary>
    [Test]
    public void BuffDurationsAreBetween45And130Seconds()
    {
        foreach (var (skill, seconds) in MobaBuffs.DurationSeconds)
        {
            Assert.That(seconds, Is.InRange(MobaBuffs.MinSeconds, MobaBuffs.MaxSeconds), $"skill {skill}");
        }
    }

    /// <summary>
    /// Every timed buff waits at least as long as it lasts at rank 1 and gets shorter with the ranks (the instant ones too).
    /// </summary>
    [Test]
    public void BuffCooldownsShrinkWithTheRank()
    {
        foreach (var (skill, seconds) in MobaBuffs.DurationSeconds.Where(d => d.Key != 77))
        {
            Assert.That(MobaCooldowns.SecondsOf(skill, 1), Is.GreaterThan(seconds), $"skill {skill}");
            Assert.That(MobaCooldowns.SecondsOf(skill, 5), Is.LessThan(MobaCooldowns.SecondsOf(skill, 1)), $"skill {skill}");
        }

        Assert.That(MobaCooldowns.SecondsOf(6, 1), Is.EqualTo(32));
        Assert.That(MobaCooldowns.SecondsOf(6, 5), Is.EqualTo(20));
    }

    /// <summary>
    /// Increase Block gives crowd-control resistance that grows with the rank.
    /// </summary>
    [Test]
    public void IncreaseBlockGivesCrowdControlResistance()
    {
        Assert.That(MobaBuffPower.CcResistForRank(0), Is.EqualTo(0));
        Assert.That(MobaBuffPower.CcResistForRank(1), Is.EqualTo(0.10).Within(1e-9));
        Assert.That(MobaBuffPower.CcResistForRank(5), Is.EqualTo(0.26).Within(1e-9));
    }

    /// <summary>
    /// The aura of the blacksmith covers its radius and only for the team that owns the base.
    /// </summary>
    [Test]
    public void BaseAuraCoversTheRadiusAroundTheVendor()
    {
        var shop = MobaLayout.ShopOf(MobaTeam.Blue);
        Assert.That(MobaBaseAura.Contains(MobaTeam.Blue, shop.X, shop.Y), Is.True);
        Assert.That(MobaBaseAura.Contains(MobaTeam.Blue, shop.X + (int)MobaBaseAura.RadiusTiles, shop.Y), Is.True);
        Assert.That(MobaBaseAura.Contains(MobaTeam.Blue, shop.X + (int)MobaBaseAura.RadiusTiles + 2, shop.Y), Is.False);
        Assert.That(MobaBaseAura.Contains(MobaTeam.None, shop.X, shop.Y), Is.False);
        Assert.That(MobaBaseAura.Contains(MobaTeam.Red, shop.X, shop.Y), Is.False);
    }
}
