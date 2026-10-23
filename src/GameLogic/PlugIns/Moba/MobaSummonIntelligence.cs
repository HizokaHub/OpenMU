// <copyright file="MobaSummonIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The brain of the creature an Elf summons in a MOBA match. It runs its own timer (so it keeps working when nobody is near it,
/// which the control mode allows), follows its owner and fights the enemies near it by itself, and obeys the orders of the
/// owner while the control mode is on: walk to a point (anywhere on the map) or attack a target.
/// </summary>
public sealed class MobaSummonIntelligence : SummonedMonsterIntelligence
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(400);

    /// <summary>Tiles from the owner at which a summon which follows starts walking back to it.</summary>
    private const int FollowDistance = 4;

    /// <summary>Tiles within which the short search of the monster is enough; further away the creature follows a route.</summary>
    private const int DirectWalkRange = 6;

    /// <summary>Steps of a stretch of the route (the walker takes 16 at most; the client follows each stretch with its own path).</summary>
    private const int StepsPerHop = 10;

    /// <summary>Nodes a route search may close (the map is 256x256).</summary>
    private const int RouteSearchLimit = 20000;

    private static readonly ConcurrentBag<PathFinder> RouteFinders = new();

    /// <summary>Tiles around the one it watches (the owner when it follows, itself when controlled) in which it picks fights.</summary>
    private const int AcquireRange = 7;

    /// <summary>Tiles from the owner past which a summon which follows gives up a chase.</summary>
    private const int LeashRange = 12;

    private Timer? _timer;
    private int _ticking;
    private IAttackable? _attackOrder;
    private IAttackable? _retaliateTarget;
    private Point? _moveOrder;
    private DateTime _nextAttackUtc;
    private IList<PathResultNode>? _route;
    private int _routeIndex;
    private Point _routeGoal;
    private int _moveFailures;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaSummonIntelligence"/> class.
    /// </summary>
    /// <param name="owner">The owner.</param>
    public MobaSummonIntelligence(Player owner)
        : base(owner)
    {
    }

    /// <summary>Gets a value indicating whether the owner is commanding the summon (otherwise it follows the owner).</summary>
    public bool Controlled => this.Owner.MobaSummonControl;

    /// <summary>Gets a value indicating whether the owner left it holding its post (it neither follows nor is commanded).</summary>
    public bool Stay => this.Owner.MobaSummonStay;

    /// <summary>Gets the point the summon was ordered to walk to, if any.</summary>
    public Point? MoveOrder => this._moveOrder;

    /// <summary>Orders the summon to walk to a point and to stand there.</summary>
    /// <param name="target">The point.</param>
    /// <returns>A task.</returns>
    public ValueTask OrderMoveAsync(Point target)
    {
        this._attackOrder = null;
        this._retaliateTarget = null;
        this._moveOrder = target;
        return this.KickAsync(stopWalking: true);
    }

    /// <summary>Orders the summon to attack a target until it dies.</summary>
    /// <param name="target">The target.</param>
    /// <returns>A task.</returns>
    public ValueTask OrderAttackAsync(IAttackable target)
    {
        this._moveOrder = null;
        this._attackOrder = target;
        return this.KickAsync(stopWalking: true);
    }

    /// <summary>Drops the orders (the owner gave the control back).</summary>
    public void ClearOrders()
    {
        this._moveOrder = null;
        this._attackOrder = null;
    }

    /// <inheritdoc />
    public override void RegisterHit(IAttacker attacker)
    {
        // Defend itself: an enemy which hits it becomes the target when nothing else is going on.
        if (attacker is IAttackable { IsAlive: true } attackable && MobaTeams.AreEnemies(this.Monster, attacker))
        {
            this._retaliateTarget = attackable;
        }
    }

    /// <inheritdoc />
    /// <remarks>The base tick stays inert; this summon runs its own timer.</remarks>
    protected override ValueTask<bool> CanAttackAsync() => ValueTask.FromResult(false);

    /// <inheritdoc />
    protected override void OnStart()
    {
        base.OnStart();
        this._timer ??= new Timer(_ => this.SafeTick(), null, TickInterval, TickInterval);
    }

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
        try
        {
            await this.KickAsync(stopWalking: false).ConfigureAwait(false);
        }
        catch
        {
            // keep the timer alive
        }
    }

    private async ValueTask KickAsync(bool stopWalking)
    {
        if (Interlocked.Exchange(ref this._ticking, 1) == 1)
        {
            return;
        }

        try
        {
            if (stopWalking && this.Monster.IsWalking)
            {
                await this.Monster.StopWalkingAsync().ConfigureAwait(false);
            }

            await this.TickAsync().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref this._ticking, 0);
        }
    }

    private async ValueTask TickAsync()
    {
        var monster = this.Monster;
        if (!monster.IsAlive || monster.CurrentMap is not { } map)
        {
            return;
        }

        if (monster.Attributes[Stats.IsStunned] > 0 || monster.Attributes[Stats.IsAsleep] > 0 || monster.Attributes[Stats.IsFrozen] > 0)
        {
            return;
        }

        var position = monster.Position;
        var attackRange = Math.Max((int)monster.Definition.AttackRange, 1);

        // 1. An ordered attack wins over everything.
        if (this._attackOrder is { } ordered)
        {
            if (!this.IsValidEnemy(ordered))
            {
                this._attackOrder = null;
            }
            else
            {
                await this.FightAsync(ordered, attackRange).ConfigureAwait(false);
                return;
            }
        }

        // 2. An ordered walk: go there (anywhere on the map), fight whatever comes at it on arrival.
        if (this._moveOrder is { } destination)
        {
            if (Distance(position, destination) <= 1)
            {
                this._moveOrder = null;
            }
            else
            {
                if (!monster.IsWalking && !await this.TravelAsync(map, destination).ConfigureAwait(false) && ++this._moveFailures >= 3)
                {
                    // No way there (a wall, a closed area): the order is dropped instead of standing still forever.
                    this._moveOrder = null;
                    this._moveFailures = 0;
                }

                return;
            }
        }

        // 3. Free: finish what attacked it, or pick a fight with the enemies near it.
        if (this._retaliateTarget is { } retaliate && !this.IsValidEnemy(retaliate))
        {
            this._retaliateTarget = null;
        }

        var holdsPost = this.Controlled || this.Stay;
        var watchCenter = holdsPost ? position : this.Owner.Position;
        var target = this._retaliateTarget ?? this.FindEnemy(map, watchCenter, position);
        if (target is not null && (holdsPost || Distance(target.Position, this.Owner.Position) <= LeashRange))
        {
            await this.FightAsync(target, attackRange).ConfigureAwait(false);
            return;
        }

        this._retaliateTarget = null;
        if (!holdsPost)
        {
            await this.FollowOwnerAsync(map, position).ConfigureAwait(false);
        }
    }

    private async ValueTask FollowOwnerAsync(GameMap map, Point position)
    {
        var owner = this.Owner;
        if (!owner.IsAlive || owner.CurrentMap != map)
        {
            return;
        }

        var distance = Distance(position, owner.Position);
        if (distance <= FollowDistance || this.Monster.IsWalking)
        {
            return;
        }

        // Called back from anywhere on the map it walks all the way (a route of short stretches), it never jumps to the owner.
        await this.TravelAsync(map, map.Terrain.GetRandomCoordinate(owner.Position, 2)).ConfigureAwait(false);
    }

    private static PathFinder CreateRouteFinder()
        => new(new FullGridNetwork(true))
        {
            Heuristic = new ManhattanHeuristic(),
            HeuristicEstimate = 12,
            SearchLimit = RouteSearchLimit,
        };

    /// <summary>
    /// Walks towards a point anywhere on the map. Close points use the short search of the monster; far ones a heuristic search of
    /// the whole route, walked in stretches of <see cref="StepsPerHop"/> steps (the short search gives up past some tiles, which left
    /// the creature standing half way, and the client cannot follow a walk of 16 steps either).
    /// </summary>
    /// <returns><see langword="true"/> if a walk was started or is on its way.</returns>
    private async ValueTask<bool> TravelAsync(GameMap map, Point goal)
    {
        var monster = this.Monster;
        var position = monster.Position;
        if (monster.IsWalking)
        {
            return true;
        }

        if (Distance(position, goal) <= DirectWalkRange && await monster.WalkToAsync(goal).ConfigureAwait(false))
        {
            this._route = null;
            return true;
        }

        if (this._route is not { } route
            || this._routeIndex >= route.Count
            || Distance(goal, this._routeGoal) > 3
            || Distance(position, route[this._routeIndex].PreviousPoint) > 1)
        {
            route = this.FindRoute(map, position, goal);
            if (route is null || route.Count == 0)
            {
                this._route = null;
                return false;
            }

            this._route = route;
            this._routeIndex = 0;
            this._routeGoal = goal;
        }

        var count = Math.Min(route.Count - this._routeIndex, StepsPerHop);
        var steps = new WalkingStep[count];
        for (var i = 0; i < count; i++)
        {
            var node = route[this._routeIndex + i];
            var from = i == 0 ? position : steps[i - 1].To;
            steps[i] = new WalkingStep(from, node.Point, from.GetDirectionTo(node.Point));
        }

        this._routeIndex += count;
        await monster.WalkToAsync(steps[count - 1].To, steps).ConfigureAwait(false);
        return true;
    }

    private IList<PathResultNode>? FindRoute(GameMap map, Point from, Point to)
    {
        if (!RouteFinders.TryTake(out var finder))
        {
            finder = CreateRouteFinder();
        }

        try
        {
            finder.ResetPathFinder();
            return finder.FindPath(from, to, map.Terrain.AIgrid, true);
        }
        finally
        {
            RouteFinders.Add(finder);
        }
    }

    private sealed class ManhattanHeuristic : IHeuristic
    {
        public int HeuristicEstimateMultiplier { get; set; }

        public int CalculateHeuristicDistance(Point location, Point target)
            => this.HeuristicEstimateMultiplier * (Math.Abs(location.X - target.X) + Math.Abs(location.Y - target.Y));
    }

    private async ValueTask FightAsync(IAttackable target, int attackRange)
    {
        var monster = this.Monster;
        var reach = attackRange <= 2 ? attackRange + 1 : attackRange;
        if (Distance(monster.Position, target.Position) <= reach)
        {
            if (monster.IsWalking)
            {
                await monster.StopWalkingAsync().ConfigureAwait(false);
            }

            if (DateTime.UtcNow >= this._nextAttackUtc)
            {
                var delay = monster.Definition.AttackDelay;
                this._nextAttackUtc = DateTime.UtcNow + (delay > TimeSpan.FromMilliseconds(800) ? delay : TimeSpan.FromMilliseconds(1200));
                await monster.AttackAsync(target).ConfigureAwait(false);
            }

            return;
        }

        if (!monster.IsWalking && monster.CurrentMap is { } map)
        {
            await this.TravelAsync(map, map.Terrain.GetRandomCoordinate(target.Position, (byte)Math.Max(attackRange - 1, 1))).ConfigureAwait(false);
        }
    }

    private bool IsValidEnemy(IAttackable target)
        => target.IsAlive
           && target.IsActive()
           && target is not Player { IsInvisible: true }
           && !target.IsAtSafezone()
           && MobaTeams.AreEnemies(this.Monster, target)
           && target.CurrentMap == this.Monster.CurrentMap;

    private IAttackable? FindEnemy(GameMap map, Point center, Point self)
    {
        // Champions and minions of the other team; the structures are only attacked on an order (a lone summon under a turret dies).
        return map.GetAttackablesInRange(center, AcquireRange)
            .Where(a => !MobaStructures.IsStructure(a) && this.IsValidEnemy(a) && MobaVision.CanTarget(this.Owner, a))
            .OrderBy(a => Distance(a.Position, self))
            .FirstOrDefault();
    }
}
