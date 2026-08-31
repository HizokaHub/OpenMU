// <copyright file="MobaWavePeriodicSpawner.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;

/// <summary>
/// Process-wide holder of the periodic "both teams push a lane wave every N seconds"
/// timers, one per map. Test scaffolding for Fase 2 until a real match context owns the
/// wave rhythm (and stops it on match end / empty arena).
/// </summary>
public static class MobaWavePeriodicSpawner
{
    /// <summary>Default seconds between waves when <c>/mobawaves</c> is started without an interval.</summary>
    public const int DefaultIntervalSeconds = 48;

    private static readonly ConcurrentDictionary<ushort, Timer> Running = new();

    /// <summary>Whether a periodic spawner is running for the given map.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns><see langword="true"/> if running.</returns>
    public static bool IsRunning(ushort mapId) => Running.ContainsKey(mapId);

    /// <summary>
    /// (Re)starts the periodic spawner for a map: a blue and a red wave now and every
    /// <paramref name="interval"/> after.
    /// </summary>
    /// <param name="map">The map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="interval">Time between wave sets.</param>
    public static void Start(GameMap map, IGameContext gameContext, TimeSpan interval)
    {
        Stop(map.MapId);
        var timer = new Timer(_ => _ = TickAsync(map, gameContext), null, TimeSpan.Zero, interval);
        Running[map.MapId] = timer;
    }

    /// <summary>Stops and disposes the periodic spawner for a map, if any.</summary>
    /// <param name="mapId">The map id.</param>
    /// <returns><see langword="true"/> if one was running.</returns>
    public static bool Stop(ushort mapId)
    {
        if (Running.TryRemove(mapId, out var timer))
        {
            timer.Dispose();
            return true;
        }

        return false;
    }

    private static async Task TickAsync(GameMap map, IGameContext gameContext)
    {
        try
        {
            await MobaWaveSpawner.SpawnWaveAsync(map, gameContext, MobaTeam.Blue).ConfigureAwait(false);
            await MobaWaveSpawner.SpawnWaveAsync(map, gameContext, MobaTeam.Red).ConfigureAwait(false);
        }
        catch
        {
            // Keep the timer alive; a failed round just means no wave that tick.
        }
    }
}
