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
            && clone is not null)
        {
            // The account is in a MOBA match: enter the world as the ephemeral clone
            // held by the registry (keeps its state across reconnects), not the real
            // character.
            player.MobaRealCharacter = realCharacter;
            player.SuppressPersistence = true;
            player.IsMobaClone = true;
            player.MobaLevel = 1;
            player.MobaExperience = 0;
            player.MobaSkillPoints = 0;
            player.MobaSkillCooldowns.Clear();
            player.MobaSkillGraceEnds.Clear();
            player.MobaKills = 0;
            player.MobaDeaths = 0;
            player.MobaAssists = 0;
            player.Died -= MobaChampionDiedHandler;
            player.Died += MobaChampionDiedHandler;
            await player.SetSelectedCharacterAsync(clone).ConfigureAwait(false);
            MobaCloneFactory.OnCloneAttached(player);

            // Default team until real matchmaking assigns one; overridable with /mobateam.
            if (MobaTeams.GetTeam(player) == MobaTeam.None)
            {
                MobaTeams.Set(player, MobaTeam.Blue);
            }

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

    private static void MobaChampionDiedHandler(object? sender, DeathInformation death)
    {
        if (sender is Player victim)
        {
            _ = MobaExperience.HandleChampionDeathAsync(victim, death);
            _ = RespawnAtMidAsync(victim);
        }
    }

    /// <summary>
    /// After the engine's 3s respawn (which drops the champion at the map's default spawn
    /// gate), snap a human MOBA champion to mid (128, 128) so it re-enters the fight in the
    /// centre of the arena. Bots handle their own respawn (back to their lane start).
    /// </summary>
    private static async Task RespawnAtMidAsync(Player victim)
    {
        try
        {
            if (victim is Offline.OfflinePlayer)
            {
                return;
            }

            await Task.Delay(3300).ConfigureAwait(false);
            if (victim.IsAlive && victim.CurrentMap?.MapId == PlugIns.Moba.MobaCloneFactory.ArenaMapNumber)
            {
                await victim.MoveAsync(new MUnique.OpenMU.Pathfinding.Point(128, 128)).ConfigureAwait(false);
            }
        }
        catch
        {
            // best effort
        }
    }
}