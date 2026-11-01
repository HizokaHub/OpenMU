// <copyright file="MobaCommanderTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// Tests the commanders mode: the economy of the board, the bot commander, the packs and the orders which need no spawning.
/// </summary>
[TestFixture]
public class MobaCommanderTests
{
    /// <summary>
    /// The economy only runs once it began, and a team starts with its starting essence.
    /// </summary>
    [Test]
    public void EconomyRunsOnlyAfterItBegan()
    {
        var board = new MobaCommanderBoard();

        board.Tick(10);
        Assert.That(board.Running, Is.False);
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(0));
        Assert.That(board.TrySpend(MobaTeam.Blue, 1), Is.False);

        board.Begin();
        Assert.That(board.Running, Is.True);
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.StartEssence));
        Assert.That(board.Essence(MobaTeam.Red), Is.EqualTo(MobaCommanderBoard.StartEssence));

        board.Tick(10);
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.StartEssence + (10 * MobaCommanderBoard.BaseIncomePerSecond)));
    }

    /// <summary>
    /// A team cannot hold more than the cap, and spending is per team.
    /// </summary>
    [Test]
    public void EssenceIsCappedAndSpentPerTeam()
    {
        var board = new MobaCommanderBoard();
        board.Begin();
        board.Tick(10000);
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.EssenceCap));

        Assert.That(board.TrySpend(MobaTeam.Blue, 600), Is.True);
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.EssenceCap - 600));
        Assert.That(board.Essence(MobaTeam.Red), Is.EqualTo(MobaCommanderBoard.EssenceCap));

        Assert.That(board.TrySpend(MobaTeam.Red, (int)MobaCommanderBoard.EssenceCap + 1), Is.False);
        board.Refund(MobaTeam.Blue, 100000);
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.EssenceCap));
    }

    /// <summary>
    /// The income levels cost more each time, raise the income and stop at the maximum.
    /// </summary>
    [Test]
    public void IncomeLevelsCostMoreAndStopAtTheMaximum()
    {
        var board = new MobaCommanderBoard();
        board.Begin();
        board.Tick(400);

        var costs = new List<int>();
        while (board.NextIncomeCost(MobaTeam.Blue) is { } cost)
        {
            costs.Add(cost);
            Assert.That(board.TryBuyIncome(MobaTeam.Blue), Is.True);
        }

        Assert.That(costs, Is.Ordered.Ascending);
        Assert.That(costs.Count, Is.EqualTo(MobaCommanderBoard.MaxIncomeLevel));
        Assert.That(board.IncomeLevel(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.MaxIncomeLevel));
        Assert.That(board.IncomePerSecond(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.BaseIncomePerSecond + (MobaCommanderBoard.MaxIncomeLevel * MobaCommanderBoard.IncomeStepPerLevel)));
        Assert.That(board.TryBuyIncome(MobaTeam.Blue), Is.False);
        Assert.That(board.IncomeLevel(MobaTeam.Red), Is.EqualTo(0), "the other team is not touched");
    }

    /// <summary>
    /// Only one account commands a team; a commander can give the command up, and the match ending clears it.
    /// </summary>
    [Test]
    public void OneCommanderPerTeam()
    {
        var board = new MobaCommanderBoard();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        Assert.That(board.TryClaim(MobaTeam.Blue, first), Is.True);
        Assert.That(board.TryClaim(MobaTeam.Blue, first), Is.True, "claiming again is harmless");
        Assert.That(board.TryClaim(MobaTeam.Blue, second), Is.False);
        Assert.That(board.TryClaim(MobaTeam.Red, second), Is.True);
        Assert.That(board.TryClaim(MobaTeam.None, second), Is.False);
        Assert.That(board.CommanderOf(MobaTeam.Blue), Is.EqualTo(first));

        Assert.That(board.Release(MobaTeam.Blue, second), Is.False);
        Assert.That(board.Release(MobaTeam.Blue, first), Is.True);
        Assert.That(board.CommanderOf(MobaTeam.Blue), Is.Null);

        board.Begin();
        board.Reset();
        Assert.That(board.CommanderOf(MobaTeam.Red), Is.Null);
        Assert.That(board.Running, Is.False);
    }

    /// <summary>
    /// Starting the economy keeps the command somebody claimed while they waited.
    /// </summary>
    [Test]
    public void BeginKeepsTheCommanders()
    {
        var board = new MobaCommanderBoard();
        var account = Guid.NewGuid();
        board.TryClaim(MobaTeam.Blue, account);

        board.Begin();

        Assert.That(board.CommanderOf(MobaTeam.Blue), Is.EqualTo(account));
    }

    /// <summary>
    /// Early in the match the bot commander buys income first.
    /// </summary>
    [Test]
    public void BotBuysIncomeFirst()
    {
        var board = new MobaCommanderBoard();
        board.Begin();

        var decision = MobaCommanderBot.Decide(board, MobaTeam.Red, TimeSpan.FromSeconds(10), new Random(1), [0, 1, 2]);

        Assert.That(decision.Action, Is.EqualTo(MobaCommanderBotAction.Economy));
    }

    /// <summary>
    /// A bot commander with little essence sends the cheapest pack, with none it waits, and it never picks a closed lane.
    /// </summary>
    [Test]
    public void BotSendsWhatItCanAffordDownOpenLanes()
    {
        var board = new MobaCommanderBoard();
        board.Begin();
        board.TrySpend(MobaTeam.Red, (int)MobaCommanderBoard.StartEssence);
        var late = TimeSpan.FromMinutes(10);

        Assert.That(MobaCommanderBot.Decide(board, MobaTeam.Red, late, new Random(1), [1]).Action, Is.EqualTo(MobaCommanderBotAction.Nothing));

        board.Tick(20); // 120 essence
        var decision = MobaCommanderBot.Decide(board, MobaTeam.Red, late, new Random(1), [MobaLayout.MidLane]);

        Assert.That(decision.Action, Is.EqualTo(MobaCommanderBotAction.Send));
        Assert.That(decision.Unit, Is.SameAs(MobaCommanderUnits.Soldier));
        Assert.That(decision.Lane, Is.EqualTo(MobaLayout.MidLane));
        Assert.That(MobaCommanderBot.Decide(board, MobaTeam.Red, late, new Random(1), []).Action, Is.EqualTo(MobaCommanderBotAction.Nothing));
    }

    /// <summary>
    /// The bot never spends more than the team has, whatever it rolls.
    /// </summary>
    [Test]
    public void BotNeverOverspends()
    {
        var board = new MobaCommanderBoard();
        board.Begin();
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            board.Tick(1);
            var decision = MobaCommanderBot.Decide(board, MobaTeam.Blue, TimeSpan.FromSeconds(i), random, [0, 1, 2]);
            if (decision.Action == MobaCommanderBotAction.Economy)
            {
                Assert.That(board.TryBuyIncome(MobaTeam.Blue), Is.True);
            }
            else if (decision.Action == MobaCommanderBotAction.Send)
            {
                Assert.That(board.TrySpend(MobaTeam.Blue, decision.Unit!.Cost), Is.True);
            }

            Assert.That(board.Essence(MobaTeam.Blue), Is.GreaterThanOrEqualTo(0));
        }
    }

    /// <summary>
    /// The packs are found by id, name or first letter; the dearer ones are stronger; each team gets ranks of its own.
    /// </summary>
    [Test]
    public void PacksAreFoundAndBuiltPerTeam()
    {
        Assert.That(MobaCommanderUnits.Find("soldado"), Is.SameAs(MobaCommanderUnits.Soldier));
        Assert.That(MobaCommanderUnits.Find(" B "), Is.SameAs(MobaCommanderUnits.Brute));
        Assert.That(MobaCommanderUnits.Find("Élite"), Is.SameAs(MobaCommanderUnits.Elite));
        Assert.That(MobaCommanderUnits.Find("dragon"), Is.Null);
        Assert.That(MobaCommanderUnits.Find(null), Is.Null);
        Assert.That(MobaCommanderUnits.All.Select(u => u.Cost), Is.Ordered.Ascending);

        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            foreach (var unit in MobaCommanderUnits.All)
            {
                var ranks = unit.Build(team);
                Assert.That(ranks, Is.Not.Empty);
                Assert.That(ranks.All(r => r.Count > 0), Is.True);
            }
        }

        Assert.That(MobaCommanderUnits.Soldier.Build(MobaTeam.Blue).Select(r => r.Number), Is.Not.EqualTo(MobaCommanderUnits.Soldier.Build(MobaTeam.Red).Select(r => r.Number)));
        Assert.That(MobaCommanderUnits.Elite.Build(MobaTeam.Blue).Single().Super, Is.True);
        Assert.That(MobaCommanderUnits.Brute.Build(MobaTeam.Blue).All(r => !r.Super && r.HealthPower > 1f), Is.True);
    }

    /// <summary>
    /// The lanes are read in English and in Spanish.
    /// </summary>
    [Test]
    public void LanesAreParsed()
    {
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane("top"), Is.EqualTo(0));
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane("ARRIBA"), Is.EqualTo(0));
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane("mid"), Is.EqualTo(MobaLayout.MidLane));
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane("centro"), Is.EqualTo(MobaLayout.MidLane));
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane("bot"), Is.EqualTo(2));
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane("abajo"), Is.EqualTo(2));
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane("izquierda"), Is.Null);
        Assert.That(MobaCommanderChatCommandPlugIn.ParseLane(null), Is.Null);
    }

    /// <summary>
    /// The commanders mode is registered and has an identifier of its own.
    /// </summary>
    [Test]
    public void CommandersModeIsRegistered()
    {
        Assert.That(MobaGameModes.Find("commanders"), Is.SameAs(MobaGameModes.Commanders));
        Assert.That(MobaGameModes.Commanders, Is.Not.SameAs(MobaGameModes.Classic));
        Assert.That(MobaGameModes.Commanders.Id, Is.EqualTo(MobaCommandersMode.ModeId));
    }

    /// <summary>
    /// An order is refused before anything is spawned when the match does not run, the lane is closed or the essence is short.
    /// </summary>
    [Test]
    public async Task OrdersAreRefusedBeforeSpawning()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var map = (await context.CreateInstanceMapAsync(0))!;
        var arena = MobaArena.Of(map);
        var board = MobaCommanderBoard.Of(arena);

        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, 1), Is.EqualTo(MobaCommanderSendResult.NotRunning));

        board.Begin();
        arena.Format = MobaFormat.Duel;
        Assert.That(MobaCommanderOrders.OpenLanes(arena), Is.EqualTo(new[] { MobaLayout.MidLane }));
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Soldier, 0), Is.EqualTo(MobaCommanderSendResult.LaneClosed));
        Assert.That(await MobaCommanderOrders.SendAsync(map, context, MobaTeam.Blue, MobaCommanderUnits.Elite, MobaLayout.MidLane), Is.EqualTo(MobaCommanderSendResult.NotEnoughEssence));
        Assert.That(board.Essence(MobaTeam.Blue), Is.EqualTo(MobaCommanderBoard.StartEssence), "nothing was charged");

        arena.Format = MobaFormat.Classic;
        Assert.That(MobaCommanderOrders.OpenLanes(arena).Count, Is.EqualTo(MobaLayout.LaneCount));
    }

    /// <summary>
    /// The commanders mode starts and stops the economy of its arena; stopping forgets the commanders.
    /// </summary>
    [Test]
    public async Task ModeStartsAndStopsTheEconomy()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var map = (await context.CreateInstanceMapAsync(0))!;
        var board = MobaCommanderBoard.Of(MobaArena.Of(map));
        board.TryClaim(MobaTeam.Blue, Guid.NewGuid());

        await MobaGameModes.Commanders.StartMatchAsync(map, context);
        Assert.That(board.Running, Is.True);
        Assert.That(board.CommanderOf(MobaTeam.Blue), Is.Not.Null);

        await MobaGameModes.Commanders.StopAsync(map, context);
        Assert.That(board.Running, Is.False);
        Assert.That(board.CommanderOf(MobaTeam.Blue), Is.Null);
    }

    /// <summary>
    /// A player who is not in a running commanders match gets a hidden panel, and the hidden state carries nothing.
    /// </summary>
    [Test]
    public async Task PanelIsHiddenOutsideACommandersMatch()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var player = await PlayerTestHelper.CreatePlayerAsync(context);

        Assert.That(MobaCommanderPanel.StateOf(player).Visible, Is.False);
        Assert.That(MobaCommanderPanelState.Hidden.Visible, Is.False);
        Assert.That(MobaCommanderPanelState.Hidden.UnitCosts, Is.Empty);
    }
}
