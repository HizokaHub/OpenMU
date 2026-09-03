// <copyright file="MobaTelemetry.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.Persistence;

/// <summary>
/// Rich PvP observability for MOBA balance work. Everything here only writes to the log
/// (markers below) - it never changes game state. Read the markers offline to reconstruct
/// a fight without needing the client open:
/// <list type="bullet">
/// <item><c>[MOBA-DMG]</c> - one line per hit (enriched: positions, levels, %HP, HP-after, mitigation, distance).</item>
/// <item><c>[MOBA-FIGHT]</c> - an engagement between two champions started.</item>
/// <item><c>[MOBA-BURST]</c> - a champion just took a huge chunk of its max HP in a short window (one-shot watch).</item>
/// <item><c>[MOBA-KILL]</c> - a champion died: killer, assists, time-to-kill, damage breakdown, overkill.</item>
/// <item><c>[MOBA-CC]</c> - crowd control applied (kind, requested vs applied ms, DR stack, tenacity).</item>
/// <item><c>[MOBA-HEAL]</c> - a champion healed / lifestole / shielded.</item>
/// <item><c>[MOBA-CHAMP]</c> - periodic per-champion combat sheet (EHP, mana, mitigation, crit%, range, MS, DPS, top skills).</item>
/// <item><c>[MOBA-ECON]</c> - periodic per-team totals + the Blue/Red level gap (snowball curve).</item>
/// <item><c>[MOBA-WAVE]</c> - periodic creep counts and lane frontier per team.</item>
/// <item><c>[MOBA-STRUCT]</c> - periodic structure HP, plus destruction events.</item>
/// </list>
/// </summary>
public static class MobaTelemetry
{
    /// <summary>A champion is considered "burst" if it loses this fraction of its max HP within <see cref="BurstWindow"/>.</summary>
    private const double BurstFraction = 0.55;

    private static readonly TimeSpan BurstWindow = TimeSpan.FromSeconds(2);

    /// <summary>Gap (no damage either way) that ends an engagement.</summary>
    private static readonly TimeSpan EngagementIdle = TimeSpan.FromSeconds(6);

    /// <summary>How often the periodic sheets are written.</summary>
    private static readonly TimeSpan PeriodicInterval = TimeSpan.FromSeconds(15);

    private static readonly ConditionalWeakTable<Player, ChampStats> StatsTable = new();
    private static readonly ConditionalWeakTable<Player, Engagement> Engagements = new();

    private static DateTime _lastPeriodicUtc = DateTime.MinValue;

    /// <summary>Running per-champion combat totals for the current server session.</summary>
    private sealed class ChampStats
    {
        public DateTime FirstSeenUtc = DateTime.UtcNow;

        public long DamageDealt;

        public long DamageTaken;

        public long Healed;

        public int Hits;

        public int Crits;

        public int BurstEvents;

        public readonly Dictionary<string, long> PerSkillDealt = new();
    }

    /// <summary>The in-progress fight for one victim: who is hitting it and how hard, since when.</summary>
    private sealed class Engagement
    {
        public DateTime StartUtc;

        public DateTime LastHitUtc;

        public float HpAtStart;

        public readonly Dictionary<string, long> DamageByAttacker = new();

        public readonly List<(DateTime When, long Amount)> Recent = new();

        public bool FightLineLogged;

        public bool Flushed;
    }

    /// <summary>Writes the [MOBA-TRADE] close-out for one victim's engagement (idempotent).</summary>
    private static void FlushEngagement(Player victim, Engagement eng, string ending)
    {
        if (eng.Flushed || eng.DamageByAttacker.Count == 0)
        {
            return;
        }

        eng.Flushed = true;
        var now = DateTime.UtcNow;
        var taken = eng.DamageByAttacker.Values.Sum();
        var dur = Math.Max(0.1, (now - eng.StartUtc).TotalSeconds);
        var va = victim.Attributes;
        var hpNow = va is null ? 0f : Math.Max(0f, va[Stats.CurrentHealth]);
        var maxHp = va?[Stats.MaximumHealth] ?? 1f;
        var breakdown = string.Join(", ", eng.DamageByAttacker.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}:{kv.Value}"));

        victim.Logger.LogInformation(
            "[MOBA-TRADE] {Victim} {Ending} after {Dur:F1}s | took {Taken} ({Dps:F0} dps) | HP {HpStart:F0}->{HpNow:F0}/{MaxHp:F0} ({PctLeft:P0} left) | from [{Breakdown}]",
            victim.Name,
            ending,
            dur,
            taken,
            taken / dur,
            eng.HpAtStart,
            hpNow,
            maxHp,
            hpNow / Math.Max(1f, maxHp),
            breakdown);
    }

    /// <summary>Records one landed hit on a champion. Call from the combat trace.</summary>
    /// <param name="attacker">The attacker.</param>
    /// <param name="victim">The victim champion.</param>
    /// <param name="skill">The skill, or <c>null</c> for a basic attack.</param>
    /// <param name="hit">The resulting hit.</param>
    /// <param name="isCombo">Whether this hit was part of a combo.</param>
    public static void NoteHit(IAttacker attacker, Player victim, Skill? skill, HitInfo hit, bool isCombo)
    {
        try
        {
            var total = (long)hit.HealthDamage + hit.ShieldDamage;
            if (total <= 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var attackerName = SafeName(attacker) ?? "?";
            var skillTag = skill is null ? "basic" : $"{skill.Name}#{skill.Number}";

            // --- running totals ---
            if (attacker is Player { IsMobaClone: true } atkChamp)
            {
                var s = StatsTable.GetOrCreateValue(atkChamp);
                s.DamageDealt += total;
                s.Hits++;
                if ((hit.Attributes & DamageAttributes.Critical) != 0)
                {
                    s.Crits++;
                }

                s.PerSkillDealt.TryGetValue(skillTag, out var prev);
                s.PerSkillDealt[skillTag] = prev + total;
            }

            var vs = StatsTable.GetOrCreateValue(victim);
            vs.DamageTaken += total;

            // --- engagement + burst ---
            var va = victim.Attributes;
            var maxHp = va?[Stats.MaximumHealth] ?? 1f;
            var eng = Engagements.GetValue(victim, _ => new Engagement { StartUtc = now, LastHitUtc = now, HpAtStart = va?[Stats.CurrentHealth] ?? maxHp });
            if (now - eng.LastHitUtc > EngagementIdle)
            {
                // stale - close it out ([MOBA-TRADE]) then start a fresh engagement
                FlushEngagement(victim, eng, "disengage");
                eng.StartUtc = now;
                eng.HpAtStart = va?[Stats.CurrentHealth] ?? maxHp;
                eng.DamageByAttacker.Clear();
                eng.Recent.Clear();
                eng.FightLineLogged = false;
                eng.Flushed = false;
            }

            eng.LastHitUtc = now;
            eng.DamageByAttacker.TryGetValue(attackerName, out var byAtk);
            eng.DamageByAttacker[attackerName] = byAtk + total;
            eng.Recent.Add((now, total));
            eng.Recent.RemoveAll(r => now - r.When > BurstWindow);
            var burstSum = eng.Recent.Sum(r => r.Amount);

            if (!eng.FightLineLogged && attacker is Player)
            {
                eng.FightLineLogged = true;
                victim.Logger.LogInformation(
                    "[MOBA-FIGHT] start {Attacker} vs {Victim} @ {Vx},{Vy} (victim {Hp:F0}/{MaxHp:F0} HP)",
                    attackerName,
                    victim.Name,
                    victim.Position.X,
                    victim.Position.Y,
                    va?[Stats.CurrentHealth] ?? 0f,
                    maxHp);
            }

            if (burstSum >= maxHp * BurstFraction)
            {
                vs.BurstEvents++;
                victim.Logger.LogInformation(
                    "[MOBA-BURST] {Victim} took {Burst:F0} ({Pct:P0} of max HP) in <{Win}s - last: {Attacker} {Skill}",
                    victim.Name,
                    burstSum,
                    burstSum / Math.Max(1f, maxHp),
                    (int)BurstWindow.TotalSeconds,
                    attackerName,
                    skillTag);
            }

            // --- enriched per-hit line ---
            var hpAfter = va?[Stats.CurrentHealth] ?? 0f;
            var sdAfter = va?[Stats.CurrentShield] ?? 0f;
            var pctOfMax = total / Math.Max(1f, maxHp);
            var dist = DistanceBetween(attacker, victim);

            string mitTag = string.Empty;
            if (MobaDefense.LastMitigation.TryGetValue(victim, out var mt) && (now - mt.WhenUtc) < TimeSpan.FromMilliseconds(250))
            {
                mitTag = $" raw={mt.Raw} mit={mt.Fraction:P0}";
            }

            var lvlTag = LevelTag(attacker, victim);
            victim.Logger.LogInformation(
                "[MOBA-DMG+] {Attacker} -> {Victim} | {Skill}{Combo} | hp={HpDmg} sd={SdDmg} ({Pct:P0} maxHP){Mit} | victim {HpAfter:F0}/{MaxHp:F0}+{SdAfter:F0}sd | dist={Dist:F1} | {Lvl}",
                attackerName,
                victim.Name,
                skillTag,
                isCombo ? " [combo]" : string.Empty,
                hit.HealthDamage,
                hit.ShieldDamage,
                pctOfMax,
                mitTag,
                hpAfter,
                maxHp,
                sdAfter,
                dist,
                lvlTag);
        }
        catch
        {
            // telemetry must never break combat
        }
    }

    /// <summary>Records a champion death: closes its engagement and writes the [MOBA-KILL] summary.</summary>
    /// <param name="victim">The champion that died.</param>
    /// <param name="killerName">Best-known killer name.</param>
    public static void NoteDeath(Player victim, string? killerName)
    {
        try
        {
            var now = DateTime.UtcNow;
            var va = victim.Attributes;
            var maxHp = va?[Stats.MaximumHealth] ?? 1f;

            string breakdown;
            double ttk;
            long totalTaken;
            if (Engagements.TryGetValue(victim, out var eng))
            {
                ttk = (now - eng.StartUtc).TotalSeconds;
                totalTaken = eng.DamageByAttacker.Values.Sum();
                breakdown = string.Join(", ", eng.DamageByAttacker
                    .OrderByDescending(kv => kv.Value)
                    .Select(kv => $"{kv.Key}:{kv.Value} ({kv.Value / (double)Math.Max(1, totalTaken):P0})"));
                var overkill = Math.Max(0, eng.Recent.Where(r => now - r.When < TimeSpan.FromSeconds(1)).Sum(r => r.Amount) - (long)Math.Max(0f, eng.HpAtStart));
                breakdown += $" | overkill~{overkill}";
                FlushEngagement(victim, eng, "died");
                Engagements.Remove(victim);
            }
            else
            {
                ttk = 0;
                totalTaken = 0;
                breakdown = "(no engagement tracked)";
            }

            victim.Logger.LogInformation(
                "[MOBA-KILL] {Victim} (Lv{Lvl} {Class}, {K}/{D}/{A}) killed by {Killer} @ {X},{Y} | ttk={Ttk:F1}s dmgTaken={Taken} maxHP={MaxHp:F0} | {Breakdown}",
                victim.Name,
                victim.MobaLevel,
                victim.SelectedCharacter?.CharacterClass?.Name,
                victim.MobaKills,
                victim.MobaDeaths,
                victim.MobaAssists,
                killerName ?? "?",
                victim.Position.X,
                victim.Position.Y,
                ttk,
                totalTaken,
                maxHp,
                breakdown);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Records a crowd-control application (before/after the MOBA cap).</summary>
    /// <param name="source">The CC source.</param>
    /// <param name="target">The CC target.</param>
    /// <param name="kind">Freeze / stun / sleep / root.</param>
    /// <param name="requested">The duration the effect asked for.</param>
    /// <param name="applied">The duration actually applied after cap + tenacity + DR.</param>
    /// <param name="drStack">The consecutive-CC diminishing-returns stack.</param>
    /// <param name="tenacity">The target family's tenacity multiplier.</param>
    public static void NoteCc(object? source, Player target, string kind, TimeSpan requested, TimeSpan applied, int drStack, double tenacity)
    {
        try
        {
            target.Logger.LogInformation(
                "[MOBA-CC] {Source} -> {Target} | {Kind} req={Req}ms applied={App}ms (DR x{Dr}, tenacity {Ten:F2}) @ {X},{Y}",
                SafeName(source as IAttacker) ?? (source?.ToString() ?? "?"),
                target.Name,
                kind,
                (int)requested.TotalMilliseconds,
                (int)applied.TotalMilliseconds,
                drStack,
                tenacity,
                target.Position.X,
                target.Position.Y);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Records a heal / lifesteal / shield gain on a champion.</summary>
    /// <param name="champion">The champion that gained HP / shield.</param>
    /// <param name="amount">The amount.</param>
    /// <param name="source">Where it came from (skill name, "lifesteal", "shield", ...).</param>
    public static void NoteHeal(Player champion, double amount, string source)
    {
        try
        {
            if (amount < 1)
            {
                return;
            }

            StatsTable.GetOrCreateValue(champion).Healed += (long)amount;
            champion.Logger.LogInformation(
                "[MOBA-HEAL] {Champion} +{Amount:F0} ({Source}) -> {Hp:F0}/{MaxHp:F0}",
                champion.Name,
                amount,
                source,
                champion.Attributes?[Stats.CurrentHealth] ?? 0f,
                champion.Attributes?[Stats.MaximumHealth] ?? 0f);
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Writes the periodic combat sheets. Call from the match tick; it self-throttles.</summary>
    /// <param name="gameContext">The game context.</param>
    public static async ValueTask TickAsync(IGameContext gameContext)
    {
        try
        {
            var now = DateTime.UtcNow;
            if (now - _lastPeriodicUtc < PeriodicInterval)
            {
                return;
            }

            _lastPeriodicUtc = now;

            var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
            var champions = players.Where(p => p.IsMobaClone && MobaTeams.GetTeam(p) != MobaTeam.None).ToList();
            if (champions.Count == 0)
            {
                return;
            }

            var elapsed = (int)MobaMatchTickPlugIn.MatchElapsed.TotalSeconds;
            GameMap? map = null;

            foreach (var c in champions.OrderBy(c => (int)MobaTeams.GetTeam(c)).ThenByDescending(c => c.MobaLevel))
            {
                map ??= c.CurrentMap;
                var a = c.Attributes;
                if (a is null)
                {
                    continue;
                }

                // Close out any engagement that has gone quiet (writes [MOBA-TRADE]).
                if (Engagements.TryGetValue(c, out var quietEng) && DateTime.UtcNow - quietEng.LastHitUtc > EngagementIdle)
                {
                    FlushEngagement(c, quietEng, "disengage");
                }

                var family = MobaPassives.FamilyOf(c);
                var hp = Math.Max(0f, a[Stats.CurrentHealth]);
                var maxHp = a[Stats.MaximumHealth];
                var sd = Math.Max(0f, a[Stats.CurrentShield]);
                var mit = MobaDefense.MitigationOf(c);
                var ehp = (hp + sd) / Math.Max(0.05, 1.0 - mit);
                var crit = MobaCombatStats.CritChanceOf(c);
                var ms = a[Stats.MovementSpeed];
                var range = MobaCombatStats.AttackRangeOf(family);

                var s = StatsTable.GetOrCreateValue(c);
                var alive = Math.Max(1.0, (now - s.FirstSeenUtc).TotalSeconds);
                var dps = s.DamageDealt / alive;
                var critRate = s.Hits > 0 ? s.Crits / (double)s.Hits : 0.0;
                var topSkills = string.Join(", ", s.PerSkillDealt.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key}:{kv.Value}"));

                c.Logger.LogInformation(
                    "[MOBA-CHAMP] t={T}s {Team} {Class} {Name} Lv{Lvl} | HP {Hp:F0}/{MaxHp:F0} +{Sd:F0}sd EHP~{Ehp:F0} | mana {Mana:F0}/{MaxMana:F0} | mit {Mit:P0} crit {Crit:P0}(obs {CritObs:P0}) rng {Rng} ms {Ms:F0} | dealt {Dealt} taken {Taken} healed {Healed} DPS {Dps:F0} burst{Burst} | top[{Top}]",
                    elapsed,
                    MobaTeams.GetTeam(c),
                    c.SelectedCharacter?.CharacterClass?.Name,
                    c.Name,
                    c.MobaLevel,
                    hp,
                    maxHp,
                    sd,
                    ehp,
                    Math.Max(0f, a[Stats.CurrentMana]),
                    a[Stats.MaximumMana],
                    mit,
                    crit,
                    critRate,
                    range,
                    ms,
                    s.DamageDealt,
                    s.DamageTaken,
                    s.Healed,
                    dps,
                    s.BurstEvents,
                    topSkills);
            }

            // --- per-team economy + snowball gap ---
            foreach (var teamGroup in champions.GroupBy(c => MobaTeams.GetTeam(c)))
            {
                var list = teamGroup.ToList();
                var sumLvl = list.Sum(c => c.MobaLevel);
                var k = list.Sum(c => c.MobaKills);
                var d = list.Sum(c => c.MobaDeaths);
                var assist = list.Sum(c => c.MobaAssists);
                var avgEhp = list.Average(c =>
                {
                    var a = c.Attributes;
                    if (a is null)
                    {
                        return 0.0;
                    }

                    var mit = MobaDefense.MitigationOf(c);
                    return (Math.Max(0f, a[Stats.CurrentHealth]) + Math.Max(0f, a[Stats.CurrentShield])) / Math.Max(0.05, 1.0 - mit);
                });
                var aliveCount = list.Count(c => c.IsAlive);

                list[0].Logger.LogInformation(
                    "[MOBA-ECON] t={T}s {Team} sumLv={SumLv} avgLv={AvgLv:F1} KDA={K}/{D}/{A} alive={Alive}/{Total} avgEHP~{AvgEhp:F0}",
                    elapsed,
                    teamGroup.Key,
                    sumLvl,
                    list.Average(c => c.MobaLevel),
                    k,
                    d,
                    assist,
                    aliveCount,
                    list.Count,
                    avgEhp);
            }

            var blue = champions.Where(c => MobaTeams.GetTeam(c) == MobaTeam.Blue).ToList();
            var red = champions.Where(c => MobaTeams.GetTeam(c) == MobaTeam.Red).ToList();
            if (blue.Count > 0 && red.Count > 0)
            {
                var gap = blue.Average(c => c.MobaLevel) - red.Average(c => c.MobaLevel);
                champions[0].Logger.LogInformation(
                    "[MOBA-ECON] t={T}s LEVEL-GAP Blue-Red = {Gap:+0.0;-0.0;0.0} (Blue avg {B:F1}, Red avg {R:F1})",
                    elapsed,
                    gap,
                    blue.Average(c => c.MobaLevel),
                    red.Average(c => c.MobaLevel));
            }

            if (map is not null)
            {
                LogWavesAndStructures(map, champions[0].Logger, elapsed);
            }
        }
        catch
        {
            // best effort
        }
    }

    /// <summary>Writes one [MOBA-STRUCT] destruction line. Call from the structure's Died handler.</summary>
    /// <param name="structureName">A readable structure name.</param>
    /// <param name="team">The owning team.</param>
    /// <param name="logger">A logger.</param>
    /// <param name="killerName">Who landed the kill, if known.</param>
    public static void NoteStructureDown(string structureName, MobaTeam team, ILogger logger, string? killerName)
    {
        try
        {
            logger.LogInformation("[MOBA-STRUCT] DOWN {Team} {Structure} (last hit: {Killer})", team, structureName, killerName ?? "?");
        }
        catch
        {
            // best effort
        }
    }

    private static void LogWavesAndStructures(GameMap map, ILogger logger, int elapsed)
    {
        var monsters = map.GetAttackablesInRange(new Point(128, 128), 400).OfType<NPC.Monster>().ToList();

        foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
        {
            var creeps = monsters.Where(m => !MobaStructures.IsStructure(m) && m.IsAlive && MobaTeams.GetTeam(m) == team).ToList();
            if (creeps.Count == 0)
            {
                logger.LogInformation("[MOBA-WAVE] t={T}s {Team} creeps=0", elapsed, team);
                continue;
            }

            // Lane frontier = how far the team's creeps have pushed toward the enemy (Blue pushes to higher Y).
            var frontier = team == MobaTeam.Blue ? creeps.Max(m => m.Position.Y) : creeps.Min(m => m.Position.Y);
            var avgY = creeps.Average(m => m.Position.Y);
            logger.LogInformation(
                "[MOBA-WAVE] t={T}s {Team} creeps={Count} frontierY={Frontier} avgY={AvgY:F0}",
                elapsed,
                team,
                creeps.Count,
                frontier,
                avgY);
        }

        foreach (var s in monsters.Where(m => MobaStructures.IsStructure(m)))
        {
            var a = s.Attributes;
            var hp = a is null ? 0f : Math.Max(0f, a[Stats.CurrentHealth]);
            var maxHp = a?[Stats.MaximumHealth] ?? 1f;
            logger.LogInformation(
                "[MOBA-STRUCT] t={T}s {Team} {Type} @ {X},{Y} HP {Hp:F0}/{MaxHp:F0} ({Pct:P0}) {Alive}",
                elapsed,
                MobaTeams.GetTeam(s),
                MobaStructures.GetStructureType(s),
                s.Position.X,
                s.Position.Y,
                hp,
                maxHp,
                hp / Math.Max(1f, maxHp),
                s.IsAlive ? "alive" : "DEAD");
        }
    }

    private static double DistanceBetween(IAttacker attacker, Player victim)
    {
        try
        {
            if (attacker is ILocateable loc)
            {
                var dx = loc.Position.X - victim.Position.X;
                var dy = loc.Position.Y - victim.Position.Y;
                return Math.Sqrt((dx * dx) + (dy * dy));
            }
        }
        catch
        {
            // ignore
        }

        return -1;
    }

    private static string LevelTag(IAttacker attacker, Player victim)
    {
        var atk = attacker as Player;
        var atkLvl = atk is { IsMobaClone: true } ? $"Lv{atk.MobaLevel}" : "-";
        var atkClass = atk?.SelectedCharacter?.CharacterClass?.Name ?? attacker.GetType().Name;
        return $"{atkClass} {atkLvl} vs {victim.SelectedCharacter?.CharacterClass?.Name} Lv{victim.MobaLevel}";
    }

    private static string? SafeName(IAttacker? attacker)
    {
        if (attacker is null)
        {
            return null;
        }

        try
        {
            return attacker.GetName();
        }
        catch
        {
            return attacker.GetType().Name;
        }
    }

}
