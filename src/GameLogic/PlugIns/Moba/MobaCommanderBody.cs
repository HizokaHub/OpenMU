// <copyright file="MobaCommanderBody.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.Persistence;

/// <summary>
/// The body of the human commander of a team in the commanders mode. While a player commands, the camera is the commander's and
/// the hero is not played, so the body is parked: it is moved to its base, hidden with the same invisibility effect the game masters
/// use (<see cref="PlayerInvisibilityExtensions.AddInvisibleEffectAsync"/>), cannot be attacked or targeted by creeps, turrets,
/// summons or the enemy, and the anti-AFK ignores it. When the command is given up or the match ends the body is restored.
/// </summary>
/// <remarks>
/// <para>
/// Mechanism (the least invasive one that fits the code): the flag <see cref="MobaChampionState.Parked"/> plus three minimal, central
/// checks that read it through <see cref="IsParked"/>: <see cref="LocateableExtensions.IsActive"/> (so every search for a target of
/// creeps, turrets, summons, golden monsters and the scope lists leaves the body out), <c>Player.AttackByAsync</c> (so no hit,
/// area spell or bot hit lands on it) and <see cref="MobaAfk"/>. The game-master invisibility effect hides the body from the other
/// clients. The flag lives in the champion state, so it survives a disconnect: the ghost left by a commander who disconnects is
/// parked (moved to the base and hidden) by the same one-second sync, and a reconnect parks the new session again.
/// </para>
/// <para>
/// A parked body counts as parked only while the economy of its arena runs, so a stale flag never leaves a champion untouchable.
/// </para>
/// </remarks>
public static class MobaCommanderBody
{
    /// <summary>
    /// Gets whether a located object is the parked body of a commander.
    /// </summary>
    /// <param name="located">The object (anything which is not a champion is never parked).</param>
    /// <returns><see langword="true"/> while the body is parked.</returns>
    public static bool IsParked(object? located)
    {
        if (located is not Player { IsMobaClone: true } player || !player.MobaState.Parked)
        {
            return false;
        }

        return player.CurrentMap is { } map && MobaCommanderBoard.Of(MobaArena.Of(map)).Running;
    }

    /// <summary>
    /// Parks or restores the body of a player to match the command he holds: the commander of his team is parked, everybody else is not.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SyncAsync(Player player)
    {
        if (!player.IsMobaClone || player is MobaBotPlayer || player.CurrentMap is not { } map || player.Account is not { } account)
        {
            return;
        }

        var board = MobaCommanderBoard.Of(MobaArena.Of(map));
        var team = MobaTeams.GetTeam(player);
        var commands = board.Running && team != MobaTeam.None && board.CommanderOf(team) == account.GetId();
        var state = player.MobaState;
        if (commands)
        {
            // A dead body is parked once it has respawned; a new session of the same champion (reconnect) is parked again, and so is
            // the ghost of a commander who disconnected (the command stays his): it is hidden in the base like the body he left.
            if (player.IsAlive && (!state.Parked || !ReferenceEquals(state.ParkedSession, player)))
            {
                await ParkAsync(player).ConfigureAwait(false);
            }
        }
        else if (state.Parked)
        {
            await RestoreAsync(player).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Parks or restores the body of every human champion of an arena (called every second and when the command changes hands).
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SyncAllAsync(GameMap map, IGameContext gameContext)
    {
        foreach (var champion in await MobaCommanderPanel.ChampionsOfAsync(map, gameContext).ConfigureAwait(false))
        {
            try
            {
                await SyncAsync(champion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                champion.Logger.LogWarning(ex, "[MOBA-CMD] could not park or restore the body of the commander.");
            }
        }
    }

    /// <summary>
    /// Restores the body of every champion of an arena which is parked (the match ended).
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask RestoreAllAsync(GameMap map, IGameContext gameContext)
    {
        foreach (var champion in await MobaCommanderPanel.ChampionsOfAsync(map, gameContext).ConfigureAwait(false))
        {
            if (champion.MobaState.Parked)
            {
                try
                {
                    await RestoreAsync(champion).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    champion.Logger.LogWarning(ex, "[MOBA-CMD] could not restore the body of the commander.");
                }
            }
        }
    }

    /// <summary>
    /// Parks a body: moves it to its base, hides it and takes it out of the anti-AFK.
    /// </summary>
    /// <param name="player">The champion.</param>
    /// <returns>A task.</returns>
    /// <remarks>
    /// The flag <see cref="MobaChampionState.Parked"/> is raised only at the end, once the body stands in the base and is invisible:
    /// while it is up <see cref="LocateableExtensions.IsActive"/> is false for the body, and the scope lists of the other clients
    /// only send "out of scope" for active objects, so a body which moved or respawned with the flag already up would stay drawn as a
    /// frozen ghost on the clients which saw it. If the move or the effect fail the flag stays down and the next second tries again.
    /// </remarks>
    public static async ValueTask ParkAsync(Player player)
    {
        var state = player.MobaState;
        var already = state.Parked && ReferenceEquals(state.ParkedSession, player);
        state.LastInputUtc = DateTime.UtcNow;
        state.AfkStage = MobaAfkStage.Active;
        MobaRecall.Interrupt(player);
        if (already)
        {
            return;
        }

        // A flag left by an earlier session (reconnect) comes down for the moment of the move, for the same reason.
        state.Parked = false;
        state.ParkedSession = null;
        if (MobaRecall.BaseOf(player) is { } home)
        {
            await player.MoveAsync(home).ConfigureAwait(false);
        }

        await player.AddInvisibleEffectAsync().ConfigureAwait(false);
        state.ParkedSession = player;
        state.Parked = true;
        await player.ShowBlueMessageAsync("[CMD] Comandas desde el mando: tu héroe queda aparcado en la base, invisible y a salvo.").ConfigureAwait(false);
    }

    /// <summary>
    /// Restores a body: visible again and a normal target of the match.
    /// </summary>
    /// <param name="player">The champion.</param>
    /// <returns>A task.</returns>
    public static async ValueTask RestoreAsync(Player player)
    {
        var state = player.MobaState;
        state.Parked = false;
        state.ParkedSession = null;

        // The idle clock restarts now: the time spent commanding was not idleness, and it must not count when the hero is back.
        state.LastInputUtc = DateTime.UtcNow;
        state.AfkStage = MobaAfkStage.Active;
        await player.RemoveInvisibleEffectAsync().ConfigureAwait(false);
        await player.ShowBlueMessageAsync("[CMD] Dejaste el mando: tu héroe vuelve a la partida.").ConfigureAwait(false);
    }
}
