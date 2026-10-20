// <copyright file="MobaArenaBoot.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Brings an arena map to life when a match starts on it (vendors, turrets and nexuses, jungle) and takes
/// everything down again when the match is over. Works on any map of the arena's number: the one a game server
/// hosts for everybody and the extra instances of <see cref="IGameContext.CreateInstanceMapAsync"/>.
/// </summary>
public static class MobaArenaBoot
{
    /// <summary>
    /// Gets the arena a command applies to: the arena map the player is on, else the shared one of the game server.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>The map, or <see langword="null"/> if the server has no arena.</returns>
    public static async ValueTask<GameMap?> ArenaOfAsync(Player player)
        => player.CurrentMap is { } current && current.MapId == MobaCloneFactory.ArenaMapNumber
            ? current
            : await player.GameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber).ConfigureAwait(false);

    /// <summary>
    /// Makes an arena ready for a match: no safezone, the vendors, the turrets and nexuses and the jungle.
    /// Does nothing for what already exists.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask EnsureAsync(GameMap map, IGameContext gameContext)
    {
        ClearSafezone(map);
        await MobaShop.EnsureVendorsAsync(map, gameContext).ConfigureAwait(false);
        await MobaStructureSpawner.EnsureStructuresAsync(map, gameContext).ConfigureAwait(false);
        if (!MobaArena.Of(map).IsPreMatch)
        {
            // In the countdown the jungle waits: it starts with the match (see MobaPreMatch).
            await MobaJungle.EnsureStartedAsync(map, gameContext).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Strips the safezone flags a Crywolf-based arena terrain carries: on safezone tiles champions cannot hurt each other.
    /// </summary>
    /// <param name="map">The arena map.</param>
    public static void ClearSafezone(GameMap map)
    {
        var arena = MobaArena.Of(map);
        if (arena.SafezoneCleared || map.Terrain?.SafezoneMap is not { } safezone)
        {
            return;
        }

        for (var x = 0; x < safezone.GetLength(0); x++)
        {
            for (var y = 0; y < safezone.GetLength(1); y++)
            {
                safezone[x, y] = false;
            }
        }

        arena.SafezoneCleared = true;
    }

    /// <summary>
    /// Takes down everything a match left on an arena map: bots, creeps, jungle, structures, vendors and timers, and
    /// returns the arena to a fresh state. An extra instance is then removed from the game context; the shared
    /// arena stays for the next match.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TearDownAsync(GameMap map, IGameContext gameContext)
    {
        MobaPreMatch.Lower(map);
        MobaArena.Of(map).PreMatchUntilUtc = DateTime.MinValue;
        MobaWavePeriodicSpawner.Stop(map);
        MobaBotObjectives.Reset(MobaArena.Of(map));
        await MobaJungle.StopAsync(map).ConfigureAwait(false);
        await MobaBotPlayer.ClearAllAsync(map).ConfigureAwait(false);
        await MobaWaveSpawner.DespawnAllCreepsAsync(map).ConfigureAwait(false);
        await MobaStructureSpawner.RemoveTurretsAsync(map).ConfigureAwait(false);
        await MobaStructureSpawner.RemoveNexusesAsync(map).ConfigureAwait(false);
        await MobaShop.RemoveVendorsAsync(map).ConfigureAwait(false);
        MobaMatchPhase.Reset(MobaArena.Of(map));
        if (map.IsInstance)
        {
            await gameContext.RemoveInstanceMapAsync(map).ConfigureAwait(false);
        }
    }
}
