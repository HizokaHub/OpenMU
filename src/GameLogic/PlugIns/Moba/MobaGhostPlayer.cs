// <copyright file="MobaGhostPlayer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.Persistence;

/// <summary>
/// The body of a MOBA champion whose human is disconnected: it stays in the arena where the
/// human left it (same clone, same health, same progress), without any intelligence. It can be
/// hit and killed like any champion. When the human reconnects the ghost is removed and the new
/// session takes over its health and position; <c>/mobaleave</c> and the end of the match remove
/// it too.
/// </summary>
public sealed class MobaGhostPlayer : OfflinePlayer
{
    private static readonly TimeSpan ReconnectWaitLimit = TimeSpan.FromSeconds(45);

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaGhostPlayer"/> class.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    public MobaGhostPlayer(IGameContext gameContext)
        : base(gameContext)
    {
    }

    /// <summary>
    /// What a disconnecting session leaves to its ghost.
    /// </summary>
    /// <param name="Account">The account.</param>
    /// <param name="CloneCharacter">The clone character.</param>
    /// <param name="Real">The real character the clone was made from.</param>
    /// <param name="State">The champion's match progress.</param>
    /// <param name="Vitals">The body at the time of the disconnect.</param>
    /// <param name="WasAlive">Whether the champion was alive.</param>
    public sealed record Handover(Account Account, Character CloneCharacter, Character? Real, MobaChampionState State, MobaVitals? Vitals, bool WasAlive)
    {
        /// <summary>Gets the map instance the match is played on (<see langword="null"/> for the shared arena).</summary>
        public GameMap? Arena => this.State.Arena;
    }

    /// <summary>
    /// Captures what a disconnecting session has to hand over, if it is a human champion in a match.
    /// Has to be called while the session still has its character.
    /// </summary>
    /// <param name="player">The session which is about to disconnect.</param>
    /// <returns>The hand-over, or <see langword="null"/> if no ghost is needed.</returns>
    public static Handover? Capture(Player player)
    {
        if (!player.IsMobaClone
            || player is OfflinePlayer
            || player.Account is not { } account
            || player.SelectedCharacter is not { } clone
            || player.CurrentMap?.MapId != MobaCloneFactory.ArenaMapNumber
            || !MobaMatchRegistry.TryGetState(account.GetId(), out var state)
            || state is null
            || !ReferenceEquals(state, player.MobaState))
        {
            return null;
        }

        clone.PositionX = (byte)player.Position.X;
        clone.PositionY = (byte)player.Position.Y;
        return new Handover(account, clone, player.MobaRealCharacter, state, MobaVitals.Capture(player), player.IsAlive);
    }

    /// <summary>
    /// Puts the ghost of a disconnected champion into the arena.
    /// </summary>
    /// <param name="gameContext">The game context of the session which disconnected.</param>
    /// <param name="handover">What the session left.</param>
    /// <returns>A task which completes when the ghost stands in the arena.</returns>
    public static async Task SpawnAsync(IGameContext gameContext, Handover handover)
    {
        try
        {
            if (!handover.WasAlive)
            {
                // Disconnecting does not skip the respawn wait: the ghost shows up when it would have.
                var arenaMap = handover.Arena ?? await gameContext.GetMapAsync(MobaCloneFactory.ArenaMapNumber, false).ConfigureAwait(false);
                await Task.Delay(MobaRespawn.FullDelayOf(handover.State, MobaArena.Of(arenaMap))).ConfigureAwait(false);
            }

            if (!MobaMatchRegistry.TryGetState(handover.Account.GetId(), out var state) || !ReferenceEquals(state, handover.State))
            {
                return; // The match ended or the champion left meanwhile.
            }

            var ghost = new MobaGhostPlayer(gameContext);
            if (await ghost.StartAsync(handover).ConfigureAwait(false))
            {
                handover.State.Ghost = ghost;
                if (!MobaMatchRegistry.IsInMatch(handover.Account.GetId()))
                {
                    // Left while the ghost was being created.
                    handover.State.Ghost = null;
                    await ghost.DisconnectAsync().ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            gameContext.LoggerFactory.CreateLogger<MobaGhostPlayer>().LogError(ex, "[MOBA-GHOST] failed to spawn the ghost of '{Name}'.", handover.CloneCharacter.Name);
        }
    }

    /// <summary>
    /// Removes the ghost of an account, if it has one, and returns its body so the reconnecting
    /// session continues with it. Waits for a dead ghost to respawn first.
    /// </summary>
    /// <param name="state">The champion's progress.</param>
    /// <returns>The body of the ghost and its position, or <see langword="null"/> if there was no ghost.</returns>
    public static async Task<(MobaVitals? Vitals, Point Position)?> TakeOverAsync(MobaChampionState state)
    {
        if (state.Ghost is not { } ghost)
        {
            return null;
        }

        var deadline = DateTime.UtcNow + ReconnectWaitLimit;
        while (!ghost.IsAlive && ghost.CurrentMap is not null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(250).ConfigureAwait(false);
        }

        var result = (MobaVitals.Capture(ghost), ghost.Position);
        state.Ghost = null;
        await ghost.DisconnectAsync().ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc />
    protected override void StartIntelligence()
    {
        // A ghost does nothing on its own.
    }

    private async ValueTask<bool> StartAsync(Handover handover)
    {
        try
        {
            this.Account = handover.Account;
            this.SuppressPersistence = true;
            this.IsMobaClone = true;
            this.MobaState = handover.State;
            this.MobaRealCharacter = handover.Real;
            this.BoundMap = handover.Arena;

            await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.LoginScreen).ConfigureAwait(false);
            await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.Authenticated).ConfigureAwait(false);
            await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.CharacterSelection).ConfigureAwait(false);

            await this.GameContext.AddPlayerAsync(this).ConfigureAwait(false);
            await this.SetSelectedCharacterAsync(handover.CloneCharacter).ConfigureAwait(false);

            MobaCloneFactory.OnCloneAttached(this);
            if (handover.WasAlive)
            {
                handover.Vitals?.ApplyTo(this);
            }

            await this.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);

            MobaTeams.Set(this, handover.State.Team);
            this.HuntingOrigin = this.Position;
            this.Died -= SelectCharacterAction_Died;
            this.Died += SelectCharacterAction_Died;

            this.Logger.LogInformation("[MOBA-GHOST] '{Name}' ({Team}) stands in for its disconnected player at {Pos}.", handover.CloneCharacter.Name, handover.State.Team, this.Position);
            return true;
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "[MOBA-GHOST] failed to start the ghost of '{Name}'.", handover.CloneCharacter.Name);
            return false;
        }
    }

    private static void SelectCharacterAction_Died(object? sender, DeathInformation death)
        => PlayerActions.Character.SelectCharacterAction.OnMobaChampionDied(sender, death);
}
