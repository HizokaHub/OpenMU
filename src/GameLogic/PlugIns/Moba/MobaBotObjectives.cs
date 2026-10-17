// <copyright file="MobaBotObjectives.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>The role of a bot in the team.</summary>
public enum MobaBotRole
{
    /// <summary>Plays a lane.</summary>
    Lane,

    /// <summary>Clears the jungle, helps the lanes (ganks) and calls the jungle objectives.</summary>
    Jungle,
}

/// <summary>The jungle objectives the bot teams go for together.</summary>
public enum MobaObjectiveKind
{
    /// <summary>Medusa (mini-boss for 2).</summary>
    Medusa,

    /// <summary>Dark Phoenix (mini-boss for 2).</summary>
    DarkPhoenix,

    /// <summary>God of Darkness (boss for 3).</summary>
    GodOfDarkness,

    /// <summary>Kundun (boss for the team).</summary>
    Kundun,

    /// <summary>The eggs of Selupan and then Selupan himself (the team).</summary>
    Selupan,
}

/// <summary>One objective that is up right now.</summary>
public sealed class MobaObjective
{
    /// <summary>Gets the kind.</summary>
    public MobaObjectiveKind Kind { get; init; }

    /// <summary>Gets a key that identifies this objective (kind and place).</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Gets the place of the objective.</summary>
    public Point Position { get; init; }

    /// <summary>Gets the main monsters (the boss, or the eggs).</summary>
    public IReadOnlyList<Monster> Main { get; init; } = Array.Empty<Monster>();

    /// <summary>Gets the monsters that also fight there (hatched spiders).</summary>
    public IReadOnlyList<Monster> Extra { get; init; } = Array.Empty<Monster>();

    /// <summary>Gets how many champions are needed.</summary>
    public int Required { get; init; }

    /// <summary>Gets the average team level the bots want before they go for it.</summary>
    public int MinLevel { get; init; }

    /// <summary>Gets how much of the objective is left (1 = untouched, 0 = done).</summary>
    public double Fraction { get; init; } = 1.0;
}

/// <summary>A team's plan to take an objective: who goes, where they gather and whether the fight has begun.</summary>
public sealed class MobaObjectiveCall
{
    private readonly HashSet<MobaBotPlayer> _arrived = new();

    /// <summary>Initializes a new instance of the <see cref="MobaObjectiveCall"/> class.</summary>
    /// <param name="team">The team.</param>
    /// <param name="objective">The objective.</param>
    /// <param name="rally">Where the team gathers.</param>
    /// <param name="participants">The bots that go.</param>
    /// <param name="contest">Whether it answers an enemy attempt (no gathering, straight in).</param>
    public MobaObjectiveCall(MobaTeam team, MobaObjective objective, Point rally, IReadOnlyList<MobaBotPlayer> participants, bool contest)
    {
        this.Team = team;
        this.Objective = objective;
        this.Rally = rally;
        this.Participants = participants.ToList();
        this.Contest = contest;
        this.Started = DateTime.UtcNow;
        this.Engaged = contest;
    }

    /// <summary>Gets the team.</summary>
    public MobaTeam Team { get; }

    /// <summary>Gets or sets the objective (refreshed every coordinator tick).</summary>
    public MobaObjective Objective { get; set; }

    /// <summary>Gets the gathering point.</summary>
    public Point Rally { get; }

    /// <summary>Gets the bots that go.</summary>
    public List<MobaBotPlayer> Participants { get; }

    /// <summary>Gets a value indicating whether this call answers an enemy attempt.</summary>
    public bool Contest { get; }

    /// <summary>Gets when the call was made.</summary>
    public DateTime Started { get; }

    /// <summary>Gets or sets a value indicating whether the team already attacks.</summary>
    public bool Engaged { get; set; }

    /// <summary>Gets the number of bots that reached the gathering point.</summary>
    public int ArrivedCount
    {
        get
        {
            lock (this._arrived)
            {
                return this._arrived.Count;
            }
        }
    }

    /// <summary>Notes that a bot reached the gathering point.</summary>
    /// <param name="bot">The bot.</param>
    public void MarkArrived(MobaBotPlayer bot)
    {
        lock (this._arrived)
        {
            this._arrived.Add(bot);
        }
    }

    /// <summary>Forgets the arrivals that are no longer at the gathering point.</summary>
    /// <param name="stillThere">Tells whether a bot is still there.</param>
    public void Refresh(Func<MobaBotPlayer, bool> stillThere)
    {
        lock (this._arrived)
        {
            this._arrived.RemoveWhere(b => !stillThere(b));
        }
    }
}

/// <summary>
/// The macro play of the bot teams around the jungle objectives (2026-10-17). Every ~2 s each team looks at the objectives that are up (Medusa, Dark Phoenix,
/// God of Darkness, Kundun, Selupan) and, when its level and its healthy bots near enough to the place allow it, calls one: the bots gather at a rally point
/// a few tiles short of the objective and attack together once most of them are there. A bot that is too far away just stays out ("only the near ones help").
/// The other team notices when the objective is being attacked (or its champions stand next to it) and answers the same way: it sends the near bots straight in
/// to defend or to steal it.
/// </summary>
public static class MobaBotObjectives
{
    /// <summary>Seconds between two looks at the objectives.</summary>
    public const double TickSeconds = 2;

    /// <summary>Tiles a bot may be from the objective to be called for it.</summary>
    public const double MaxTravelTiles = 130;

    /// <summary>Tiles a bot may be from the objective to be sent to contest it.</summary>
    public const double MaxContestTiles = 90;

    /// <summary>Minimum life fraction of a bot that is called.</summary>
    public const float MinHealth = 0.55f;

    /// <summary>A call is dropped after this long (seconds).</summary>
    public const double MaxCallSeconds = 200;

    /// <summary>Seconds after which the team goes in even if some bots have not arrived.</summary>
    public const double MaxGatherSeconds = 50;

    /// <summary>Tiles from the rally point that count as "arrived".</summary>
    public const double ArrivedTiles = 10;

    /// <summary>Tiles around the objective in which an enemy champion counts as "attacking it".</summary>
    public const double ScoutTiles = 25;

    private static readonly object Gate = new();

    private static readonly Dictionary<MobaTeam, MobaObjectiveCall> Calls = new();

    private static readonly Dictionary<string, DateTime> Cooldowns = new();

    private static DateTime _nextTickUtc;

    private static IReadOnlyList<MobaObjective> _objectives = Array.Empty<MobaObjective>();

    /// <summary>The data of a bot that could be called.</summary>
    /// <param name="Id">An identifier of the bot.</param>
    /// <param name="Distance">Tiles to the objective.</param>
    /// <param name="HpPct">Life fraction.</param>
    /// <param name="IsJungler">Whether it is the jungler (always first).</param>
    public readonly record struct Candidate(int Id, double Distance, float HpPct, bool IsJungler);

    /// <summary>Gets the objectives that were up at the last look.</summary>
    public static IReadOnlyList<MobaObjective> Objectives => _objectives;

    /// <summary>Chooses the bots of a call: the jungler first, then the nearest; none when fewer than <paramref name="need"/> qualify.</summary>
    /// <param name="candidates">The bots of the team.</param>
    /// <param name="need">How many are needed at least.</param>
    /// <param name="take">How many go at most.</param>
    /// <param name="maxDistance">The farthest a bot may be.</param>
    /// <param name="minHealth">The least life fraction a bot may have.</param>
    /// <returns>The ids of the bots that go.</returns>
    public static IReadOnlyList<int> ChooseParticipants(IEnumerable<Candidate> candidates, int need, int take, double maxDistance, float minHealth)
    {
        var eligible = candidates
            .Where(c => c.Distance <= maxDistance && c.HpPct >= minHealth)
            .OrderByDescending(c => c.IsJungler)
            .ThenBy(c => c.Distance)
            .ToList();
        return eligible.Count < need ? Array.Empty<int>() : eligible.Take(Math.Max(need, take)).Select(c => c.Id).ToList();
    }

    /// <summary>The minimum average level and number of champions for an objective kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The level and the champions.</returns>
    public static (int MinLevel, int Required) RequirementsOf(MobaObjectiveKind kind) => kind switch
    {
        MobaObjectiveKind.Medusa or MobaObjectiveKind.DarkPhoenix => (4, 2),
        MobaObjectiveKind.GodOfDarkness => (6, 3),
        MobaObjectiveKind.Kundun => (8, 4),
        _ => (9, 4),
    };

    /// <summary>Gets the call a bot has to follow, or <see langword="null"/>.</summary>
    /// <param name="bot">The bot.</param>
    /// <returns>The call.</returns>
    public static MobaObjectiveCall? AssignmentOf(MobaBotPlayer bot)
    {
        lock (Gate)
        {
            return Calls.Values.FirstOrDefault(c => c.Participants.Contains(bot));
        }
    }

    /// <summary>The monster of an objective a bot should hit now: the nearest main one, else the nearest extra one.</summary>
    /// <param name="call">The call.</param>
    /// <param name="from">Where the bot is.</param>
    /// <returns>The target.</returns>
    public static Monster? PickTarget(MobaObjectiveCall call, Point from)
    {
        var objective = call.Objective;
        return objective.Main.Where(m => m.IsAlive).OrderBy(m => m.GetDistanceTo(from)).FirstOrDefault()
               ?? objective.Extra.Where(m => m.IsAlive).OrderBy(m => m.GetDistanceTo(from)).FirstOrDefault();
    }

    /// <summary>Forgets every call (the match ended or the bots were removed).</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            Calls.Clear();
            Cooldowns.Clear();
            _objectives = Array.Empty<MobaObjective>();
        }
    }

    /// <summary>Looks at the objectives and updates the calls of both teams (at most every <see cref="TickSeconds"/>).</summary>
    /// <param name="map">The arena map.</param>
    public static void Tick(GameMap map)
    {
        lock (Gate)
        {
            var now = DateTime.UtcNow;
            if (now < _nextTickUtc)
            {
                return;
            }

            _nextTickUtc = now + TimeSpan.FromSeconds(TickSeconds);
            try
            {
                var objectives = BuildObjectives(MobaJungle.LiveMonsters(map.MapId));
                _objectives = objectives;
                var bots = MobaBotPlayer.All.Where(b => !b.IsDummy && b.IsMobaClone && b.IsAlive && b.CurrentMap == map).ToList();
                foreach (var team in new[] { MobaTeam.Blue, MobaTeam.Red })
                {
                    Validate(team, objectives, bots, now);
                    if (!Calls.ContainsKey(team))
                    {
                        TryCall(map, team, objectives, bots, now);
                    }
                }
            }
            catch
            {
                // the coordinator must never break the bots
            }
        }
    }

    private static List<MobaObjective> BuildObjectives(IReadOnlyList<(Monster Monster, MobaJungleIntelligence Ai)> live)
    {
        var result = new List<MobaObjective>();
        foreach (var (monster, ai) in live)
        {
            var kind = ai.Kind switch
            {
                MobaJungle.Kind.Medusa => (MobaObjectiveKind?)MobaObjectiveKind.Medusa,
                MobaJungle.Kind.DarkPhoenix => MobaObjectiveKind.DarkPhoenix,
                MobaJungle.Kind.GodOfDarkness => MobaObjectiveKind.GodOfDarkness,
                MobaJungle.Kind.Kundun => MobaObjectiveKind.Kundun,
                _ => null,
            };
            if (kind is null)
            {
                continue;
            }

            var (minLevel, required) = RequirementsOf(kind.Value);
            result.Add(new MobaObjective
            {
                Kind = kind.Value,
                Key = $"{kind}@{ai.Home.X},{ai.Home.Y}",
                Position = ai.Home,
                Main = new[] { monster },
                Required = required,
                MinLevel = minLevel,
                Fraction = Fraction(monster),
            });
        }

        var selupan = live.Where(l => l.Ai.Kind == MobaJungle.Kind.Selupan).Select(l => l.Monster).ToList();
        var eggs = live.Where(l => l.Ai.Kind is MobaJungle.Kind.SpiderEgg1 or MobaJungle.Kind.SpiderEgg2 or MobaJungle.Kind.SpiderEgg3).Select(l => l.Monster).ToList();
        var spiders = live.Where(l => l.Ai.Kind == MobaJungle.Kind.Spider).Select(l => l.Monster).ToList();
        if (selupan.Count > 0 || eggs.Count > 0)
        {
            var main = selupan.Count > 0 ? selupan : eggs;
            var (minLevel, required) = RequirementsOf(MobaObjectiveKind.Selupan);
            if (selupan.Count == 0)
            {
                // The eggs are a lighter job than the boss.
                (minLevel, required) = (minLevel - 1, required - 1);
            }

            var x = (int)main.Average(m => m.Position.X);
            var y = (int)main.Average(m => m.Position.Y);
            result.Add(new MobaObjective
            {
                Kind = MobaObjectiveKind.Selupan,
                Key = selupan.Count > 0 ? "Selupan" : "SelupanEggs",
                Position = MobaLayout.NearestWalkable(x, y),
                Main = main,
                Extra = spiders,
                Required = required,
                MinLevel = minLevel,
                Fraction = selupan.Count > 0 ? Fraction(selupan[0]) : Math.Clamp(eggs.Count / 8.0, 0, 1),
            });
        }

        return result;
    }

    private static double Fraction(Monster monster)
    {
        var max = monster.Attributes[GameLogic.Attributes.Stats.MaximumHealth];
        return max <= 0 ? 1.0 : Math.Clamp(monster.Attributes[GameLogic.Attributes.Stats.CurrentHealth] / max, 0.0, 1.0);
    }

    private static MobaTeam Enemy(MobaTeam team) => team == MobaTeam.Blue ? MobaTeam.Red : MobaTeam.Blue;

    private static void Validate(MobaTeam team, IReadOnlyList<MobaObjective> objectives, IReadOnlyList<MobaBotPlayer> bots, DateTime now)
    {
        if (!Calls.TryGetValue(team, out var call))
        {
            return;
        }

        var current = objectives.FirstOrDefault(o => o.Key == call.Objective.Key);
        call.Participants.RemoveAll(p => !bots.Contains(p));
        string? endReason = null;
        if (current is null)
        {
            endReason = "objective gone";
            Cooldowns[call.Objective.Key] = now + TimeSpan.FromSeconds(20);
        }
        else if ((now - call.Started).TotalSeconds > MaxCallSeconds)
        {
            endReason = "timeout";
            Cooldowns[call.Objective.Key] = now + TimeSpan.FromSeconds(60);
        }
        else if (call.Participants.Count < Math.Min(2, call.Objective.Required))
        {
            endReason = "not enough bots left";
            Cooldowns[call.Objective.Key] = now + TimeSpan.FromSeconds(40);
        }

        if (endReason is not null)
        {
            Calls.Remove(team);
            Log(bots, "[MOBA-OBJ] {Team} ends {Key}: {Reason}.", team, call.Objective.Key, endReason);
            return;
        }

        call.Objective = current!;
        if (!call.Engaged)
        {
            call.Refresh(b => b.IsAlive && b.Position.EuclideanDistanceTo(call.Rally) <= ArrivedTiles + 4);
            var arrived = call.ArrivedCount;
            var elapsed = (now - call.Started).TotalSeconds;
            if (arrived >= Math.Ceiling(call.Participants.Count * 0.75) || (elapsed > MaxGatherSeconds && arrived >= 2))
            {
                call.Engaged = true;
                Log(bots, "[MOBA-OBJ] {Team} attacks {Key} with {N} bot(s) ({Arrived} gathered after {Sec:F0}s).", team, call.Objective.Key, call.Participants.Count, arrived, elapsed);
            }
        }
    }

    private static void TryCall(GameMap map, MobaTeam team, IReadOnlyList<MobaObjective> objectives, IReadOnlyList<MobaBotPlayer> bots, DateTime now)
    {
        var mine = bots.Where(b => MobaTeams.GetTeam(b) == team).ToList();
        if (mine.Count < 2 || objectives.Count == 0)
        {
            return;
        }

        var enemy = Enemy(team);
        var averageLevel = mine.Average(b => b.MobaLevel);
        var ownNexus = MobaLayout.NexusOf(team);
        MobaObjective? best = null;
        var bestScore = double.MinValue;
        var bestContest = false;
        IReadOnlyList<int> bestIds = Array.Empty<int>();

        foreach (var objective in objectives)
        {
            if (Cooldowns.TryGetValue(objective.Key, out var until) && now < until)
            {
                continue;
            }

            // Do the enemies already go for it? Their call has begun, or the objective is hurt and their champions stand around it.
            var enemyCall = Calls.TryGetValue(enemy, out var ec) && ec.Objective.Key == objective.Key && ec.Engaged;
            var enemiesThere = objective.Fraction < 0.98
                && map.GetAttackablesInRange(objective.Position, (int)ScoutTiles).OfType<Player>().Any(p => p.IsMobaClone && p.IsAlive && MobaTeams.GetTeam(p) == enemy);
            var contest = enemyCall || enemiesThere;
            if (!contest && averageLevel < objective.MinLevel)
            {
                continue;
            }

            var candidates = mine.Select((b, i) => new Candidate(i, b.Position.EuclideanDistanceTo(objective.Position), HpOf(b), b.Role == MobaBotRole.Jungle)).ToList();
            var need = contest ? Math.Min(2, objective.Required) : objective.Required;
            var ids = ChooseParticipants(candidates, need, objective.Required + 1, contest ? MaxContestTiles : MaxTravelTiles, MinHealth);
            if (ids.Count == 0)
            {
                continue;
            }

            var nearest = ids.Min(i => candidates[i].Distance);
            var ownSide = (team == MobaTeam.Blue) == (objective.Position.Y < 128);
            var score = (contest ? 100 : 0) + (ownSide ? 40 : 0) + (objective.Required * 3) - (nearest * 0.25);
            if (score > bestScore)
            {
                best = objective;
                bestScore = score;
                bestContest = contest;
                bestIds = ids;
            }
        }

        if (best is null)
        {
            return;
        }

        var participants = bestIds.Select(i => mine[i]).ToList();
        var rally = bestContest ? best.Position : RallyPoint(best.Position, ownNexus, 9);
        Calls[team] = new MobaObjectiveCall(team, best, rally, participants, bestContest);
        Log(
            bots,
            "[MOBA-OBJ] {Team} {Kind} {Key} ({Fraction:P0} left, avg level {Lv:F1}): {Names} {Mode} at ({X},{Y}).",
            team,
            bestContest ? "CONTESTS" : "calls",
            best.Key,
            best.Fraction,
            averageLevel,
            string.Join(", ", participants.Select(p => p.Name + (p.Role == MobaBotRole.Jungle ? "(jungle)" : string.Empty))),
            bestContest ? "go straight in" : "gather",
            rally.X,
            rally.Y);
    }

    private static float HpOf(Player player)
        => player.Attributes is { } a ? Math.Clamp(a[GameLogic.Attributes.Stats.CurrentHealth] / Math.Max(1f, a[GameLogic.Attributes.Stats.MaximumHealth]), 0f, 1f) : 1f;

    /// <summary>A walkable point <paramref name="tiles"/> short of the objective on the side of the team's base.</summary>
    private static Point RallyPoint(Point objective, Point ownNexus, int tiles)
    {
        var dx = ownNexus.X - objective.X;
        var dy = ownNexus.Y - objective.Y;
        var length = Math.Max(1.0, Math.Sqrt((dx * dx) + (dy * dy)));
        return MobaLayout.NearestWalkable(objective.X + (dx / length * tiles), objective.Y + (dy / length * tiles));
    }

    private static void Log(IReadOnlyList<MobaBotPlayer> bots, string message, params object?[] args)
    {
        bots.FirstOrDefault()?.Logger.LogInformation(message, args);
    }
}
