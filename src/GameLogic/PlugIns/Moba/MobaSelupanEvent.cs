// <copyright file="MobaSelupanEvent.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The Selupan event of the red jungle (2026-10-17, positions drawn by the user): seven spider eggs lie around the nest. Every egg that is destroyed hatches
/// 1, 2 or 3 spiders (45 % / 35 % / 20 %) that fight the champion that broke it. A destroyed egg grows back after 5 minutes, but when all seven are
/// destroyed at the same time Selupan appears at the nest (with all his skills, see <see cref="MobaJungleSkills"/>). The eggs come back 5 minutes after
/// Selupan dies.
/// </summary>
public static class MobaSelupanEvent
{
    /// <summary>Seconds a destroyed egg takes to grow back (and the eggs after Selupan died).</summary>
    public const int EggRegenSeconds = 300;

    /// <summary>The positions of the seven eggs, as drawn by the user.</summary>
    public static readonly IReadOnlyList<(int X, int Y)> EggSpots = new[] { (170, 132), (171, 139), (177, 134), (183, 142), (188, 135), (182, 138), (176, 134), (177, 43) };

    /// <summary>Where Selupan appears.</summary>
    public static readonly (int X, int Y) SelupanSpot = (175, 140);

    private static readonly MobaJungle.Kind[] EggKinds =
    {
        MobaJungle.Kind.SpiderEgg1, MobaJungle.Kind.SpiderEgg2, MobaJungle.Kind.SpiderEgg3,
        MobaJungle.Kind.SpiderEgg1, MobaJungle.Kind.SpiderEgg2, MobaJungle.Kind.SpiderEgg3, MobaJungle.Kind.SpiderEgg1, MobaJungle.Kind.SpiderEgg2,
    };

    private static readonly Dictionary<GameMap, EventState> States = new();

    /// <summary>Gets the number of spiders an egg hatches: 1 (45 %), 2 (35 %) or 3 (20 %).</summary>
    /// <param name="roll">A random number in [0, 1).</param>
    /// <returns>The number of spiders.</returns>
    public static int SpidersFor(double roll) => roll < 0.45 ? 1 : roll < 0.80 ? 2 : 3;

    /// <summary>Spawns the seven eggs.</summary>
    /// <param name="map">The map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="delayed">Whether the eggs appear at their first-spawn time instead of at once.</param>
    /// <returns>The number of monsters spawned at once.</returns>
    public static async ValueTask<int> StartAsync(GameMap map, IGameContext gameContext, bool delayed = false)
    {
        await StopAsync(map).ConfigureAwait(false);
        var state = new EventState(CancellationTokenSource.CreateLinkedTokenSource(MobaJungle.TokenOf(map)));
        States[map] = state;
        var wait = delayed ? MobaJungle.Info[MobaJungle.Kind.SpiderEgg1].FirstSpawn : 0;
        if (wait > 0)
        {
            _ = SpawnEggsLaterAsync(map, gameContext, state, wait);
            return 0;
        }

        var spawned = 0;
        for (var i = 0; i < EggSpots.Count; i++)
        {
            spawned += await SpawnEggAsync(map, gameContext, state, i).ConfigureAwait(false) ? 1 : 0;
        }

        return spawned;
    }

    /// <summary>Stops the event (its monsters are removed with the jungle).</summary>
    /// <param name="map">The map.</param>
    /// <returns>A task.</returns>
    public static async ValueTask StopAsync(GameMap map)
    {
        if (States.Remove(map, out var state))
        {
            await state.Cancel.CancelAsync().ConfigureAwait(false);
        }
    }

    private static async Task SpawnEggsLaterAsync(GameMap map, IGameContext gameContext, EventState state, int seconds)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds), state.Cancel.Token).ConfigureAwait(false);
            for (var i = 0; i < EggSpots.Count; i++)
            {
                await SpawnEggAsync(map, gameContext, state, i).ConfigureAwait(false);
            }
        }
        catch
        {
            // the match ended
        }
    }

    private static async ValueTask<bool> SpawnEggAsync(GameMap map, IGameContext gameContext, EventState state, int index)
    {
        var (x, y) = EggSpots[index];
        var (fx, fy) = MobaJungleFormation.FacingAt(x, y);
        var cell = MobaLayout.NearestWalkable(x, y);
        var slot = new MobaJungleFormation.Slot(cell.X, cell.Y, 0, MobaJungleFormation.DirectionOf(fx, fy));
        var egg = await MobaJungle.SpawnMonsterAsync(
            map,
            gameContext,
            EggKinds[index],
            slot,
            new MobaJungleGroup(),
            cell,
            (dead, death) => OnEggDiedAsync(map, gameContext, state, index, dead, death)).ConfigureAwait(false);
        lock (state)
        {
            state.Eggs[index] = egg;
        }

        return egg is not null;
    }

    private static async Task OnEggDiedAsync(GameMap map, IGameContext gameContext, EventState state, int index, Monster dead, DeathInformation death)
    {
        if (state.Cancel.IsCancellationRequested)
        {
            return;
        }

        bool allDestroyed;
        lock (state)
        {
            state.Eggs[index] = null;
            allDestroyed = !state.SelupanActive && state.Eggs.All(e => e is null);
            if (allDestroyed)
            {
                state.SelupanActive = true;
            }
        }

        var killer = map.GetObject(death.KillerId) as IAttackable;
        await HatchAsync(map, gameContext, state, index, killer).ConfigureAwait(false);

        if (allDestroyed)
        {
            await SpawnSelupanAsync(map, gameContext, state, killer).ConfigureAwait(false);
            return;
        }

        // A single destroyed egg grows back by itself (unless Selupan took over).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(EggRegenSeconds), state.Cancel.Token).ConfigureAwait(false);
            bool regrow;
            lock (state)
            {
                regrow = !state.SelupanActive && state.Eggs[index] is null;
            }

            if (regrow)
            {
                await SpawnEggAsync(map, gameContext, state, index).ConfigureAwait(false);
            }
        }
        catch
        {
            // the match ended
        }
    }

    private static async ValueTask HatchAsync(GameMap map, IGameContext gameContext, EventState state, int index, IAttackable? killer)
    {
        var (x, y) = EggSpots[index];
        var count = SpidersFor(Random.Shared.NextDouble());
        var group = new MobaJungleGroup();
        for (var i = 0; i < count; i++)
        {
            var cell = MobaLayout.NearestWalkable(x + Random.Shared.Next(-1, 2), y + Random.Shared.Next(-1, 2));
            var slot = new MobaJungleFormation.Slot(cell.X, cell.Y, 0, MobaJungleFormation.DirectionOf(0, 1));
            var spider = await MobaJungle.SpawnMonsterAsync(map, gameContext, MobaJungle.Kind.Spider, slot, group, cell, null).ConfigureAwait(false);
            if (spider is not null)
            {
                lock (state)
                {
                    state.Spiders.Add(spider);
                }
            }
        }

        if (killer is Player { IsMobaClone: true })
        {
            group.Aggro(killer);
        }
    }

    private static async ValueTask SpawnSelupanAsync(GameMap map, IGameContext gameContext, EventState state, IAttackable? killer)
    {
        var cell = MobaLayout.NearestWalkable(SelupanSpot.X, SelupanSpot.Y);
        var (fx, fy) = MobaJungleFormation.FacingAt(SelupanSpot.X, SelupanSpot.Y);
        var slot = new MobaJungleFormation.Slot(cell.X, cell.Y, 0, MobaJungleFormation.DirectionOf(fx, fy));
        var group = new MobaJungleGroup();
        var selupan = await MobaJungle.SpawnMonsterAsync(
            map,
            gameContext,
            MobaJungle.Kind.Selupan,
            slot,
            group,
            cell,
            (_, _) => OnSelupanDiedAsync(map, gameContext, state)).ConfigureAwait(false);
        if (selupan is null)
        {
            lock (state)
            {
                state.SelupanActive = false;
            }

            return;
        }

        if (killer is Player champion)
        {
            champion.Logger.LogInformation("[MOBA-SELUPAN] All the eggs are destroyed: Selupan appears at ({X},{Y}).", cell.X, cell.Y);
        }
    }

    private static async Task OnSelupanDiedAsync(GameMap map, IGameContext gameContext, EventState state)
    {
        if (state.Cancel.IsCancellationRequested)
        {
            return;
        }

        try
        {
            // The hatched spiders that are still around die with him.
            Monster[] spiders;
            lock (state)
            {
                spiders = state.Spiders.ToArray();
                state.Spiders.Clear();
            }

            foreach (var spider in spiders.Where(s => s.IsAlive))
            {
                try
                {
                    await map.RemoveAsync(spider).ConfigureAwait(false);
                    spider.Dispose();
                }
                catch
                {
                    // already gone
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(EggRegenSeconds), state.Cancel.Token).ConfigureAwait(false);
            lock (state)
            {
                state.SelupanActive = false;
            }

            for (var i = 0; i < EggSpots.Count; i++)
            {
                bool missing;
                lock (state)
                {
                    missing = state.Eggs[i] is null;
                }

                if (missing)
                {
                    await SpawnEggAsync(map, gameContext, state, i).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // the match ended
        }
    }

    private sealed class EventState
    {
        public EventState(CancellationTokenSource cancel) => this.Cancel = cancel;

        public CancellationTokenSource Cancel { get; }

        public Monster?[] Eggs { get; } = new Monster?[EggSpots.Count];

        public List<Monster> Spiders { get; } = new();

        public bool SelupanActive { get; set; }
    }
}
