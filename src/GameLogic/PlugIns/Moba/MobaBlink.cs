// <copyright file="MobaBlink.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The Teleport skill of the Soul Master and Grand Master (skill 6) in a match (2026-10-25): a short jump of up to 6 tiles to a walkable
/// tile, like a "flash". The champion has it besides its attacks (it is listed with the buffs: it is not one of the picks and its
/// ranks come from the buff points). The cooldown is in <see cref="MobaCooldowns"/> and the mana is the usual one times 3.
/// </summary>
public static class MobaBlink
{
    /// <summary>The number of the Teleport skill.</summary>
    public const short SkillNumber = 6;

    /// <summary>
    /// Whether a champion can jump to a tile: never before the match starts (the barrier of the base would be crossed) and never onto the
    /// green aura of the enemy base (the blacksmith of the other team heals only its own side, but nobody should land on the vendor).
    /// </summary>
    /// <param name="player">The player who casts it.</param>
    /// <param name="target">The tile.</param>
    /// <returns><see langword="true"/> if the jump is allowed.</returns>
    public static bool Allows(Player player, Point target)
    {
        if (!player.IsMobaClone)
        {
            return true;
        }

        if (!player.IsAlive || player.CurrentMap is not { } map || MobaArena.Of(map).IsPreMatch)
        {
            return false;
        }

        var team = MobaTeams.GetTeam(player);
        var enemy = team == MobaTeam.Blue ? MobaTeam.Red : team == MobaTeam.Red ? MobaTeam.Blue : MobaTeam.None;
        return !MobaBaseAura.Contains(enemy, target.X, target.Y);
    }
}
