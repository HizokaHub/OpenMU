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

    /// <summary>The jungler keeps the monster it is hitting while it lives, is not walking home and stays within this many tiles.</summary>
    private const double JungleKeepTargetTiles = 22;

    private Monster? _jungleTarget;

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
    /// The jungler's day: clear the camps of its own half (the nearest first; when none is left, the enemy's), never wandering into a
    /// champion fight by itself. When the lanes need help it gangs (see <c>TryRegroup</c>) and when the team calls an objective it goes.
    /// </summary>
    private async ValueTask TickJungleAsync(BotContext c)
    {
        var team = MobaTeams.GetTeam(this);
        var live = MobaJungle.LiveMonsters(c.Map.MapId);

        // Keep hitting the monster it started on.
        var target = this._jungleTarget;
        if (target is null || !target.IsAlive || target.GetDistanceTo(c.Pos) > JungleKeepTargetTiles
            || live.Any(l => ReferenceEquals(l.Monster, target) && l.Ai.IsReturning))
        {
            target = live
                .Where(l => !IsObjectiveKind(l.Ai.Kind) && !l.Ai.IsReturning)
                .OrderBy(l => l.Monster.GetDistanceTo(c.Pos) + (OwnHalf(team, l.Ai.Home) ? 0 : 40))
                .Select(l => l.Monster)
                .FirstOrDefault();
            this._jungleTarget = target;
        }

        if (target is null)
        {
            // Nothing alive: wait in the middle of the own jungle (a camp comes back every minute or two).
            var wait = MobaLayout.NearestWalkable(118, team == MobaTeam.Blue ? 104 : 151);
            if (c.Pos.EuclideanDistanceTo(wait) > 6)
            {
                await this.WalkTowardAsync(wait).ConfigureAwait(false);
            }

            return;
        }

        await this.EngageAsync(c, target).ConfigureAwait(false);
    }

    private static bool OwnHalf(MobaTeam team, Point home) => team == MobaTeam.Blue ? home.Y < 128 : home.Y >= 128;
}
