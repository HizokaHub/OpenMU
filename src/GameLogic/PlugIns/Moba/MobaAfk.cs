// <copyright file="MobaAfk.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// How far the anti-AFK escalation of a champion has gone.
/// </summary>
public enum MobaAfkStage
{
    /// <summary>The champion is active.</summary>
    Active,

    /// <summary>The champion has been idle for <see cref="MobaAfk.AvailableAfterSeconds"/>: the team was told.</summary>
    Idle,

    /// <summary>The champion has been idle for <see cref="MobaAfk.RecallAfterSeconds"/>: it is being sent to its base.</summary>
    Recalling,
}

/// <summary>
/// The anti-AFK of the MOBA mode. A champion whose human gives no input (walking, attacking, casting, using the
/// MOBA keys, chatting) or is disconnected (the ghost stands in the arena) goes through two steps: after
/// <see cref="AvailableAfterSeconds"/> the team is told the champion is idle, and after
/// <see cref="RecallAfterSeconds"/> it is sent to the team's base with the regular recall channel, so it neither
/// stands in a lane nor feeds. Any input of the human clears it. The countdown does not run during the pre-match
/// countdown, while dead, or for bots.
/// </summary>
public static class MobaAfk
{
    /// <summary>Seconds without input before the team is told the champion is idle.</summary>
    public const double AvailableAfterSeconds = 15;

    /// <summary>Seconds without input before the champion is sent to its base.</summary>
    public const double RecallAfterSeconds = 20;

    /// <summary>Tiles around the base within which a champion counts as being at home.</summary>
    private const int AtBaseTiles = 14;

    /// <summary>
    /// Records a sign of life of a human champion. Clears a running escalation.
    /// </summary>
    /// <param name="player">The champion's session.</param>
    /// <returns>A task which completes when the notice (if any) was sent.</returns>
    public static async ValueTask NoteInputAsync(Player player)
    {
        var state = player.MobaState;
        state.LastInputUtc = DateTime.UtcNow;
        if (state.AfkStage == MobaAfkStage.Active)
        {
            return;
        }

        state.AfkStage = MobaAfkStage.Active;
        await player.ShowBlueMessageAsync("[MOBA] Volviste a la partida.").ConfigureAwait(false);
    }

    /// <summary>
    /// Evaluates the idle time of every human champion (and ghost) of every arena. Called every tick.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task which completes when all champions were evaluated.</returns>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        var now = DateTime.UtcNow;
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var champion in players.Where(IsWatched))
        {
            try
            {
                await EvaluateAsync(champion, players, now).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                champion.Logger.LogWarning(ex, "[MOBA-AFK] could not evaluate '{Name}'.", NameOf(champion));
            }
        }
    }

    /// <summary>
    /// Gets what the stage of an idle time is.
    /// </summary>
    /// <param name="idleSeconds">The seconds without input.</param>
    /// <returns>The stage.</returns>
    public static MobaAfkStage StageFor(double idleSeconds)
        => idleSeconds >= RecallAfterSeconds ? MobaAfkStage.Recalling
            : idleSeconds >= AvailableAfterSeconds ? MobaAfkStage.Idle
            : MobaAfkStage.Active;

    // Only the players of a real match: the game masters who test alone in the shared arena (or watch bots without a team) are
    // allowed to stand still.
    private static bool IsWatched(Player player)
        => player.IsMobaClone
           && player is not MobaBotPlayer
           && player.CurrentMap is not null
           && player.CurrentMap.MapId == MobaCloneFactory.ArenaMapNumber
           && player.MobaState.Arena is not null
           && MobaTeams.GetTeam(player) != MobaTeam.None
           && (player is MobaGhostPlayer || player is not OfflinePlayer);

    private static string NameOf(Player player) => player.SelectedCharacter?.Name ?? "?";

    private static async ValueTask EvaluateAsync(Player champion, IList<Player> players, DateTime now)
    {
        var state = champion.MobaState;
        var arena = MobaArena.Of(champion.CurrentMap);
        if (arena.IsPreMatch || !champion.IsAlive)
        {
            // Nothing to do yet / a respawn wait is not idleness.
            state.LastInputUtc = now;
            state.AfkStage = MobaAfkStage.Active;
            return;
        }

        var idle = (now - state.LastInputUtc).TotalSeconds;
        var stage = StageFor(idle);
        if (stage == MobaAfkStage.Active)
        {
            return;
        }

        if (state.AfkStage == MobaAfkStage.Active)
        {
            state.AfkStage = MobaAfkStage.Idle;
            await AnnounceIdleAsync(champion, players).ConfigureAwait(false);
        }

        if (stage == MobaAfkStage.Recalling)
        {
            state.AfkStage = MobaAfkStage.Recalling;
            if (!MobaRecall.IsChanneling(champion) && !IsAtBase(champion))
            {
                // Retried while the champion stays idle (a hit breaks the channel).
                champion.Logger.LogInformation("[MOBA-AFK] '{Name}' idle {Idle:0}s: sending it to its base.", NameOf(champion), idle);
                await MobaRecall.StartRecallAsync(champion).ConfigureAwait(false);
            }
        }
    }

    private static bool IsAtBase(Player champion)
    {
        var team = MobaTeams.GetTeam(champion);
        if (team == MobaTeam.None)
        {
            return true;
        }

        var shop = MobaLayout.ShopOf(team);
        var dx = champion.Position.X - shop.X;
        var dy = champion.Position.Y - shop.Y;
        return (dx * dx) + (dy * dy) <= AtBaseTiles * AtBaseTiles;
    }

    private static async ValueTask AnnounceIdleAsync(Player champion, IList<Player> players)
    {
        var name = NameOf(champion);
        var disconnected = champion is MobaGhostPlayer;
        champion.Logger.LogInformation("[MOBA-AFK] '{Name}' is idle ({Why}).", name, disconnected ? "disconnected" : "no input");
        var team = MobaTeams.GetTeam(champion);
        foreach (var ally in players.Where(p => p != champion && p is not MobaBotPlayer && p is not OfflinePlayer && ReferenceEquals(p.CurrentMap, champion.CurrentMap) && MobaTeams.GetTeam(p) == team))
        {
            await ally.ShowBlueMessageAsync($"[MOBA] {name} está inactivo{(disconnected ? " (desconectado)" : string.Empty)}; irá a la base en {RecallAfterSeconds - AvailableAfterSeconds:0} s.").ConfigureAwait(false);
        }

        if (!disconnected)
        {
            await champion.ShowBlueMessageAsync($"[MOBA] Estás inactivo: si no te mueves en {RecallAfterSeconds - AvailableAfterSeconds:0} s tu campeón volverá a la base.").ConfigureAwait(false);
        }
    }
}
