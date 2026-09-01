// <copyright file="MobaMatchTickPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Per-match periodic upkeep for the MOBA mode. Right now: the passive champion EXP drip
/// (every <see cref="MobaLevels.PassiveTickSeconds"/> seconds every champion in a match
/// gains a little EXP, like the LoL baseline). More match upkeep will hang off this.
/// </summary>
[PlugIn]
[Display(Name = "MOBA: match tick", Description = "Passive champion EXP drip and other per-match upkeep.")]
[Guid("6A1F8C34-9D27-4B50-8E63-2C7A0B4F1D95")]
public class MobaMatchTickPlugIn : IPeriodicTaskPlugIn
{
    private static bool _arenaSafezoneCleared;

    private DateTime _lastDripUtc = DateTime.MinValue;

    /// <inheritdoc />
    public void ForceStart() => this._lastDripUtc = DateTime.MinValue;

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        // The arena terrain is a copy of Crywolf, which carries a big safezone; on
        // safezone tiles PvP is blocked (champions can't damage each other). A MOBA
        // arena has no safe tiles - strip the flag once the map is live.
        await EnsureArenaHasNoSafezoneAsync(gameContext).ConfigureAwait(false);

        // Runs every tick (~1s): expire stacking passive buffs, tick passive DoTs, refresh the DL aura.
        await MobaPassives.TickAsync(gameContext).ConfigureAwait(false);

        var now = DateTime.UtcNow;
        if ((now - this._lastDripUtc).TotalSeconds < MobaLevels.PassiveTickSeconds)
        {
            return;
        }

        this._lastDripUtc = now;

        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var champion in players.Where(p => p.IsMobaClone && p.MobaLevel < MobaLevels.MaxLevel).ToList())
        {
            await MobaExperience.GrantAsync(champion, MobaLevels.PassiveExpPerTick, "passive").ConfigureAwait(false);
        }
    }

    private static async ValueTask EnsureArenaHasNoSafezoneAsync(IGameContext gameContext)
    {
        if (_arenaSafezoneCleared)
        {
            return;
        }

        var maps = await gameContext.GetMapsAsync().ConfigureAwait(false);
        var arena = maps.FirstOrDefault(m => m.MapId == MobaCloneFactory.ArenaMapNumber);
        if (arena?.Terrain?.SafezoneMap is not { } safezone)
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

        _arenaSafezoneCleared = true;
    }
}
