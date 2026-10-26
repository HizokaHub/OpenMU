// <copyright file="MobaInhibitorTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The rules of the inhibitors and the super-minions (approved 2026-10-26): the nexus can only be attacked while an inhibitor of its team is down, a fallen
/// inhibitor sends one super-minion per wave down its lane (two in every lane when all three are down), and each inhibitor stands behind the base turret of its lane.
/// </summary>
[TestFixture]
public class MobaInhibitorTests
{
    /// <summary>
    /// How many super-minions a wave carries.
    /// </summary>
    /// <param name="inhibitors">The inhibitors of the enemy.</param>
    /// <param name="down">How many are down.</param>
    /// <param name="laneDown">Whether the one of the lane is down.</param>
    /// <param name="expected">The super-minions.</param>
    [TestCase(3, 0, false, 0)]
    [TestCase(3, 1, true, 1)]
    [TestCase(3, 2, true, 1)]
    [TestCase(3, 2, false, 0)]
    [TestCase(3, 3, true, 2)]
    [TestCase(1, 1, true, 1)]
    [TestCase(1, 0, false, 0)]
    public void SuperMinionsPerWave(int inhibitors, int down, bool laneDown, int expected)
    {
        Assert.That(MobaInhibitors.SuperMinionCount(inhibitors, down, laneDown), Is.EqualTo(expected));
    }

    /// <summary>
    /// The nexus is shielded unless an inhibitor of its team is down; without inhibitors the old rule (a whole lane fallen) holds.
    /// </summary>
    /// <param name="hasInhibitors">Whether the team has inhibitors.</param>
    /// <param name="anyDown">Whether one is down.</param>
    /// <param name="laneFallen">The old rule.</param>
    /// <param name="shielded">The expected result.</param>
    [TestCase(true, false, false, true)]
    [TestCase(true, false, true, true)]
    [TestCase(true, true, false, false)]
    [TestCase(false, false, false, true)]
    [TestCase(false, false, true, false)]
    public void NexusShieldRule(bool hasInhibitors, bool anyDown, bool laneFallen, bool shielded)
    {
        Assert.That(MobaInhibitors.NexusShielded(hasInhibitors, anyDown, laneFallen), Is.EqualTo(shielded));
    }

    /// <summary>
    /// The numbers the players were told: 5 minutes, 100 gold, the EXP of a turret, five times the life and two and a half times the damage.
    /// </summary>
    [Test]
    public void ApprovedNumbers()
    {
        Assert.That(MobaInhibitors.RespawnSeconds, Is.EqualTo(300));
        Assert.That(MobaInhibitors.KillGold, Is.EqualTo(100));
        Assert.That(MobaInhibitors.KillExp, Is.EqualTo(MobaLevels.TurretKillExp));
        Assert.That(MobaSuperMinions.HealthFactor, Is.EqualTo(5f));
        Assert.That(MobaSuperMinions.DamageFactor, Is.EqualTo(2.5f));
        Assert.That(MobaSuperMinions.DefenseFactor, Is.EqualTo(2f));
        Assert.That(MobaSuperMinions.RewardFactor, Is.EqualTo(3));
        Assert.That(MobaSuperMinions.SizePercent, Is.EqualTo(135));
    }

    /// <summary>
    /// The models of the super-minions are different for each team and are recognised.
    /// </summary>
    [Test]
    public void SuperMinionModels()
    {
        Assert.That(MobaSuperMinions.ModelOf(MobaTeam.Blue), Is.Not.EqualTo(MobaSuperMinions.ModelOf(MobaTeam.Red)));
        Assert.That(MobaSuperMinions.IsSuperModel(MobaSuperMinions.BlueModel), Is.True);
        Assert.That(MobaSuperMinions.IsSuperModel(MobaSuperMinions.RedModel), Is.True);
        Assert.That(MobaSuperMinions.IsSuperModel(3), Is.False);
    }

    /// <summary>
    /// One inhibitor per lane and team, on walkable ground, behind the base turret of the lane (not on top of it), and the red ones mirror the blue ones.
    /// </summary>
    [Test]
    public void InhibitorsStandBehindTheBaseTurrets()
    {
        var inhibitors = MobaLayout.Inhibitors;
        Assert.That(inhibitors.Count, Is.EqualTo(6));
        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            Assert.That(inhibitors.Where(i => i.Team == team).Select(i => i.Lane).OrderBy(l => l), Is.EqualTo(new[] { 0, 1, 2 }), team.ToString());
        }

        foreach (var inhibitor in inhibitors)
        {
            Assert.That(MobaLayout.IsWalkable(inhibitor.Position.X, inhibitor.Position.Y), Is.True, $"{inhibitor.Team} lane {inhibitor.Lane}");
            var baseTurret = MobaLayout.Towers.Single(t => t.Team == inhibitor.Team && t.Lane == inhibitor.Lane && t.Tier == 2);
            var distanceToTurret = Math.Sqrt(Math.Pow(baseTurret.Position.X - inhibitor.Position.X, 2) + Math.Pow(baseTurret.Position.Y - inhibitor.Position.Y, 2));
            Assert.That(distanceToTurret, Is.InRange(4.0, 14.0), $"{inhibitor.Team} lane {inhibitor.Lane} is {distanceToTurret:0.0} tiles from its base turret");
            var nexus = MobaLayout.NexusOf(inhibitor.Team);
            var distanceToNexus = Math.Sqrt(Math.Pow(nexus.X - inhibitor.Position.X, 2) + Math.Pow(nexus.Y - inhibitor.Position.Y, 2));
            var turretToNexus = Math.Sqrt(Math.Pow(nexus.X - baseTurret.Position.X, 2) + Math.Pow(nexus.Y - baseTurret.Position.Y, 2));
            Assert.That(distanceToNexus, Is.LessThan(turretToNexus), $"{inhibitor.Team} lane {inhibitor.Lane}: the inhibitor must be closer to its nexus than the base turret");
        }
    }

    /// <summary>
    /// The announcer events of the inhibitors are part of the packet: their numbers must not move.
    /// </summary>
    [Test]
    public void InhibitorAnnouncementNumbers()
    {
        Assert.That((byte)MobaAnnouncement.InhibitorDestroyed, Is.EqualTo(21));
        Assert.That((byte)MobaAnnouncement.InhibitorRestored, Is.EqualTo(22));
        Assert.That((byte)MobaAnnouncement.SuperMinions, Is.EqualTo(23));
        Assert.That(MobaMinimapKind.Inhibitor, Is.EqualTo(5));
    }
}
