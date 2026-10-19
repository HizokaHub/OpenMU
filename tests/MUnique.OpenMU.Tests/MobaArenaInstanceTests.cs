// <copyright file="MobaArenaInstanceTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
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

    /// <summary>
    /// Every instance has its own match state (clock, phase), so two matches never share a timeline.
    /// </summary>
    [Test]
    public async Task EveryInstanceHasItsOwnArenaState()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var first = await context.CreateInstanceMapAsync(0);
        var second = await context.CreateInstanceMapAsync(0);

        MobaArena.Of(first).Phase = MobaShopTier.T3;
        MobaArena.Of(first).MatchStartUtc = DateTime.UtcNow.AddMinutes(-20);

        Assert.That(MobaArena.Of(second).Phase, Is.EqualTo(MobaShopTier.T1));
        Assert.That(MobaArena.Of(second).Elapsed, Is.EqualTo(TimeSpan.Zero));
        Assert.That(MobaArena.Of(first), Is.SameAs(MobaArena.Of(first)).And.Not.SameAs(MobaArena.Of(second)));
    }

    /// <summary>
    /// Stripping the safezone of one arena leaves the safezone of the others alone.
    /// </summary>
    [Test]
    public async Task SafezoneIsStrippedPerMap()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var first = await context.CreateInstanceMapAsync(0);
        var second = await context.CreateInstanceMapAsync(0);
        first!.Terrain.SafezoneMap[3, 3] = true;
        second!.Terrain.SafezoneMap[3, 3] = true;

        MobaArenaBoot.ClearSafezone(first);

        Assert.That(first.Terrain.SafezoneMap[3, 3], Is.False);
        Assert.That(second.Terrain.SafezoneMap[3, 3], Is.True);
    }

    /// <summary>
    /// Tearing an instance down removes it from the context and resets its state; a registered champion keeps it in use until it leaves.
    /// </summary>
    [Test]
    public async Task TearDownRemovesTheInstance()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var instance = (await context.CreateInstanceMapAsync(0))!;
        var account = Guid.NewGuid();
        MobaMatchRegistry.Enter(account, new MUnique.OpenMU.DataModel.Entities.Character());
        MobaMatchRegistry.TryGetState(account, out var state);
        state!.Arena = instance;
        MobaArena.Of(instance).Phase = MobaShopTier.T2;

        Assert.That(MobaMatchRegistry.UsesArena(instance), Is.True);
        MobaMatchRegistry.Leave(account);
        Assert.That(MobaMatchRegistry.UsesArena(instance), Is.False);

        await MobaArenaBoot.TearDownAsync(instance, context);

        Assert.That((await context.GetMapsAsync()).Contains(instance), Is.False);
        Assert.That(MobaArena.Of(instance).Phase, Is.EqualTo(MobaShopTier.T1));
    }
}
