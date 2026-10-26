// <copyright file="MobaPreMatch.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The countdown before a queued match starts: for a few seconds the champions can only move inside a circle around
/// their base, held in by a barrier (walls on the terrain of their arena); nothing else in the arena moves (no bots, no
/// waves, no jungle, no clock). When it ends the barrier goes down and the match starts: jungle, waves, clock.
/// </summary>
public static class MobaPreMatch
{
    /// <summary>Seconds from the moment the match is launched until it starts (players need a few seconds to reconnect).</summary>
    public const int CountdownSeconds = 45;

    /// <summary>Radius, in tiles, of the circle the champions are held in around their base.</summary>
    public const int BarrierRadius = 10;

    /// <summary>Thickness of the barrier wall, in tiles.</summary>
    public const double BarrierThickness = 1.8;

    /// <summary>How long the "match starts" banner stays on screen.</summary>
    private const int BannerMilliseconds = 3500;

    /// <summary>The seconds before the start at which the players are reminded.</summary>
    private static readonly int[] Reminders = [30, 20, 15, 10, 5, 4, 3, 2, 1];

    /// <summary>
    /// Gets the tiles of the barrier around a point.
    /// </summary>
    /// <param name="center">The centre of the circle.</param>
    /// <param name="isWalkable">Whether a tile is walkable (only walkable tiles need a wall).</param>
    /// <returns>The tiles.</returns>
    public static IReadOnlyList<Point> BarrierTiles(Point center, Func<int, int, bool> isWalkable)
    {
        var tiles = new List<Point>();
        var reach = BarrierRadius + 3;
        for (var x = Math.Max(0, center.X - reach); x <= Math.Min(255, center.X + reach); x++)
        {
            for (var y = Math.Max(0, center.Y - reach); y <= Math.Min(255, center.Y + reach); y++)
            {
                var distance = Math.Sqrt(((x - center.X) * (x - center.X)) + ((y - center.Y) * (y - center.Y)));
                if (distance >= BarrierRadius && distance < BarrierRadius + BarrierThickness && isWalkable(x, y))
                {
                    tiles.Add(new Point((byte)x, (byte)y));
                }
            }
        }

        return tiles;
    }

    /// <summary>
    /// Gets the centre of the base circle of a team.
    /// </summary>
    /// <param name="team">The team.</param>
    /// <returns>The centre.</returns>
    public static Point BaseCenterOf(MobaTeam team) => MobaLayout.NearestWalkable(MobaLayout.ShopOf(team).X, MobaLayout.ShopOf(team).Y);

    /// <summary>
    /// Starts the countdown of an arena and puts the barrier up around both bases.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    public static void Begin(GameMap map, IGameContext gameContext)
    {
        var arena = MobaArena.Of(map);
        arena.PreMatchUntilUtc = DateTime.UtcNow.AddSeconds(CountdownSeconds);
        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            foreach (var tile in BarrierTiles(BaseCenterOf(team), (x, y) => map.Terrain.WalkMap[x, y]))
            {
                map.Terrain.WalkMap[tile.X, tile.Y] = false;
                map.Terrain.UpdateAiGridValue(tile.X, tile.Y);
                arena.BarrierTiles.Add(tile);
            }
        }

        _ = RunAsync(map, gameContext);
    }

    /// <summary>
    /// Takes the barrier of an arena down (the countdown is over, or the arena is being taken down).
    /// </summary>
    /// <param name="map">The arena map.</param>
    public static void Lower(GameMap map)
    {
        var arena = MobaArena.Of(map);
        foreach (var tile in arena.BarrierTiles)
        {
            if (MobaLaneWalls.IsWall(arena.Format, tile.X, tile.Y))
            {
                continue; // The countdown ring crosses a lane wall: that tile stays a wall.
            }

            map.Terrain.WalkMap[tile.X, tile.Y] = true;
            map.Terrain.UpdateAiGridValue(tile.X, tile.Y);
        }

        arena.BarrierTiles.Clear();
    }

    private static async Task RunAsync(GameMap map, IGameContext gameContext)
    {
        var arena = MobaArena.Of(map);
        var logger = gameContext.LoggerFactory.CreateLogger(typeof(MobaPreMatch));
        try
        {
            var announced = new HashSet<int>();
            while (DateTime.UtcNow < arena.PreMatchUntilUtc)
            {
                if (arena.PreMatchUntilUtc == DateTime.MinValue || !(await gameContext.GetMapsAsync().ConfigureAwait(false)).Contains(map))
                {
                    return; // The arena was taken down meanwhile.
                }

                await SendBarrierAsync(map, gameContext, true).ConfigureAwait(false);
                await OfferLoadoutsAsync(map, gameContext, open: true).ConfigureAwait(false);
                var remaining = (int)Math.Ceiling((arena.PreMatchUntilUtc - DateTime.UtcNow).TotalSeconds);
                if (Array.IndexOf(Reminders, remaining) >= 0 && announced.Add(remaining))
                {
                    await TellChampionsAsync(map, gameContext, $"[MOBA] La partida empieza en {remaining} s. Hasta entonces solo puedes moverte dentro de tu base.", null).ConfigureAwait(false);
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }

            Lower(map);
            await SendBarrierAsync(map, gameContext, false).ConfigureAwait(false);
            arena.PreMatchUntilUtc = DateTime.MinValue;
            await OfferLoadoutsAsync(map, gameContext, open: false).ConfigureAwait(false);
            arena.MatchStartUtc = DateTime.UtcNow;
            await MobaJungle.EnsureStartedAsync(map, gameContext).ConfigureAwait(false);
            MobaWavePeriodicSpawner.Start(map, gameContext, TimeSpan.FromSeconds(MobaWavePeriodicSpawner.DefaultIntervalSeconds));
            await TellChampionsAsync(map, gameContext, "[MOBA] ¡Comienza la partida!", "¡COMIENZA LA PARTIDA!").ConfigureAwait(false);
            await MobaAnnouncer.AnnounceAsync(map, MobaAnnouncement.MatchStart, 0).ConfigureAwait(false);
            logger.LogInformation("[MOBA-QUEUE] match started on arena {Arena}.", map.Id.ToString()[..8]);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[MOBA-QUEUE] error during the countdown of arena {Arena}.", map.Id.ToString()[..8]);
            Lower(map);
            arena.PreMatchUntilUtc = DateTime.MinValue;
        }
    }

    /// <summary>Tells the champions of the arena the state of the barrier (repeated every second while it is up: the client drops it when the news stop).</summary>
    private static async ValueTask SendBarrierAsync(GameMap map, IGameContext gameContext, bool active)
    {
        var blue = BaseCenterOf(MobaTeam.Blue);
        var red = BaseCenterOf(MobaTeam.Red);
        foreach (var player in (await gameContext.GetPlayersAsync().ConfigureAwait(false)).Where(p => p.IsMobaClone && ReferenceEquals(p.CurrentMap, map)))
        {
            await player.InvokeViewPlugInAsync<Views.Moba.IMobaBarrierPlugIn>(p => p.ShowBarrierAsync(active, (byte)BarrierRadius, blue, red)).ConfigureAwait(false);
        }
    }

    /// <summary>Opens the loadout window of every human champion of the arena which did not get it yet (open) or closes it (the match starts).</summary>
    private static async ValueTask OfferLoadoutsAsync(GameMap map, IGameContext gameContext, bool open)
    {
        foreach (var player in (await gameContext.GetPlayersAsync().ConfigureAwait(false)).Where(p => p.IsMobaClone && p is not MobaBotPlayer && ReferenceEquals(p.CurrentMap, map)))
        {
            await (open ? MobaLoadoutService.OfferAsync(player) : MobaLoadoutService.CloseAsync(player)).ConfigureAwait(false);
        }
    }

    private static async ValueTask TellChampionsAsync(GameMap map, IGameContext gameContext, string blue, string? banner)
    {
        foreach (var player in (await gameContext.GetPlayersAsync().ConfigureAwait(false)).Where(p => p.IsMobaClone && ReferenceEquals(p.CurrentMap, map)))
        {
            await player.ShowBlueMessageAsync(blue).ConfigureAwait(false);
            if (banner is not null)
            {
                await player.InvokeViewPlugInAsync<Views.Moba.IMobaBannerPlugIn>(p => p.ShowBannerAsync(banner, BannerMilliseconds)).ConfigureAwait(false);
            }
        }
    }
}
