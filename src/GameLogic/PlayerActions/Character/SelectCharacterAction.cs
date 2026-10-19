// <copyright file="SelectCharacterAction.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlayerActions.Character;

using MUnique.OpenMU.GameLogic.PlugIns.Moba;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Action to select a character and enter the world with it.
/// </summary>
public class SelectCharacterAction
{
    /// <summary>
    /// Selects the character and enters the world.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="characterName">Name of the character.</param>
    public async ValueTask SelectCharacterAsync(Player player, string characterName)
    {
        using var loggerScope = player.Logger.BeginScope(this.GetType());
        if (player.PlayerState.CurrentState != PlayerState.CharacterSelection)
        {
            player.Logger.LogError("Could not select character because of wrong current player state: {0}", player.PlayerState.CurrentState);
            await player.DisconnectAsync().ConfigureAwait(false);
            return;
        }

        var realCharacter = player.Account?.Characters.FirstOrDefault(c => c.Name.Equals(characterName));

        if (realCharacter is not null
            && player.Account is { } account
            && MobaMatchRegistry.TryGetClone(account.GetId(), out var clone)
            && clone is not null
            && MobaMatchRegistry.TryGetState(account.GetId(), out var championState)
            && championState is not null)
        {
            // The account is in a MOBA match: enter the world as the ephemeral clone
            // held by the registry, not the real character. Level, EXP, points, K/D/A,
            // team and cooldowns are the champion's own state object (it survives
            // reconnects); the body (health, position) comes from the ghost which stood
            // in for the disconnected player, if there is one.
            var ghostBody = await MobaGhostPlayer.TakeOverAsync(championState).ConfigureAwait(false);
            if (ghostBody is { } body)
            {
                clone.PositionX = (byte)body.Position.X;
                clone.PositionY = (byte)body.Position.Y;
            }

            player.MobaRealCharacter = realCharacter;
            player.SuppressPersistence = true;
            player.IsMobaClone = true;
            player.MobaState = championState;
            player.Died -= OnMobaChampionDied;
            player.Died += OnMobaChampionDied;
            await player.SetSelectedCharacterAsync(clone).ConfigureAwait(false);
            MobaCloneFactory.OnCloneAttached(player);
            ghostBody?.Vitals?.ApplyTo(player);

            // Default team until real matchmaking assigns one; overridable with /mobateam.
            MobaTeams.Set(player, championState.Team == MobaTeam.None ? MobaTeam.Blue : championState.Team);

            await MobaShop.EnsureVendorsAsync(player.GameContext).ConfigureAwait(false);
            await PlugIns.Moba.MobaStructureSpawner.EnsureStructuresAsync(player.GameContext).ConfigureAwait(false);
            await PlugIns.Moba.MobaJungle.EnsureStartedAsync(player.GameContext).ConfigureAwait(false);

            player.Logger.LogInformation("Account {0} entered the MOBA match as a clone of '{1}'.", account.LoginName, characterName);
            return;
        }

        await player.SetSelectedCharacterAsync(realCharacter).ConfigureAwait(false);
        if (player.SelectedCharacter is null)
        {
            player.Logger.LogError("Could not select character because character not found: [{0}]", characterName);
            await player.DisconnectAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Handles the death of a human MOBA champion (or its ghost): EXP / K-D-A bookkeeping and the respawn notice.
    /// </summary>
    /// <param name="sender">The champion.</param>
    /// <param name="death">The death information.</param>
    public static void OnMobaChampionDied(object? sender, DeathInformation death)
    {
        if (sender is Player victim)
        {
            _ = MobaExperience.HandleChampionDeathAsync(victim, death);
            _ = AnnounceRespawnAsync(victim);
        }
    }

    /// <summary>
    /// Tells a human champion how long the death costs. The respawn itself (delay and the
    /// spot next to the team's shop) is handled by <see cref="PlugIns.Moba.MobaRespawn"/>.
    /// </summary>
    private static async Task AnnounceRespawnAsync(Player victim)
    {
        try
        {
            if (victim is Offline.OfflinePlayer)
            {
                return;
            }

            var seconds = (int)Math.Ceiling(PlugIns.Moba.MobaRespawn.CurrentDelay().TotalSeconds);
            await victim.ShowBlueMessageAsync($"[MOBA] You respawn in {seconds} s.").ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }
}