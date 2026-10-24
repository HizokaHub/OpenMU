// <copyright file="MobaGoldenIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The brain of a golden monster of the invasion (see <see cref="MobaGolden"/>). It runs its own timer and attacks everything in its reach: champions,
/// lane minions, summons and structures, never another golden monster. A guard stands on its spot and chases up to <see cref="LeashTiles"/> tiles from it;
/// a patroller walks its route (the lane towards an enemy base) and fights what it meets. A turret hit makes it retreat for a few seconds and ignore the
/// structures for a while, so the goblins do not die suicidally under a turret.
/// </summary>
public sealed class MobaGoldenIntelligence : BasicMonsterIntelligence
{
    /// <summary>Tiles a guard may stray from its spot.</summary>
    public const double LeashTiles = 20;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(300);

    private static readonly TimeSpan RetreatTime = TimeSpan.FromSeconds(7);

    private static readonly TimeSpan IgnoreStructuresTime = TimeSpan.FromSeconds(30);

    private readonly MobaGolden.Tier _tier;
    private readonly Point _home;
    private readonly bool _guard;
    private readonly IReadOnlyList<Point>? _patrol;
    private readonly MobaRouteWalker _walker = new();
    private Timer? _timer;
    private volatile bool _ticking;
    private IAttackable? _target;
    private int _patrolIndex;
    private DateTime _nextAttackUtc;
    private DateTime _retreatUntilUtc;
    private DateTime _ignoreStructuresUntilUtc;
    private DateTime _nextRegenUtc;
    private Point _retreatGoal;
    private int _pathFailures;

    /// <summary>Initializes a new instance of the <see cref="MobaGoldenIntelligence"/> class.</summary>
    /// <param name="tier">The tier of the monster.</param>
    /// <param name="home">The spot it guards (or starts from).</param>
    /// <param name="guard">Whether it stands on its spot (otherwise it patrols).</param>
    /// <param name="patrol">The points it walks, in order, when it patrols.</param>
    public MobaGoldenIntelligence(MobaGolden.Tier tier, Point home, bool guard, IReadOnlyList<Point>? patrol)
    {
        this._tier = tier;
        this._home = home;
        this._guard = guard || patrol is null || patrol.Count == 0;
        this._patrol = patrol;
    }

    /// <summary>Starts the AI timer (the base AI only starts with the first observer).</summary>
    public void Arm()
    {
        var phase = TimeSpan.FromMilliseconds((this.Monster.Id % 10) * 30);
        this._timer ??= new Timer(_ => this.SafeTick(), null, TickInterval + phase, TickInterval);
    }

    /// <inheritdoc />
    public override void RegisterHit(IAttacker attacker)
    {
        // Not calling the base: its own AI must stay idle, this class decides.
        if (attacker is Monster structure && MobaStructures.IsStructure(structure))
        {
            var now = DateTime.UtcNow;
            this._retreatUntilUtc = now + RetreatTime;
            this._ignoreStructuresUntilUtc = now + IgnoreStructuresTime;
            this._target = null;
            var from = this.Monster.Position;
            var dx = from.X - structure.Position.X;
            var dy = from.Y - structure.Position.Y;
            var length = Math.Max(1.0, Math.Sqrt((dx * dx) + (dy * dy)));
            this._retreatGoal = MobaLayout.NearestWalkable(from.X + (dx / length * 14), from.Y + (dy / length * 14));
        }
    }

    /// <inheritdoc />
    protected override ValueTask<IAttackable?> SearchNextTargetAsync() => ValueTask.FromResult<IAttackable?>(null);

    /// <inheritdoc />
    protected override ValueTask TickWithoutTargetAsync() => ValueTask.CompletedTask;

    /// <inheritdoc />
    protected override void Dispose(bool managed)
    {
        this._timer?.Dispose();
        this._timer = null;
        base.Dispose(managed);
    }

    private static int Distance(Point a, Point b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Timer callback; exceptions swallowed to keep the timer alive.")]
    private async void SafeTick()
    {
        if (this._ticking)
        {
            return;
        }

        this._ticking = true;
        try
        {
            await this.TickAsync().ConfigureAwait(false);
        }
        catch
        {
            // keep the timer alive
        }
        finally
        {
            this._ticking = false;
        }
    }

    private async ValueTask TickAsync()
    {
        var monster = this.Monster;
        if (!monster.IsAlive || monster.CurrentMap is not { } map)
        {
            this._timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        if (monster.Attributes[Stats.IsStunned] > 0 || monster.Attributes[Stats.IsAsleep] > 0 || monster.Attributes[Stats.IsFrozen] > 0)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (now < this._retreatUntilUtc)
        {
            if (!monster.IsWalking)
            {
                await this._walker.TravelAsync(monster, this._retreatGoal).ConfigureAwait(false);
            }

            return;
        }

        var position = monster.Position;
        if (this._target is { } current && !this.IsValidTarget(current, now))
        {
            this._target = null;
        }

        if (this._target is not null && this._guard && Distance(position, this._home) > LeashTiles)
        {
            this._target = null;
        }

        this._target ??= this.FindTarget(map, position, now);
        if (this._target is { } target)
        {
            await this.FightAsync(map, target).ConfigureAwait(false);
            return;
        }

        this._pathFailures = 0;
        if (this._guard)
        {
            await this.GuardAsync(position, now).ConfigureAwait(false);
        }
        else
        {
            await this.PatrolAsync(position).ConfigureAwait(false);
        }
    }

    private bool IsValidTarget(IAttackable target, DateTime now)
    {
        if (!target.IsAlive || !target.IsActive() || target.CurrentMap != this.Monster.CurrentMap || ReferenceEquals(target, this.Monster) || MobaGolden.IsGolden(target))
        {
            return false;
        }

        switch (target)
        {
            case Player player:
                return player.IsMobaClone && !player.IsInvisible && !player.IsAtSafezone();
            case Monster monster when MobaStructures.IsStructure(monster):
                return now >= this._ignoreStructuresUntilUtc;
            case Monster monster:
                return MobaTeams.GetTeam(monster) != MobaTeam.None;
            default:
                return false;
        }
    }

    private IAttackable? FindTarget(GameMap map, Point position, DateTime now)
    {
        var radius = this._tier == MobaGolden.Tier.Great ? 12 : 9;
        IAttackable? best = null;
        var bestScore = double.MaxValue;
        foreach (var candidate in map.GetAttackablesInRange(position, radius))
        {
            if (!this.IsValidTarget(candidate, now))
            {
                continue;
            }

            // Champions first, then minions and summons, structures last.
            var distance = candidate.GetDistanceTo(position);
            var score = distance + (candidate is Player ? 0 : MobaStructures.IsStructure(candidate) ? 40 : 6);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        return best;
    }

    private async ValueTask FightAsync(GameMap map, IAttackable target)
    {
        var monster = this.Monster;
        var reach = monster.Definition.AttackRange + (MobaStructures.IsStructure(target) ? 2 : 0);
        if (target.GetDistanceTo(monster.Position) <= reach)
        {
            this._pathFailures = 0;
            if (monster.IsWalking)
            {
                await monster.StopWalkingAsync().ConfigureAwait(false);
            }

            var now = DateTime.UtcNow;
            if (now >= this._nextAttackUtc)
            {
                this._nextAttackUtc = now + TimeSpan.FromSeconds(MobaGolden.AttackSecondsOf(this._tier));
                await monster.AttackAsync(target).ConfigureAwait(false);
                if (this._tier >= MobaGolden.Tier.Large && target is Player)
                {
                    await this.CleaveAsync(map, target).ConfigureAwait(false);
                }
            }

            return;
        }

        if (monster.IsWalking)
        {
            return;
        }

        var goal = map.Terrain.GetRandomCoordinate(target.Position, (byte)Math.Max(reach - 1, 1));
        if (!await this._walker.TravelAsync(monster, goal).ConfigureAwait(false) && ++this._pathFailures >= 4)
        {
            // Nowhere to go (the target stands where the monster cannot reach): look for another one.
            this._target = null;
            this._pathFailures = 0;
        }
    }

    /// <summary>The big golden monsters hit the other champions around the target too.</summary>
    private async ValueTask CleaveAsync(GameMap map, IAttackable target)
    {
        var factor = this._tier == MobaGolden.Tier.Great ? 0.6 : 0.4;
        foreach (var other in map.GetAttackablesInRange(target.Position, 3).OfType<Player>().Where(p => p.IsMobaClone && p.IsAlive && !ReferenceEquals(p, target) && !p.IsAtSafezone()).ToList())
        {
            await other.AttackByAsync(this.Monster, null, false, factor).ConfigureAwait(false);
        }
    }

    private async ValueTask GuardAsync(Point position, DateTime now)
    {
        var monster = this.Monster;
        if (Distance(position, this._home) > 1)
        {
            if (!monster.IsWalking)
            {
                await this._walker.TravelAsync(monster, this._home).ConfigureAwait(false);
            }

            return;
        }

        if (now >= this._nextRegenUtc)
        {
            this._nextRegenUtc = now + TimeSpan.FromSeconds(1);
            var attributes = monster.Attributes;
            var max = attributes[Stats.MaximumHealth];
            var current = attributes[Stats.CurrentHealth];
            if (current < max)
            {
                attributes[Stats.CurrentHealth] = Math.Min(max, current + (max * 0.03f));
            }
        }
    }

    private async ValueTask PatrolAsync(Point position)
    {
        var monster = this._patrol is { } route && route.Count > 0 ? this.Monster : null;
        if (monster is null)
        {
            return;
        }

        var goal = this._patrol![Math.Min(this._patrolIndex, this._patrol.Count - 1)];
        if (Distance(position, goal) <= 3)
        {
            if (this._patrolIndex < this._patrol.Count - 1)
            {
                this._patrolIndex++;
            }

            return;
        }

        if (!monster.IsWalking && !await this._walker.TravelAsync(monster, goal).ConfigureAwait(false) && this._patrolIndex < this._patrol.Count - 1)
        {
            this._patrolIndex++;
        }
    }
}
