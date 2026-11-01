// <copyright file="MobaGameModeTests.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Tests;

using MUnique.OpenMU.GameLogic;
using MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Tests the game modes: the registry, the mode of every arena and that the engine calls the mode of the arena.
/// </summary>
[TestFixture]
public class MobaGameModeTests
{
    /// <summary>
    /// A new arena plays the classic mode.
    /// </summary>
    [Test]
    public async Task ArenaStartsWithTheClassicMode()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var instance = (await context.CreateInstanceMapAsync(0))!;

        Assert.That(MobaArena.Of(instance).Mode, Is.SameAs(MobaGameModes.Classic));
        Assert.That(MobaGameModes.Classic.Id, Is.EqualTo("classic"));
    }

    /// <summary>
    /// Modes are found by identifier whatever the case; the identifiers are unique.
    /// </summary>
    [Test]
    public void RegistryFindsModesById()
    {
        Assert.That(MobaGameModes.Find("classic"), Is.SameAs(MobaGameModes.Classic));
        Assert.That(MobaGameModes.Find("  CLASSIC "), Is.SameAs(MobaGameModes.Classic));
        Assert.That(MobaGameModes.Find("nope"), Is.Null);
        Assert.That(MobaGameModes.Find(null), Is.Null);
        Assert.That(MobaGameModes.Find(" "), Is.Null);
        Assert.That(MobaGameModes.All.Select(m => m.Id).Distinct().Count(), Is.EqualTo(MobaGameModes.All.Count));
        Assert.That(MobaGameModes.All.All(m => m.Id == m.Id.ToLowerInvariant() && !string.IsNullOrWhiteSpace(m.DisplayName)), Is.True);
    }

    /// <summary>
    /// The mode belongs to the arena: changing one leaves the others alone.
    /// </summary>
    [Test]
    public async Task EveryArenaHasItsOwnMode()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var first = (await context.CreateInstanceMapAsync(0))!;
        var second = (await context.CreateInstanceMapAsync(0))!;
        var recorder = new RecordingMode();

        MobaArena.Of(first).Mode = recorder;

        Assert.That(MobaArena.Of(first).Mode, Is.SameAs(recorder));
        Assert.That(MobaArena.Of(second).Mode, Is.SameAs(MobaGameModes.Classic));
    }

    /// <summary>
    /// Opening an arena asks its mode to prepare it and to start the ambient systems; taking it down asks it to stop
    /// and gives the next match the classic mode again.
    /// </summary>
    [Test]
    public async Task EngineCallsTheModeOfTheArena()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var instance = (await context.CreateInstanceMapAsync(0))!;
        var recorder = new RecordingMode();
        MobaArena.Of(instance).Mode = recorder;

        await MobaArenaBoot.EnsureAsync(instance, context);

        Assert.That(recorder.Calls, Is.EqualTo(new[] { "prepare", "ambient" }));

        await MobaArenaBoot.TearDownAsync(instance, context);

        Assert.That(recorder.Calls, Is.EqualTo(new[] { "prepare", "ambient", "stop" }));
        Assert.That(MobaArena.Of(instance).Mode, Is.SameAs(MobaGameModes.Classic));
    }

    /// <summary>
    /// During the countdown the ambient systems wait: only the preparation is done.
    /// </summary>
    [Test]
    public async Task AmbientWaitsForTheCountdown()
    {
        var context = GameContextTestHelper.CreateGameContext();
        var instance = (await context.CreateInstanceMapAsync(0))!;
        var recorder = new RecordingMode();
        MobaArena.Of(instance).Mode = recorder;
        MobaArena.Of(instance).PreMatchUntilUtc = DateTime.UtcNow.AddMinutes(1);

        await MobaArenaBoot.EnsureAsync(instance, context);

        Assert.That(recorder.Calls, Is.EqualTo(new[] { "prepare" }));
        MobaArena.Of(instance).PreMatchUntilUtc = DateTime.MinValue;
    }

    private sealed class RecordingMode : MobaGameModeBase
    {
        public List<string> Calls { get; } = [];

        public override string Id => "recording";

        public override string DisplayName => "Recording";

        public override ValueTask PrepareAsync(GameMap map, IGameContext gameContext)
        {
            this.Calls.Add("prepare");
            return ValueTask.CompletedTask;
        }

        public override ValueTask StartAmbientAsync(GameMap map, IGameContext gameContext)
        {
            this.Calls.Add("ambient");
            return ValueTask.CompletedTask;
        }

        public override ValueTask StopAsync(GameMap map, IGameContext gameContext)
        {
            this.Calls.Add("stop");
            return ValueTask.CompletedTask;
        }
    }
}
