// <copyright file="MobaCommanderQueueTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using Moq;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.GameServer.RemoteView.World;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.Persistence;
using BasicMonsterAttribute = MUnique.OpenMU.Persistence.BasicModel.MonsterAttribute;
using BasicMonsterDefinition = MUnique.OpenMU.Persistence.BasicModel.MonsterDefinition;

/// <summary>
/// Tests the waiting zone of the commanders mode (the Direct Strike way of playing): the wave timer, the held creeps, "send now",
/// the packet of the panel and the parked body of the human commander.
/// </summary>
[TestFixture]
public class MobaCommanderQueueTests
{
    /// <summary>
    /// The wave timer is common to both teams: it runs out every interval and starts again by itself; it stands still while the economy does not run.
    /// </summary>
    [Test]
    public void WaveTimerRunsOutEveryIntervalAndStartsAgain()
    {
        var queue = new MobaCommanderQueue();
        Assert.That(queue.Advance(5), Is.False, "no timer before the economy runs");
        Assert.That(queue.WaveSeconds, Is.EqualTo(0));

        queue.Begin();
        Assert.That(queue.WaveSeconds, Is.EqualTo(MobaCommanderQueue.WaveIntervalSeconds));
        for (var second = 1; second < MobaCommanderQueue.WaveIntervalSeconds; second++)
        {
            Assert.That(queue.Advance(1), Is.False, $"second {second}");
        }

        Assert.That(queue.WaveSeconds, Is.EqualTo(1));
        Assert.That(queue.Advance(1), Is.True, "the wave is due");
        Assert.That(queue.WaveSeconds, Is.EqualTo(MobaCommanderQueue.WaveIntervalSeconds), "the timer starts again");

        queue.Reset();
        Assert.That(queue.WaveSeconds, Is.EqualTo(0));
        Assert.That(queue.Advance(100), Is.False);
    }

    /// <summary>
    /// The cooldown of "send now" runs per team, goes down with the timer and can only be started when it is ready.
    /// </summary>
    [Test]
    public void SendNowCooldownRunsPerTeam()
    {
        var queue = new MobaCommanderQueue();
        queue.Begin();

        Assert.That(queue.TryStartSendNowCooldown(MobaTeam.Blue), Is.True);
        Assert.That(queue.SendNowCooldown(MobaTeam.Blue), Is.EqualTo(MobaCommanderQueue.SendNowCooldownSeconds));
        Assert.That(queue.SendNowCooldown(MobaTeam.Red), Is.EqualTo(0));
        Assert.That(queue.TryStartSendNowCooldown(MobaTeam.Blue), Is.False);

        queue.Advance(MobaCommanderQueue.SendNowCooldownSeconds - 1);
        Assert.That(queue.SendNowCooldown(MobaTeam.Blue), Is.EqualTo(1));
        queue.Advance(1);
        Assert.That(queue.SendNowCooldown(MobaTeam.Blue), Is.EqualTo(0));
        Assert.That(queue.TryStartSendNowCooldown(MobaTeam.Blue), Is.True);
    }

    /// <summary>
    /// The waiting zone of every lane has room for the most packs, on walkable tiles clear of the structures, inside the base of the
    /// team; no two creeps share a tile and no two packs overlap; the creeps only walk the waypoints which are still ahead of them.
    /// </summary>
    [Test]
    public void WaitingZoneFitsEveryPackInTheBase()
    {
        var footprints = MobaLayout.Towers.Select(t => t.Position)
            .Concat(MobaLayout.Inhibitors.Select(i => i.Position))
            .Concat([MobaLayout.NexusOf(MobaTeam.Blue), MobaLayout.NexusOf(MobaTeam.Red)])
            .ToList();

        bool Clear(int x, int y) => MobaLayout.IsWalkable(x, y) && footprints.All(f => Math.Abs(f.X - x) > 1 || Math.Abs(f.Y - y) > 1);

        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            var nexus = MobaLayout.NexusOf(team);
            for (var lane = 0; lane < MobaLayout.LaneCount; lane++)
            {
                var occupied = new HashSet<(int, int)>();
                var path = MobaLayout.WaypointsFor(team, lane);
                for (var slot = 0; slot < MobaCommanderQueue.MaxPacksPerLane; slot++)
                {
                    foreach (var unit in MobaCommanderUnits.All)
                    {
                        var composition = unit.Build(team);
                        var spots = MobaCommanderQueue.WaitingSpots(team, lane, slot, composition, (x, y) => Clear(x, y) && !occupied.Contains((x, y)));
                        var where = $"{team} lane {lane} slot {slot} {unit.Id}";
                        Assert.That(spots.Count, Is.EqualTo(composition.Sum(c => c.Count)), where);
                        Assert.That(spots.Select(p => (p.X, p.Y)).Distinct().Count(), Is.EqualTo(spots.Count), $"{where}: no shared tiles");
                        foreach (var spot in spots)
                        {
                            Assert.That(Clear(spot.X, spot.Y), Is.True, $"{where}: ({spot.X},{spot.Y}) is walkable and clear");
                            Assert.That(Math.Sqrt(Math.Pow(spot.X - nexus.X, 2) + Math.Pow(spot.Y - nexus.Y, 2)), Is.LessThanOrEqualTo(40), $"{where}: inside the base");
                            Assert.That(MobaCommanderQueue.FirstWaypointAhead(path, spot), Is.InRange(1, path.Count - 1), where);
                        }
                    }

                    // The packs fill the zone one after the other: the soldiers (the widest pack) of each slot must not overlap an earlier pack.
                    var soldiers = MobaCommanderQueue.WaitingSpots(team, lane, slot, MobaCommanderUnits.Soldier.Build(team), (x, y) => Clear(x, y) && !occupied.Contains((x, y)));
                    foreach (var spot in soldiers)
                    {
                        Assert.That(occupied.Add((spot.X, spot.Y)), Is.True, $"{team} lane {lane}: slot {slot} does not overlap an earlier pack");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Bought packs appear held in the base and are counted from the living held creeps; the wave timer releases both teams together
    /// and "send now" only the team of the commander, with a cooldown which a refused order does not spend.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task PacksWaitHeldUntilTheWaveOrSendNow()
    {
        var (context, map, arena) = await CreateArenaWithCreepsAsync();
        var board = MobaCommanderBoard.Of(arena);
        var queue = MobaCommanderQueue.Of(arena);
        board.Begin();
        queue.Begin();
        board.Refund(MobaTeam.Blue, 3000);
        board.Refund(MobaTeam.Red, 3000);

        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, MobaLayout.MidLane), Is.EqualTo(MobaCommanderSendResult.Sent));
        var firstSoldiers = Creeps(map, MobaTeam.Blue);
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, MobaLayout.MidLane), Is.EqualTo(MobaCommanderSendResult.Sent));
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Elite, 0), Is.EqualTo(MobaCommanderSendResult.Sent));
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Red, MobaCommanderUnits.Brute, 2), Is.EqualTo(MobaCommanderSendResult.Sent));

        var blueCreeps = Creeps(map, MobaTeam.Blue);
        var redCreeps = Creeps(map, MobaTeam.Red);
        Assert.That(firstSoldiers.Count, Is.EqualTo(4));
        Assert.That(blueCreeps.Count, Is.EqualTo(4 + 4 + 1));
        Assert.That(redCreeps.Count, Is.EqualTo(2));
        Assert.That(blueCreeps.Concat(redCreeps).All(MobaCreepHold.IsHeld), Is.True, "everything waits held");
        Assert.That(blueCreeps.Concat(redCreeps).Select(c => (c.Position.X, c.Position.Y)).Distinct().Count(), Is.EqualTo(blueCreeps.Count + redCreeps.Count), "nobody shares a tile");

        // What waits is read from the creeps (lane-major): two soldier packs mid, one elite pack top, a brute pack bot for the other team.
        Assert.That(queue.Queued(MobaTeam.Blue, 3), Is.EqualTo(new[] { 0, 0, 1, 2, 0, 0, 0, 0, 0 }));
        Assert.That(queue.Queued(MobaTeam.Red, 3), Is.EqualTo(new[] { 0, 0, 0, 0, 0, 0, 0, 1, 0 }));

        // A tick before the wave keeps the hold and the timer runs.
        await MobaCommanderOrders.StepAsync(map, context, 1);
        Assert.That(queue.WaveSeconds, Is.EqualTo(MobaCommanderQueue.WaveIntervalSeconds - 1));
        Assert.That(blueCreeps.Concat(redCreeps).All(MobaCreepHold.IsHeld), Is.True);

        // Creeps which die stop counting; a pack counts while one of its creeps lives.
        foreach (var creep in firstSoldiers.Take(3))
        {
            creep.Dispose();
        }

        Assert.That(queue.Queued(MobaTeam.Blue, 3)[(MobaLayout.MidLane * 3) + 0], Is.EqualTo(2), "a pack with one survivor still waits");
        firstSoldiers.Last().Dispose();
        Assert.That(queue.Queued(MobaTeam.Blue, 3)[(MobaLayout.MidLane * 3) + 0], Is.EqualTo(1), "a pack with no creep alive is gone");

        // "Send now" lets only the team of the commander go and has a cooldown.
        var wave = queue.WaveSeconds;
        Assert.That(MobaCommanderOrders.SendNow(map, MobaTeam.Blue), Is.EqualTo(MobaCommanderSendNowResult.Released));
        Assert.That(queue.WaveSeconds, Is.EqualTo(wave), "the wave timer is not restarted");
        Assert.That(Creeps(map, MobaTeam.Blue).Any(MobaCreepHold.IsHeld), Is.False);
        Assert.That(redCreeps.Where(c => c.IsAlive).All(MobaCreepHold.IsHeld), Is.True, "the other team keeps waiting");

        Assert.That(MobaCommanderOrders.SendNow(map, MobaTeam.Blue), Is.EqualTo(MobaCommanderSendNowResult.NothingWaiting));
        Assert.That(queue.SendNowCooldown(MobaTeam.Blue), Is.EqualTo(MobaCommanderQueue.SendNowCooldownSeconds), "still the cooldown of the first order");
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, 0), Is.EqualTo(MobaCommanderSendResult.Sent));
        Assert.That(MobaCommanderOrders.SendNow(map, MobaTeam.Blue), Is.EqualTo(MobaCommanderSendNowResult.Cooldown));

        queue.Advance(MobaCommanderQueue.SendNowCooldownSeconds);
        Assert.That(MobaCommanderOrders.SendNow(map, MobaTeam.Blue), Is.EqualTo(MobaCommanderSendNowResult.Released));

        // An order with nothing waiting does not spend the cooldown.
        Assert.That(MobaCommanderOrders.SendNow(map, MobaTeam.Red), Is.EqualTo(MobaCommanderSendNowResult.Released));
        queue.Advance(MobaCommanderQueue.SendNowCooldownSeconds);
        Assert.That(MobaCommanderOrders.SendNow(map, MobaTeam.Red), Is.EqualTo(MobaCommanderSendNowResult.NothingWaiting));
        Assert.That(queue.SendNowCooldown(MobaTeam.Red), Is.EqualTo(0));

        // The wave timer releases whatever waits, for both teams.
        await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, 1);
        await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Red, MobaCommanderUnits.Soldier, 1);
        var waiting = Creeps(map, MobaTeam.Blue).Concat(Creeps(map, MobaTeam.Red)).Where(MobaCreepHold.IsHeld).ToList();
        Assert.That(waiting.Count, Is.EqualTo(8));
        queue.Advance(queue.WaveSeconds - 1);
        await MobaCommanderOrders.StepAsync(map, context, 1);
        Assert.That(waiting.Any(MobaCreepHold.IsHeld), Is.False, "the wave released both teams");
        Assert.That(queue.WaveSeconds, Is.EqualTo(MobaCommanderQueue.WaveIntervalSeconds));
        Assert.That(queue.PacksWaiting(MobaTeam.Blue) + queue.PacksWaiting(MobaTeam.Red), Is.EqualTo(0));

        await MobaWaveSpawner.DespawnAllCreepsAsync(map);
        MobaCommanderOrders.Stop(map);
    }

    /// <summary>
    /// A lane takes the most packs per team; the next order is refused without charging, and the other lanes and the other team are still open.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task WaitingZoneHasATopPerLane()
    {
        var (context, map, arena) = await CreateArenaWithCreepsAsync();
        var board = MobaCommanderBoard.Of(arena);
        board.Begin();
        MobaCommanderQueue.Of(arena).Begin();
        board.Refund(MobaTeam.Blue, 3000);
        board.Refund(MobaTeam.Red, 3000);

        for (var i = 0; i < MobaCommanderQueue.MaxPacksPerLane; i++)
        {
            Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Brute, MobaLayout.MidLane), Is.EqualTo(MobaCommanderSendResult.Sent), $"pack {i}");
        }

        var essence = board.Essence(MobaTeam.Blue);
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, MobaLayout.MidLane), Is.EqualTo(MobaCommanderSendResult.QueueFull));
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(essence), "nothing was charged");
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, 0), Is.EqualTo(MobaCommanderSendResult.Sent), "the other lanes are open");
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Red, MobaCommanderUnits.Soldier, MobaLayout.MidLane), Is.EqualTo(MobaCommanderSendResult.Sent), "the other team has its own zone");

        await MobaWaveSpawner.DespawnAllCreepsAsync(map);
        MobaCommanderOrders.Stop(map);
    }

    /// <summary>
    /// The packet of the panel carries the wave timer, the cooldown and the waiting packs after the creeps of the lanes, with the agreed length.
    /// </summary>
    [Test]
    public void PacketCarriesTheWaitingZone()
    {
        var state = new MobaCommanderPanelState(
            true, true, false, 1234, 6, 1, 5, 250, 7, [100, 250, 600], [1, 2, 3], [4, 5, 6], 23, 48, 5, [0, 1, 2, 3, 4, 5, 6, 7, 8]);

        Assert.That(MobaCommanderPlugIn.PacketLength(state), Is.EqualTo(14 + (2 * 3) + 6 + 3 + (3 * 3)));
        var buffer = new byte[MobaCommanderPlugIn.PacketLength(state)];
        MobaCommanderPlugIn.WritePacket(buffer, state);

        Assert.That(buffer[..4], Is.EqualTo(new byte[] { 0xC1, (byte)buffer.Length, 0xD5, 0x40 }));
        Assert.That(buffer[13], Is.EqualTo(3), "unit count");
        Assert.That(buffer[20..26], Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6 }), "own and enemy creeps");
        Assert.That(buffer[26], Is.EqualTo(23), "seconds to the next wave");
        Assert.That(buffer[27], Is.EqualTo(48), "interval of the wave");
        Assert.That(buffer[28], Is.EqualTo(5), "cooldown of send now");
        Assert.That(buffer[29..], Is.EqualTo(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }), "queued, lane-major");

        var hidden = MobaCommanderPanelState.Hidden;
        Assert.That(MobaCommanderPlugIn.PacketLength(hidden), Is.EqualTo(14 + 6 + 3));
        Assert.That(hidden.Queued, Is.Empty);
    }

    /// <summary>
    /// The panel shows the timer and the queue to every player of the match, an action the server does not know is ignored, and only
    /// the commander can use "send now".
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task PanelShowsTheTimerAndIgnoresUnknownActions()
    {
        var (context, map, arena) = await CreateArenaWithCreepsAsync();
        arena.Mode = MobaGameModes.Commanders;
        var board = MobaCommanderBoard.Of(arena);
        var queue = MobaCommanderQueue.Of(arena);
        board.Begin();
        queue.Begin();
        board.Refund(MobaTeam.Blue, 3000);
        var player = await CreateChampionAsync(context, map, MobaTeam.Blue);

        var state = MobaCommanderPanel.StateOf(player);
        Assert.That(state.Visible, Is.True);
        Assert.That(state.WaveSeconds, Is.EqualTo(MobaCommanderQueue.WaveIntervalSeconds));
        Assert.That(state.WaveInterval, Is.EqualTo(MobaCommanderQueue.WaveIntervalSeconds));
        Assert.That(state.SendNowCooldown, Is.EqualTo(0));
        Assert.That(state.Queued, Is.EqualTo(new int[9]));

        // Not the commander: "send now" is refused.
        await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, MobaLayout.MidLane);
        await MobaCommanderPanel.HandleOrderAsync(player, MobaCommanderPanelAction.SendNow, 0, 0);
        Assert.That(Creeps(map, MobaTeam.Blue).All(MobaCreepHold.IsHeld), Is.True, "a player who is not the commander cannot release");
        Assert.That(MobaCommanderPanel.StateOf(player).Queued[(MobaLayout.MidLane * 3) + 0], Is.EqualTo(1));

        // An unknown action changes nothing and does not break.
        board.TryClaim(MobaTeam.Blue, player.Account!.GetId());
        var essence = board.Essence(MobaTeam.Blue);
        await MobaCommanderPanel.HandleOrderAsync(player, (MobaCommanderPanelAction)99, 1, 1);
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(essence));
        Assert.That(Creeps(map, MobaTeam.Blue).All(MobaCreepHold.IsHeld), Is.True);

        // The commander releases his own team.
        await MobaCommanderPanel.HandleOrderAsync(player, MobaCommanderPanelAction.SendNow, 0, 0);
        Assert.That(Creeps(map, MobaTeam.Blue).Any(MobaCreepHold.IsHeld), Is.False);
        Assert.That(MobaCommanderPanel.StateOf(player).SendNowCooldown, Is.EqualTo(MobaCommanderQueue.SendNowCooldownSeconds));

        await MobaWaveSpawner.DespawnAllCreepsAsync(map);
        MobaCommanderOrders.Stop(map);
    }

    /// <summary>
    /// The body of the human commander is parked while he commands (moved to his base, not a target, exempt from the AFK) and restored
    /// when he gives the command up; a stale flag after the match is harmless.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task CommanderBodyIsParkedAndRestored()
    {
        var (context, map, arena) = await CreateArenaWithCreepsAsync();
        arena.Mode = MobaGameModes.Commanders;
        var board = MobaCommanderBoard.Of(arena);
        board.Begin();
        MobaCommanderQueue.Of(arena).Begin();
        var player = await CreateChampionAsync(context, map, MobaTeam.Blue);

        Assert.That(MobaCommanderBody.IsParked(player), Is.False);
        Assert.That(player.IsActive(), Is.True);

        // Nobody commands: nothing is parked.
        await MobaCommanderBody.SyncAsync(player);
        Assert.That(MobaCommanderBody.IsParked(player), Is.False);

        board.TryClaim(MobaTeam.Blue, player.Account!.GetId());
        await MobaCommanderBody.SyncAsync(player);
        Assert.That(MobaCommanderBody.IsParked(player), Is.True);
        Assert.That(player.IsActive(), Is.False, "creeps, turrets and summons look for targets among the active ones");
        var shop = MobaLayout.ShopOf(MobaTeam.Blue);
        Assert.That(Math.Abs(player.Position.X - shop.X), Is.LessThanOrEqualTo(4), "moved to its base");
        Assert.That(Math.Abs(player.Position.Y - shop.Y), Is.LessThanOrEqualTo(4), "moved to its base");

        var attacker = new Mock<IAttacker>();
        Assert.That(await player.AttackByAsync(attacker.Object, null, false), Is.Null, "a parked body cannot be hurt");

        // Giving the command up restores the body at once.
        board.Release(MobaTeam.Blue, player.Account!.GetId());
        await MobaCommanderBody.SyncAsync(player);
        Assert.That(MobaCommanderBody.IsParked(player), Is.False);
        Assert.That(player.IsActive(), Is.True);
        Assert.That(DateTime.UtcNow - player.MobaState.LastInputUtc, Is.LessThan(TimeSpan.FromSeconds(5)), "the idle clock restarted");

        // A stale flag after the match ended leaves nobody untouchable.
        board.TryClaim(MobaTeam.Blue, player.Account!.GetId());
        await MobaCommanderBody.SyncAsync(player);
        Assert.That(MobaCommanderBody.IsParked(player), Is.True);
        MobaCommanderOrders.Stop(map);
        Assert.That(MobaCommanderBody.IsParked(player), Is.False, "the flag only counts while the economy runs");
        await MobaCommanderBody.RestoreAsync(player);
        Assert.That(player.MobaState.Parked, Is.False);
    }

    /// <summary>
    /// A pack which is released while it is still being spawned (the wave comes in the middle of a purchase) lets its late creeps go
    /// too: none of them is left held or uncounted, and the place of the pack is free again once its spawn ends.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task ReleaseDuringSpawnLetsTheLateCreepsGo()
    {
        var (context, map, arena) = await CreateArenaWithCreepsAsync();
        var queue = MobaCommanderQueue.Of(arena);
        queue.Begin();
        var pack = queue.TryReservePack(MobaTeam.Blue, MobaLayout.MidLane, 0)!;
        var registered = 0;
        await MobaWaveSpawner.SpawnUnitsAsync(
            map,
            context,
            MobaTeam.Blue,
            MobaLayout.MidLane,
            MobaCommanderUnits.Soldier.Build(MobaTeam.Blue),
            pack.Slot,
            creep =>
            {
                queue.Register(pack, creep);
                if (++registered == 2)
                {
                    queue.Release();
                }
            });
        queue.Complete(pack);

        var creeps = Creeps(map, MobaTeam.Blue);
        Assert.That(creeps.Count, Is.EqualTo(4));
        Assert.That(creeps.Any(MobaCreepHold.IsHeld), Is.False, "the late creeps joined the wave");
        Assert.That(queue.PacksWaiting(MobaTeam.Blue), Is.EqualTo(0));
        Assert.That(queue.Queued(MobaTeam.Blue, 3), Is.EqualTo(new int[9]));
        Assert.That(queue.TryReservePack(MobaTeam.Blue, MobaLayout.MidLane, 0)!.Slot, Is.EqualTo(0), "the place is free again");

        await MobaWaveSpawner.DespawnAllCreepsAsync(map);
        queue.Reset();
    }

    /// <summary>
    /// Starting the economy again without stopping it lets go of what waited before, and a creep whose hold ran out and which already
    /// walked away is not caught again in mid-lane by the renewal.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task BeginReleasesOldPacksAndRenewDoesNotCatchWalkers()
    {
        var (context, map, arena) = await CreateArenaWithCreepsAsync();
        var board = MobaCommanderBoard.Of(arena);
        var queue = MobaCommanderQueue.Of(arena);
        board.Begin();
        queue.Begin();
        board.Refund(MobaTeam.Blue, 3000);
        await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, MobaLayout.MidLane);
        var creeps = Creeps(map, MobaTeam.Blue);
        Assert.That(creeps.All(MobaCreepHold.IsHeld), Is.True);

        // One creep lost its hold and walked off: the renewal leaves it alone and it stops counting as waiting.
        var walker = creeps[0];
        MobaCreepHold.Release(walker);
        walker.Position = new Point((byte)(walker.Position.X + 10), (byte)(walker.Position.Y + 10));
        queue.Renew();
        Assert.That(MobaCreepHold.IsHeld(walker), Is.False);
        Assert.That(creeps.Skip(1).All(MobaCreepHold.IsHeld), Is.True);
        Assert.That(queue.PacksWaiting(MobaTeam.Blue), Is.EqualTo(1), "the rest of the pack still waits");

        // A second start releases whatever waited.
        queue.Begin();
        Assert.That(creeps.Any(MobaCreepHold.IsHeld), Is.False);
        Assert.That(queue.PacksWaiting(MobaTeam.Blue), Is.EqualTo(0));

        await MobaWaveSpawner.DespawnAllCreepsAsync(map);
        MobaCommanderOrders.Stop(map);
    }

    /// <summary>
    /// A creep which finds no free tile near its place in the zone gets no tile at all (it is never put on one it could not leave).
    /// </summary>
    [Test]
    public void WaitingSpotsNeverUseABlockedTile()
    {
        var composition = MobaCommanderUnits.Soldier.Build(MobaTeam.Blue);
        var plan = MobaCommanderQueue.PlanWaitingSpots(MobaTeam.Blue, MobaLayout.MidLane, 0, composition, (_, _) => false);
        Assert.That(plan.Count, Is.EqualTo(composition.Sum(c => c.Count)));
        Assert.That(plan.All(p => p is null), Is.True);
        Assert.That(MobaCommanderQueue.WaitingSpots(MobaTeam.Blue, MobaLayout.MidLane, 0, composition, (_, _) => false), Is.Empty);
    }

    /// <summary>
    /// A new session of the commander (a reconnect, which finds the flag of the old session still up) is parked again: moved to the base
    /// and flagged for its own session.
    /// </summary>
    /// <returns>A task.</returns>
    [Test]
    public async Task ReconnectedCommanderIsParkedAgain()
    {
        var (context, map, arena) = await CreateArenaWithCreepsAsync();
        arena.Mode = MobaGameModes.Commanders;
        var board = MobaCommanderBoard.Of(arena);
        board.Begin();
        MobaCommanderQueue.Of(arena).Begin();
        var player = await CreateChampionAsync(context, map, MobaTeam.Blue);
        var other = await CreateChampionAsync(context, map, MobaTeam.Blue);
        board.TryClaim(MobaTeam.Blue, player.Account!.GetId());

        // The state remembers a flag from another session.
        player.MobaState.Parked = true;
        player.MobaState.ParkedSession = other;
        player.Position = new Point(120, 120);
        await MobaCommanderBody.SyncAsync(player);

        Assert.That(player.MobaState.Parked, Is.True);
        Assert.That(player.MobaState.ParkedSession, Is.SameAs(player));
        var shop = MobaLayout.ShopOf(MobaTeam.Blue);
        Assert.That(Math.Abs(player.Position.X - shop.X), Is.LessThanOrEqualTo(4), "moved to its base again");

        await MobaCommanderBody.RestoreAsync(player);
        MobaCommanderOrders.Stop(map);
    }

    private static async Task<(IGameContext Context, GameMap Map, MobaArena Arena)> CreateArenaWithCreepsAsync()
    {
        var context = GameContextTestHelper.CreateGameContext();
        foreach (var number in new short[] { 3, 24, 26, 418, MobaSuperMinions.BlueModel, MobaSuperMinions.RedModel })
        {
            var definition = new BasicMonsterDefinition { Number = number, ObjectKind = NpcObjectKind.Monster, Designation = "Creep " + number };
            definition.Attributes.Add(new BasicMonsterAttribute { AttributeDefinition = Stats.MaximumHealth, Value = 1000 });
            context.Configuration.Monsters.Add(definition);
        }

        var map = (await context.CreateInstanceMapAsync(0))!;
        return (context, map, MobaArena.Of(map));
    }

    private static async Task<Player> CreateChampionAsync(IGameContext context, GameMap map, MobaTeam team)
    {
        var player = await PlayerTestHelper.CreatePlayerAsync(context);
        player.IsMobaClone = true;
        player.CurrentMap = map;
        player.IsAlive = true;
        MobaTeams.Set(player, team);
        return player;
    }

    private static List<Monster> Creeps(GameMap map, MobaTeam team)
        => map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<Monster>()
            .Where(m => m.IsAlive && MobaTeams.GetTeam(m) == team && !MobaStructures.IsStructure(m))
            .OrderBy(m => m.Id)
            .ToList();
}
