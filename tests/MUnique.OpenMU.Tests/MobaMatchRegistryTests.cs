// <copyright file="MobaMatchRegistryTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the match registry which keeps the clone and the champion state across reconnects.
/// </summary>
[TestFixture]
public class MobaMatchRegistryTests
{
    /// <summary>
    /// Entering a match registers the clone with a fresh state, which later lookups return again.
    /// </summary>
    [Test]
    public void StateOutlivesSessionsUntilTheAccountLeaves()
    {
        var account = Guid.NewGuid();
        var clone = new Character();
        MobaMatchRegistry.Enter(account, clone);

        Assert.That(MobaMatchRegistry.TryGetState(account, out var first), Is.True);
        first!.Level = 12;
        first.Kills = 3;
        first.Team = MobaTeam.Red;
        first.SkillCooldowns[7] = DateTime.UtcNow.AddSeconds(30);

        Assert.That(MobaMatchRegistry.TryGetState(account, out var second), Is.True);
        Assert.That(second, Is.SameAs(first));
        Assert.That(second!.Level, Is.EqualTo(12));
        Assert.That(second.Team, Is.EqualTo(MobaTeam.Red));
        Assert.That(second.SkillCooldowns, Contains.Key((short)7));
        Assert.That(MobaMatchRegistry.TryGetClone(account, out var found) && ReferenceEquals(found, clone), Is.True);

        Assert.That(MobaMatchRegistry.Leave(account), Is.SameAs(clone));
        Assert.That(MobaMatchRegistry.IsInMatch(account), Is.False);
        Assert.That(MobaMatchRegistry.TryGetState(account, out _), Is.False);
    }

    /// <summary>
    /// A new match of the same account starts again from level 1.
    /// </summary>
    [Test]
    public void ReEnteringStartsFresh()
    {
        var account = Guid.NewGuid();
        MobaMatchRegistry.Enter(account, new Character());
        MobaMatchRegistry.TryGetState(account, out var old);
        old!.Level = 20;

        MobaMatchRegistry.Enter(account, new Character());
        MobaMatchRegistry.TryGetState(account, out var fresh);

        Assert.That(fresh, Is.Not.SameAs(old));
        Assert.That(fresh!.Level, Is.EqualTo(1));
        MobaMatchRegistry.Leave(account);
    }

    /// <summary>
    /// Leaving without being in a match is harmless.
    /// </summary>
    [Test]
    public void LeavingWithoutMatchReturnsNull()
    {
        Assert.That(MobaMatchRegistry.Leave(Guid.NewGuid()), Is.Null);
    }
}
