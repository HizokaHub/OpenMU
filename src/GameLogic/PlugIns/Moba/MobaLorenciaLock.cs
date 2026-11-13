// <copyright file="MobaLorenciaLock.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;

/// <summary>
/// Lorencia is the lobby of the MOBA server (2026-11-13, decided by the user): its four gates (Dungeon, Devias, Noria and the fourth one) and the
/// warp list are closed for normal players so the only thing to play is the MOBA. Game masters can still move anywhere.
/// </summary>
public static class MobaLorenciaLock
{
    private const short LorenciaMapNumber = 0;

    /// <summary>Gets or sets a value indicating whether the exits of Lorencia are closed. Set to <see langword="false"/> to reopen them.</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>Gets whether the exit of the player towards the target map is closed.</summary>
    /// <param name="player">The player.</param>
    /// <param name="target">The map the player wants to go to.</param>
    /// <returns><see langword="true"/> if the player must stay in Lorencia.</returns>
    public static bool Blocks(Player player, GameMapDefinition? target)
        => Enabled
           && target is not null
           && player.CurrentMap?.Definition.Number == LorenciaMapNumber
           && target.Number != LorenciaMapNumber
           && player.SelectedCharacter?.CharacterStatus != CharacterStatus.GameMaster;

    /// <summary>The message shown to a player whose exit is closed.</summary>
    public const string Message = "[MOBA] Las salidas de Lorencia están cerradas: habla con el NPC de partidas para jugar el MOBA.";
}
