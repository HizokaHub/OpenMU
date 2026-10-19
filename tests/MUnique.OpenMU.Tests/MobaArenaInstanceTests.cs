// <copyright file="MobaArenaInstanceTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Tests the extra map instances which let several MOBA matches run side by side.
/// </summary>
[TestFixture]
public class MobaArenaInstanceTests
{
    /// <summary>
    /// A player bound to an instance enters that instance; the others enter the shared map. Nothing is shared between them.
    /// </summary>
    [Test]
    public async Task BoundPlayersEnterTheirOwnInstance()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var shared = await context.GetMapAsync(0);
        var first = await context.CreateInstanceMapAsync(0);
        var second = await context.CreateInstanceMapAsync(0);

        Assert.That(first, Is.Not.Null.And.Not.SameAs(second).And.Not.SameAs(shared));
        Assert.That(first!.IsInstance && second!.IsInstance, Is.True);
        Assert.That(shared!.IsInstance, Is.False);
        Assert.That((await context.GetMapsAsync()).Count(m => m.MapId == 0), Is.EqualTo(3));

        var inFirst = await PlayerTestHelper.CreatePlayerAsync(context);
        var inSecond = await PlayerTestHelper.CreatePlayerAsync(context);
        var inShared = await PlayerTestHelper.CreatePlayerAsync(context);
        Assert.That(inFirst.CurrentMap, Is.SameAs(shared), "the test helper enters the shared map");

        // The path of the real game: bind, warp (the map change), then the client reports it loaded the map.
        var spot = new Point(100, 100);
        inFirst.BoundMap = first;
        await inFirst.TeleportToMapAsync(first!, spot);
        await inFirst.ClientReadyAfterMapChangeAsync();
        inSecond.BoundMap = second;
        await inSecond.TeleportToMapAsync(second!, spot);
        await inSecond.ClientReadyAfterMapChangeAsync();

        Assert.That(inFirst.CurrentMap, Is.SameAs(first));
        Assert.That(inSecond.CurrentMap, Is.SameAs(second));
        Assert.That(inShared.CurrentMap, Is.SameAs(shared));

        var centre = new Point(128, 128);
        Assert.That(first.GetAttackablesInRange(centre, 255), Does.Contain(inFirst).And.Not.Contain(inSecond).And.Not.Contain(inShared));
        Assert.That(second!.GetAttackablesInRange(centre, 255), Does.Contain(inSecond).And.Not.Contain(inFirst).And.Not.Contain(inShared));
        Assert.That(shared!.GetAttackablesInRange(centre, 255), Does.Contain(inShared).And.Not.Contain(inFirst).And.Not.Contain(inSecond));
    }

    /// <summary>
    /// A removed instance is no longer hosted; the shared map cannot be removed that way.
    /// </summary>
    [Test]
    public async Task RemovedInstanceDisappears()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var shared = await context.GetMapAsync(0);
        var instance = await context.CreateInstanceMapAsync(0);

        Assert.That(await context.RemoveInstanceMapAsync(instance!), Is.True);
        Assert.That(await context.RemoveInstanceMapAsync(instance!), Is.False);
        Assert.That(await context.RemoveInstanceMapAsync(shared!), Is.False);
        Assert.That((await context.GetMapsAsync()).Count(m => m.MapId == 0), Is.EqualTo(1));
    }

    /// <summary>
    /// An unknown map number gives no instance.
    /// </summary>
    [Test]
    public async Task UnknownMapGivesNoInstance()
    {
        var context = GameContextTestHelper.CreateGameContext();

        Assert.That(await context.CreateInstanceMapAsync(250), Is.Null);
    }
}
