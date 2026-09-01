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
    private DateTime _lastDripUtc = DateTime.MinValue;

    /// <inheritdoc />
    public void ForceStart() => this._lastDripUtc = DateTime.MinValue;

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
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
}
