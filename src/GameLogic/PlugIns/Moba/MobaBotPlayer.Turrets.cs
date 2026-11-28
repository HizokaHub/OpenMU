// <copyright file="MobaBotPlayer.Turrets.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// How the bots treat enemy turrets (2026-11-28). A turret takes minions before champions and kills a champion in about three shots, so a bot
/// only hits one while an allied minion stands inside its reach, and walks away as soon as it is aimed at.
/// </summary>
public sealed partial class MobaBotPlayer
{
    /// <summary>How far outside a turret's reach a bot waits for its minions.</summary>
    private const int TurretWaitMargin = 2;

    /// <summary>How near an allied minion must be to a structure that does not shoot (inhibitor, nexus) to count as support.</summary>
    private const int StructureWaveTiles = TurretDangerTiles + 3;

    private bool _leavingTurret;

    /// <summary>Whether the structure shoots: only the turrets do (the inhibitors and the nexus never attack).</summary>
    private static bool IsShootingTurret(Monster structure)
        => MobaStructures.GetStructureType(structure) == MobaStructureType.Turret;

    private static Point PointAtDistance(Point center, Point toward, int tiles)
    {
        var dx = toward.X - center.X;
        var dy = toward.Y - center.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.1)
        {
            return toward;
        }

        var x = (int)Math.Round(center.X + (dx / length * tiles));
        var y = (int)Math.Round(center.Y + (dy / length * tiles));
        return new Point((byte)Math.Clamp(x, byte.MinValue, byte.MaxValue), (byte)Math.Clamp(y, byte.MinValue, byte.MaxValue));
    }

    /// <summary>
    /// Whether the allied wave covers an attack on the structure: for a turret, a minion inside its reach (that is who it shoots first);
    /// for a structure that does not shoot, a minion nearby.
    /// </summary>
    private bool IsCoveredByWave(GameMap map, Monster structure)
    {
        var tiles = IsShootingTurret(structure) ? structure.Definition.AttackRange : StructureWaveTiles;
        return map.GetAttackablesInRange(structure.Position, tiles)
            .OfType<Monster>()
            .Any(m => m.IsAlive && !MobaStructures.IsStructure(m) && MobaTeams.AreAllies(this, m));
    }

    /// <summary>Walks home while an enemy turret aims at the bot.</summary>
    /// <returns><see langword="true"/> when the bot is leaving and must do nothing else this tick.</returns>
    private async ValueTask<bool> TryLeaveTurretFireAsync(GameMap map)
    {
        var turret = map.GetAttackablesInRange(this.Position, TurretDangerTiles)
            .OfType<Monster>()
            .FirstOrDefault(m => m.IsAlive && IsShootingTurret(m) && MobaTeams.AreEnemies(this, m) && m.IsTargetingPlayer(this));
        if (turret is null)
        {
            this._leavingTurret = false;
            return false;
        }

        if (!this._leavingTurret)
        {
            this._leavingTurret = true;

            // A walk already under way may lead further in: it is replaced by the way home.
            await this.StopWalkingAsync().ConfigureAwait(false);
            this.Logger.LogInformation(
                "[MOBA-AI] {Name} leaving turret fire (turret @ {X},{Y}, covered by wave: {Covered})",
                this.Name,
                turret.Position.X,
                turret.Position.Y,
                this.IsCoveredByWave(map, turret));
        }

        await this.WalkTowardAsync(this._homeSpawn).ConfigureAwait(false);
        return true;
    }

    /// <summary>Keeps the bot just outside an enemy turret's reach while no allied minion is inside it.</summary>
    /// <returns><see langword="true"/> when the bot is waiting and must not attack the turret.</returns>
    private async ValueTask<bool> TryWaitForMinionsAsync(BotContext c, Monster structure)
    {
        if (!IsShootingTurret(structure) || this.IsCoveredByWave(c.Map, structure))
        {
            return false;
        }

        var spot = PointAtDistance(structure.Position, c.Pos, structure.Definition.AttackRange + TurretWaitMargin);
        await this.WalkTowardAsync(spot).ConfigureAwait(false);
        this.EngageNote("waiting for minions under the turret", structure);
        return true;
    }
}
