// <copyright file="MobaBotPlayer.Jungle.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The jungler role of the bots and their part in the team objectives (2026-10-17).
/// </summary>
public sealed partial class MobaBotPlayer
{
    /// <summary>A jungler below this life fraction goes home to recover (nobody is hunting it, the camps hurt).</summary>
    private const float JungleRecallHpPct = 0.38f;

    /// <summary>A bot needs this much life to follow the team to an objective.</summary>
    private const float ObjectiveMinHp = 0.5f;

    /// <summary>The jungler starts ganking at this level.</summary>
    private const int GankMinLevel = 3;

    /// <summary>The jungler comes to the aid of a lane from up to this many tiles away.</summary>
    private const double GankMaxTiles = 110;

    private static readonly TimeSpan GankBudget = TimeSpan.FromSeconds(25);

    private static readonly TimeSpan GankCooldown = TimeSpan.FromSeconds(45);

    private Monster? _jungleTarget;

    private MobaJungleGroup? _jungleGroup;

    /// <summary>Camp life (sum of the maximum life of its monsters) a level 0 jungler can take; every level adds <see cref="CampLifePerLevel"/>.</summary>
    private const double CampLifeAllowance = 2800;

    private const double CampLifePerLevel = 2500;

    private readonly Dictionary<BotState, double> _jungleStateSeconds = new();

    private DateTime _jungleLastAccountUtc;

    private DateTime _nextJungleSummaryUtc;

    private int _jungleKills;

    private int _jungleLevelAtStart;

    private DateTime _jungleWaitSinceUtc;

    /// <summary>Gets the role of the bot in the team.</summary>
    public MobaBotRole Role => this._role;

    private static bool IsObjectiveKind(MobaJungle.Kind kind)
        => kind is MobaJungle.Kind.Medusa or MobaJungle.Kind.DarkPhoenix or MobaJungle.Kind.GodOfDarkness or MobaJungle.Kind.Kundun
            or MobaJungle.Kind.Selupan or MobaJungle.Kind.SpiderEgg1 or MobaJungle.Kind.SpiderEgg2 or MobaJungle.Kind.SpiderEgg3 or MobaJungle.Kind.Spider;

    /// <summary>Gathers at the rally point and, when the team attacks, hits the objective.</summary>
    private async ValueTask TickObjectiveAsync(BotContext c)
    {
        var call = this._objective;
        if (call is null || MobaBotObjectives.AssignmentOf(this) != call)
        {
            this._objective = null;
            this._state = BotState.Lane;
            await this.TickLaneAsync(c).ConfigureAwait(false);
            return;
        }

        if (!call.Engaged)
        {
            if (c.Pos.EuclideanDistanceTo(call.Rally) > MobaBotObjectives.ArrivedTiles - 4)
            {
                await this.WalkTowardAsync(call.Rally).ConfigureAwait(false);
            }
            else
            {
                call.MarkArrived(this);
            }

            return;
        }

        var target = MobaBotObjectives.PickTarget(call, c.Pos);
        if (target is null)
        {
            this._objective = null;
            this._state = BotState.Lane;
            await this.TickLaneAsync(c).ConfigureAwait(false);
            return;
        }

        await this.EngageAsync(c, target).ConfigureAwait(false);
    }

    /// <summary>
    /// The jungler's day: it picks a camp (the nearest one that its level can take, own half first) and stays on it until every monster of the
    /// camp is dead, then picks the next; when none is left it waits in the middle of its jungle. It never wanders into a champion fight by itself.
    /// When the lanes need help it gangs (see <c>TryRegroup</c>) and when the team calls an objective it goes.
    /// </summary>
    private async ValueTask TickJungleAsync(BotContext c)
    {
        var team = MobaTeams.GetTeam(this);
        if (await this.TryCaptureAsync(c).ConfigureAwait(false))
        {
            return;
        }

        var live = MobaJungle.LiveMonsters(c.Map.MapId);

        var previous = this._jungleTarget;
        if (previous is not null && !previous.IsAlive)
        {
            this._jungleKills++;
            this.Logger.LogInformation(
                "[MOBA-JGL] {Name} killed {Monster} @ {X},{Y} (kills {Kills}, Lv{Lv}, hp={Hp:P0}).",
                this.Name,
                previous.Definition.Designation,
                previous.Position.X,
                previous.Position.Y,
                this._jungleKills,
                this.MobaLevel,
                c.HpPct);
        }

        var camp = live.Where(l => !IsObjectiveKind(l.Ai.Kind)).ToList();

        // Stay on the camp until every monster of it is dead (a monster that walks home to heal is left alone).
        Monster? target = null;
        if (this._jungleGroup is { } group)
        {
            target = camp.Where(l => ReferenceEquals(l.Ai.Group, group) && !l.Ai.IsReturning)
                .OrderBy(l => l.Monster.GetDistanceTo(c.Pos))
                .Select(l => l.Monster)
                .FirstOrDefault();
        }

        if (target is null)
        {
            // New camp: the nearest of those that the level can take (own half first); when all are too hard, the weakest.
            var allowedLife = CampLifeAllowance + (CampLifePerLevel * this.MobaLevel);
            var candidates = camp.Where(l => !l.Ai.IsReturning)
                .GroupBy(l => l.Ai.Group)
                .Select(g => new
                {
                    Group = g.Key,
                    Life = g.Sum(l => (double)l.Monster.Attributes[GameLogic.Attributes.Stats.MaximumHealth]),
                    Distance = g.Min(l => l.Monster.GetDistanceTo(c.Pos)) + (OwnHalf(team, g.First().Ai.Home) ? 0 : 40),
                    Own = OwnHalf(team, g.First().Ai.Home),
                    Count = g.Count(),
                    Nearest = g.OrderBy(l => l.Monster.GetDistanceTo(c.Pos)).First().Monster,
                })
                .ToList();
            var pick = candidates.Where(x => x.Life <= allowedLife).OrderBy(x => x.Distance).FirstOrDefault()
                       ?? candidates.OrderBy(x => x.Life).FirstOrDefault();
            if (pick is not null)
            {
                this._jungleGroup = pick.Group;
                target = pick.Nearest;
                this.Logger.LogInformation(
                    "[MOBA-JGL] {Name} new camp: {Monster} x{Count} @ {X},{Y} ({Dist:F0} tiles{Far}), camp life {Life:F0} vs allowance {Allow:F0} (Lv{Lv}); {Alive} camp monster(s) alive on the map.",
                    this.Name,
                    pick.Nearest.Definition.Designation,
                    pick.Count,
                    pick.Nearest.Position.X,
                    pick.Nearest.Position.Y,
                    pick.Nearest.GetDistanceTo(c.Pos),
                    pick.Own ? string.Empty : ", ENEMY half",
                    pick.Life,
                    allowedLife,
                    this.MobaLevel,
                    camp.Count);
            }
            else
            {
                this._jungleGroup = null;
            }
        }

        this._jungleTarget = target;
        if (target is null)
        {
            if (this._jungleWaitSinceUtc == default)
            {
                this._jungleWaitSinceUtc = c.Now;
                this.Logger.LogInformation("[MOBA-JGL] {Name} has no camp to clear: waits in the jungle (Lv{Lv}, hp={Hp:P0}).", this.Name, this.MobaLevel, c.HpPct);
            }

            // Nothing alive: wait in the middle of the own jungle (a camp comes back every minute or two).
            var wait = MobaLayout.NearestWalkable(118, team == MobaTeam.Blue ? 104 : 151);
            if (c.Pos.EuclideanDistanceTo(wait) > 6)
            {
                await this.WalkTowardAsync(wait).ConfigureAwait(false);
            }

            return;
        }

        if (this._jungleWaitSinceUtc != default)
        {
            this.Logger.LogInformation("[MOBA-JGL] {Name} waited {Secs:F0}s for a camp.", this.Name, (c.Now - this._jungleWaitSinceUtc).TotalSeconds);
            this._jungleWaitSinceUtc = default;
        }

        await this.EngageAsync(c, target).ConfigureAwait(false);
    }

    /// <summary>Whether a jungle monster is fighting this bot right now (it must not stand still channelling a recall then).</summary>
    private bool IsHuntedByJungle(BotContext c)
        => c.Map is { } map && MobaJungle.LiveMonsters(map.MapId).Any(l => l.Ai.IsFighting && ReferenceEquals(l.Ai.Target, this) && l.Monster.GetDistanceTo(c.Pos) <= 16);

    /// <summary>Time per state and a summary every 30 s: what the jungler did, what it earned and how it stands.</summary>
    private void JungleTelemetry(BotContext c, BotState previousState)
    {
        var now = c.Now;
        if (this._jungleLastAccountUtc == default)
        {
            this._jungleLastAccountUtc = now;
            this._nextJungleSummaryUtc = now + TimeSpan.FromSeconds(30);
            this._jungleLevelAtStart = this.MobaLevel;
        }

        this._jungleStateSeconds.TryGetValue(previousState, out var spent);
        this._jungleStateSeconds[previousState] = spent + (now - this._jungleLastAccountUtc).TotalSeconds;
        this._jungleLastAccountUtc = now;

        if (now < this._nextJungleSummaryUtc)
        {
            return;
        }

        this._nextJungleSummaryUtc = now + TimeSpan.FromSeconds(30);
        this.Logger.LogInformation(
            "[MOBA-JGL] {Name} SUMMARY t={T}s Lv{Lv} hp={Hp:P0} kills={Kills} pos={X},{Y} state={State} | time per state: {States} | {Income}",
            this.Name,
            (int)MobaMatchTickPlugIn.MatchElapsed.TotalSeconds,
            this.MobaLevel,
            c.HpPct,
            this._jungleKills,
            c.Pos.X,
            c.Pos.Y,
            this._state,
            string.Join(", ", this._jungleStateSeconds.OrderByDescending(p => p.Value).Select(p => $"{p.Key} {p.Value:F0}s")),
            MobaIncome.Describe(this));
    }

    /// <summary>The jungler walks up to this many tiles out of its way to capture a totem or fountain; the other bots only the ones they pass.</summary>
    private const double CaptureDetourJungler = 45;

    private const double CaptureDetourLaner = 18;

    /// <summary>A bot walking to a push, a gathering or an ally captures what is this close to it (tiles).</summary>
    private const double CaptureDetourPassing = 14;

    private int _captureIndex = -1;

    /// <summary>
    /// Captures a fountain or a jungle totem that the team does not own (vision for the whole team, healing at the fountains): walks to it and
    /// holds the capture key like a player holds F. Only when no enemy is around.
    /// </summary>
    /// <returns><see langword="true"/> when it used the turn (walking to the point or channelling).</returns>
    private async ValueTask<bool> TryCaptureAsync(BotContext c, double? detourOverride = null)
    {
        var team = MobaTeams.GetTeam(this);
        if (team == MobaTeam.None || c.EnemyChampsNear.Count > 0 || c.InCombat || c.HpPct < 0.5f)
        {
            this._captureIndex = -1;
            return false;
        }

        var detour = detourOverride ?? (this._role == MobaBotRole.Jungle ? CaptureDetourJungler : CaptureDetourLaner);
        var best = -1;
        var bestDistance = detour;
        for (var i = 0; i < MobaFountains.PointCount; i++)
        {
            if (MobaFountains.StateOf(i).Owner == team)
            {
                continue;
            }

            var (px, py) = MobaFountains.PositionOf(i);
            var distance = c.Pos.EuclideanDistanceTo(new Point(px, py));
            if (distance < bestDistance || (i == this._captureIndex && distance <= detour))
            {
                best = i;
                bestDistance = distance;
            }
        }

        if (best < 0)
        {
            this._captureIndex = -1;
            return false;
        }

        var (x, y) = MobaFountains.PositionOf(best);
        var spot = MobaLayout.NearestWalkable(x, y);
        if (c.Pos.EuclideanDistanceTo(spot) > MobaFountains.CaptureRadius - 2)
        {
            if (this._captureIndex != best)
            {
                this.Logger.LogInformation("[MOBA-BOT] {Name} goes to capture point {Index} @ {X},{Y} ({Dist:F0} tiles).", this.Name, best, x, y, bestDistance);
            }

            this._captureIndex = best;
            await this.WalkTowardAsync(spot).ConfigureAwait(false);
            return true;
        }

        this._captureIndex = best;
        await MobaFountains.SignalAsync(this, true).ConfigureAwait(false);
        return true;
    }

    private static bool OwnHalf(MobaTeam team, Point home) => team == MobaTeam.Blue ? home.Y < 128 : home.Y >= 128;
}
