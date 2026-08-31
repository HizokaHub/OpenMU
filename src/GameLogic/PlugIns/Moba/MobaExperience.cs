// <copyright file="MobaExperience.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Interfaces;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Grants MOBA champion experience and handles level-ups (skill point + milestone pick
/// notification). Champion level is <see cref="Player.MobaLevel"/>; it is separate from
/// the MU character level.
/// </summary>
public static class MobaExperience
{
    /// <summary>
    /// Adds <paramref name="amount"/> experience to a champion and processes any level-ups.
    /// </summary>
    /// <param name="champion">The champion (a MOBA clone player).</param>
    /// <param name="amount">The experience to add.</param>
    /// <param name="reason">Short tag for logging (creep / champion / turret / passive).</param>
    public static async ValueTask GrantAsync(Player champion, long amount, string reason)
    {
        if (!champion.IsMobaClone)
        {
            return;
        }

        if (amount <= 0 || champion.MobaLevel >= MobaLevels.MaxLevel)
        {
            await PushStateAsync(champion).ConfigureAwait(false);
            return;
        }

        champion.MobaExperience += amount;

        var leveledUp = false;
        while (champion.MobaLevel < MobaLevels.MaxLevel
               && champion.MobaExperience >= MobaLevels.ExpToNext(champion.MobaLevel))
        {
            champion.MobaExperience -= MobaLevels.ExpToNext(champion.MobaLevel);
            champion.MobaLevel++;
            champion.MobaSkillPoints++;
            leveledUp = true;

            champion.Logger.LogDebug("[MOBA] {Name} -> champion level {Level} (via {Reason})", champion.SelectedCharacter?.Name, champion.MobaLevel, reason);

            if (MobaLevels.IsMilestone(champion.MobaLevel))
            {
                await champion.InvokeViewPlugInAsync<IShowMessagePlugIn>(p =>
                    p.ShowMessageAsync($"NIVEL {champion.MobaLevel} - elegí una habilidad", MessageType.GoldenCenter)).ConfigureAwait(false);
                await champion.ShowBlueMessageAsync($"[MOBA] ¡Nivel {champion.MobaLevel}! Tenés un pick de habilidad disponible.").ConfigureAwait(false);
            }
            else
            {
                await champion.ShowBlueMessageAsync($"[MOBA] Nivel de campeón {champion.MobaLevel} (+1 punto de habilidad, {champion.MobaSkillPoints} sin gastar).").ConfigureAwait(false);
            }

            if (champion.MobaLevel >= MobaLevels.MaxLevel)
            {
                await champion.ShowBlueMessageAsync("[MOBA] Nivel de campeón máximo alcanzado.").ConfigureAwait(false);
            }
        }

        if (leveledUp)
        {
            // TODO(step 3): open the milestone pick window here instead of just messaging.
        }

        await PushStateAsync(champion).ConfigureAwait(false);
    }

    /// <summary>Sends the champion its current level / experience / skill points for the HUD bar.</summary>
    /// <param name="champion">The champion.</param>
    public static ValueTask PushStateAsync(Player champion)
    {
        if (!champion.IsMobaClone)
        {
            return ValueTask.CompletedTask;
        }

        var toNext = champion.MobaLevel >= MobaLevels.MaxLevel ? 0 : MobaLevels.ExpToNext(champion.MobaLevel);
        return champion.InvokeViewPlugInAsync<IMobaChampionStatePlugIn>(p =>
            p.ShowChampionStateAsync(champion.MobaLevel, champion.MobaExperience, toNext, champion.MobaSkillPoints));
    }

    /// <summary>Grants EXP to every MOBA champion of <paramref name="team"/> on the map.</summary>
    /// <param name="map">The arena map.</param>
    /// <param name="team">The team to reward.</param>
    /// <param name="amount">The experience each champion gets.</param>
    /// <param name="reason">Log tag.</param>
    public static async ValueTask GrantToTeamAsync(GameMap map, MobaTeam team, long amount, string reason)
    {
        foreach (var champion in ChampionsOnMap(map).Where(c => MobaTeams.GetTeam(c) == team).ToList())
        {
            await GrantAsync(champion, amount, reason).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// <see cref="Player.Died"/> handler for a champion clone: rewards the enemy killer
    /// (scaled by the victim's level) and nearby enemy assisters.
    /// </summary>
    /// <param name="victim">The champion that died.</param>
    /// <param name="death">The death information (carries the killer id).</param>
    public static async Task HandleChampionDeathAsync(Player victim, DeathInformation death)
    {
        try
        {
            if (!victim.IsMobaClone || victim.CurrentMap is not { } map)
            {
                return;
            }

            if (map.GetObject(death.KillerId) is not Player killer
                || !killer.IsMobaClone
                || !MobaTeams.AreEnemies(killer, victim))
            {
                return;
            }

            var killExp = MobaLevels.ChampionKillExp + (victim.MobaLevel * MobaLevels.ChampionKillPerVictimLevel);
            await GrantAsync(killer, killExp, "champion").ConfigureAwait(false);

            var killerTeam = MobaTeams.GetTeam(killer);
            var assisters = map.GetAttackablesInRange(victim.Position, MobaLevels.ShareRadius)
                .OfType<Player>()
                .Where(p => p.IsMobaClone && !ReferenceEquals(p, killer) && MobaTeams.GetTeam(p) == killerTeam)
                .ToList();

            foreach (var assister in assisters)
            {
                await GrantAsync(assister, MobaLevels.AssistExp, "assist").ConfigureAwait(false);
            }
        }
        catch
        {
            // best effort
        }
    }

    private static IEnumerable<Player> ChampionsOnMap(GameMap map)
        => map.GetAttackablesInRange(new Point(128, 128), 400).OfType<Player>().Where(p => p.IsMobaClone);
}
