// <copyright file="MobaMatchTickPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;
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
    /// <summary>Seconds between the [MOBA-SCORE] match-state log lines (for offline balance analysis).</summary>
    private const double ScoreLogSeconds = 20;

    private DateTime _lastDripUtc = DateTime.MinValue;

    // Static: the plug-in instance is not guaranteed to persist across periodic ticks
    // (see the static _arenaSafezoneCleared above). The match timeline itself lives in MobaArena.
    private static readonly TimeSpan EmptyArenaResetAfter = TimeSpan.FromSeconds(120);

    private static readonly TimeSpan AbandonedInstanceAfter = TimeSpan.FromMinutes(5);

    /// <inheritdoc />
    public void ForceStart() => this._lastDripUtc = DateTime.MinValue;

    /// <inheritdoc />
    public async ValueTask ExecuteTaskAsync(GameContext gameContext)
    {
        // The arena terrain is a copy of Crywolf, which carries a big safezone; on
        // safezone tiles PvP is blocked (champions can't damage each other). A MOBA
        // arena has no safe tiles - strip the flag from every arena map (copies included) once it is live.
        var arenaMaps = (await gameContext.GetMapsAsync().ConfigureAwait(false)).Where(m => m.MapId == MobaCloneFactory.ArenaMapNumber).ToList();
        foreach (var arenaMap in arenaMaps)
        {
            MobaArenaBoot.ClearSafezone(arenaMap);
        }

        // Runs every tick (~1s): expire stacking passive buffs, tick passive DoTs, refresh the DL aura.
        await MobaPassives.TickAsync(gameContext).ConfigureAwait(false);

        // Cap hard CC (S6 freeze / cold can lock a champion for ~10s).
        await MobaCc.CapCrowdControlAsync(gameContext).ConfigureAwait(false);

        // No HP / SD regen while in combat.
        await MobaCombatRegen.TickAsync(gameContext).ConfigureAwait(false);

        // Champion mana regeneration (scaled to the loadout, see MobaMana).
        await MobaMana.TickAsync(gameContext).ConfigureAwait(false);

        // Expire temp shields from Greater Defense / Heal / Soul Barrier.
        await MobaCastEffects.TickAsync(gameContext).ConfigureAwait(false);

        // Dark Lords with an equipped Dark Raven: make it attack with its owner.
        await MobaPets.TickAsync(gameContext).ConfigureAwait(false);

        // Fountains: capture channels, ownership, allied healing.
        await MobaFountains.TickAsync(gameContext).ConfigureAwait(false);

        // The green aura of the blacksmith in each base: 20 % of life, mana and shield per second.
        await MobaBaseAura.TickAsync(gameContext).ConfigureAwait(false);

        // The walls which close the side lanes of the middle-lane-only formats: the clients mirror them.
        await MobaLaneWalls.TickAsync(gameContext).ConfigureAwait(false);

        // Anti-AFK: idle champions are announced to their team and then sent to their base.
        await MobaAfk.TickAsync(gameContext).ConfigureAwait(false);

        // Which disconnected teammates each champion may control (the panel of the control mode).
        await MobaPuppet.TickStatusAsync(gameContext).ConfigureAwait(false);

        // Periodic PvP observability sheets ([MOBA-CHAMP], [MOBA-ECON], [MOBA-WAVE], [MOBA-STRUCT]).
        await MobaTelemetry.TickAsync(gameContext).ConfigureAwait(false);

        // State of the server for the web (players online, champions in a match).
        if (MobaMatchRecorder.StatusSink is not null)
        {
            var online = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            var humans = online.Where(p => p is not MobaBotPlayer && p.Account?.IsBot != true).ToList();
            MobaMatchRecorder.ReportStatus("game", humans.Count, humans.Count(p => p.IsMobaClone));
        }

        var now = DateTime.UtcNow;
        if ((now - this._lastDripUtc).TotalSeconds < MobaLevels.PassiveTickSeconds)
        {
            return;
        }

        this._lastDripUtc = now;

        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var arenaMap in arenaMaps)
        {
            var mobaChampions = players.Where(p => p.IsMobaClone && ReferenceEquals(p.CurrentMap, arenaMap)).ToList();
            await this.TickArenaAsync(gameContext, arenaMap, mobaChampions, now).ConfigureAwait(false);
        }
    }

    /// <summary>One passive step of one arena: its phase, its empty-arena bookkeeping, the passive EXP and gold, the score log.</summary>
    private async ValueTask TickArenaAsync(IGameContext gameContext, GameMap arenaMap, List<Player> mobaChampions, DateTime now)
    {
        var leaderLevel = mobaChampions.Select(p => p.MobaLevel).DefaultIfEmpty(0).Max();
        var arena = MobaArena.Of(arenaMap);
        MobaMatchPhase.Evaluate(arena, mobaChampions);
        TrackEmptiness(arena, mobaChampions.Count, now);
        if (mobaChampions.Count > 0)
        {
            await MobaGolden.TickAsync(gameContext, arenaMap, arena).ConfigureAwait(false);
        }

        await this.ReapIfAbandonedAsync(gameContext, arenaMap, arena, mobaChampions.Count, now).ConfigureAwait(false);

        foreach (var champion in mobaChampions.Where(p => p.MobaLevel < MobaLevels.MaxLevel && !arena.IsPreMatch))
        {
            await MobaExperience.GrantAsync(champion, MobaLevels.PassiveExpPerTick, "passive").ConfigureAwait(false);

            // Passive gold: same anti-snowball idea as the EXP catch-up, but a smaller bonus
            // since gold buys permanent power for the rest of the match.
            var behind = leaderLevel - champion.MobaLevel >= MobaLevels.CatchUpLevelGap;
            var goldAmount = behind ? (int)(MobaGold.PassiveGoldPerTick * MobaGold.BehindPassiveMultiplier) : MobaGold.PassiveGoldPerTick;
            await MobaGold.GrantAsync(champion, goldAmount, "passive").ConfigureAwait(false);
        }

        this.LogMatchState(arena, mobaChampions, now);
    }

    /// <summary>
    /// An extra arena instance nobody plays on any more (no champion in it, none registered for it) for a good while is
    /// taken down, so abandoned matches do not pile up.
    /// </summary>
    private async ValueTask ReapIfAbandonedAsync(IGameContext gameContext, GameMap arenaMap, MobaArena arena, int champions, DateTime now)
    {
        if (!arenaMap.IsInstance
            || champions > 0
            || arena.EmptySinceUtc == DateTime.MinValue
            || now - arena.EmptySinceUtc < AbandonedInstanceAfter
            || MobaMatchRegistry.UsesArena(arenaMap))
        {
            return;
        }

        await MobaArenaBoot.TearDownAsync(arenaMap, gameContext).ConfigureAwait(false);
    }

    /// <summary>
    /// Every <see cref="ScoreLogSeconds"/>, writes one <c>[MOBA-SCORE]</c> line per champion
    /// (elapsed time, team, class, level, K/D/A, HP, invested primary stat) so a full match
    /// can be reconstructed from the log for balance analysis.
    /// </summary>
    private void LogMatchState(MobaArena arena, IEnumerable<Player> players, DateTime now)
    {
        var champions = players.Where(p => p.IsMobaClone && MobaTeams.GetTeam(p) != MobaTeam.None).ToList();
        if (champions.Count == 0)
        {
            return;
        }

        // Set on the first champion of the arena's match. Not reset on a transient empty tick
        // (that made elapsed always read 0); TrackEmptiness restarts it after a long empty spell.
        if (arena.MatchStartUtc == DateTime.MinValue)
        {
            arena.MatchStartUtc = now;
        }

        if ((now - arena.LastScoreLogUtc).TotalSeconds < ScoreLogSeconds)
        {
            return;
        }

        arena.LastScoreLogUtc = now;
        var elapsed = (int)(now - arena.MatchStartUtc).TotalSeconds;

        foreach (var c in champions.OrderBy(c => (int)MobaTeams.GetTeam(c)).ThenByDescending(c => c.MobaLevel))
        {
            var a = c.Attributes;
            var family = MobaPassives.FamilyOf(c);
            var primary = family switch
            {
                MobaFamily.Knight or MobaFamily.RageFighter => Stats.TotalStrength,
                MobaFamily.Elf => Stats.TotalAgility,
                MobaFamily.DarkLord => Stats.TotalLeadership,
                _ => Stats.TotalEnergy,
            };
            var invested = a is null ? 0 : (int)Math.Max(0, a[primary] - MobaCloneFactory.BaselineStatValue);

            var aliveTag = c.IsAlive ? "-" : "DEAD";
            c.Logger.LogInformation(
                "[MOBA-SCORE] t={Elapsed}s {Team} {Class} {Name} Lv{Level} KDA={K}/{D}/{A} HP={Hp:F0}/{MaxHp:F0} [{Dead}] {Primary}+{Invested} dmgX{Scale:F1}",
                elapsed,
                MobaTeams.GetTeam(c),
                c.SelectedCharacter?.CharacterClass?.Name,
                c.SelectedCharacter?.Name,
                c.MobaLevel,
                c.MobaKills,
                c.MobaDeaths,
                c.MobaAssists,
                Math.Max(0f, a?[Stats.CurrentHealth] ?? 0),
                a?[Stats.MaximumHealth] ?? 0,
                aliveTag,
                primary.Designation,
                invested,
                MobaProgression.DamageScaleFor(c));
            c.Logger.LogInformation("[MOBA-INCOME] t={Elapsed}s {Team} {Name} Lv{Level} | {Income}", elapsed, MobaTeams.GetTeam(c), c.SelectedCharacter?.Name, c.MobaLevel, MobaIncome.Describe(c));
        }
    }

    /// <summary>
    /// Starts a fresh timeline (clock, phase) once the arena has been without any champion for a while,
    /// so the next match does not inherit the time and phase of the previous one. The grace period covers
    /// reconnects (a champion is briefly absent between its session and its ghost).
    /// </summary>
    private static void TrackEmptiness(MobaArena arena, int champions, DateTime now)
    {
        if (champions > 0)
        {
            arena.EmptySinceUtc = DateTime.MinValue;
            return;
        }

        if (arena.EmptySinceUtc == DateTime.MinValue)
        {
            arena.EmptySinceUtc = now;
        }
        else if (arena.MatchStartUtc != DateTime.MinValue && (now - arena.EmptySinceUtc) > EmptyArenaResetAfter)
        {
            MobaMatchPhase.Reset(arena);
        }
    }
}
