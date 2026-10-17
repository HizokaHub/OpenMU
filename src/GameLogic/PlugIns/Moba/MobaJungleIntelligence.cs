// <copyright file="MobaJungleIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>The monsters of one jungle spot: when one is hit, all of them defend.</summary>
public sealed class MobaJungleGroup
{
    /// <summary>Gets the monsters of the group.</summary>
    public List<MobaJungleIntelligence> Members { get; } = new();

    /// <summary>Makes every idle monster of the group fight the attacker.</summary>
    /// <param name="attacker">The champion that hit a monster of the group.</param>
    public void Aggro(IAttackable attacker)
    {
        foreach (var member in Members.ToArray())
        {
            member.Provoke(attacker);
        }
    }
}

/// <summary>
/// AI of a jungle monster (2026-10-17): it stands on its spot and never wanders. It does nothing until a champion hits it (or a monster of its spot);
/// then it defends and chases up to <see cref="LeashTiles"/> tiles from its spot. Once it is farther away than that, or loses its target, it walks
/// back to its spot and, once there, recovers <see cref="RegenFractionPerSecond"/> of its life every second. Runs on its own timer, so it works with nobody watching.
/// </summary>
public sealed class MobaJungleIntelligence : BasicMonsterIntelligence
{
    /// <summary>Tiles a monster may stray from its spot while chasing.</summary>
    public const double LeashTiles = 10;

    /// <summary>Fraction of the maximum life recovered every second once back on the spot.</summary>
    public const float RegenFractionPerSecond = 0.10f;

    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan MaxReturnTime = TimeSpan.FromSeconds(10);

    private readonly MobaJungleGroup _group;

    private readonly bool _passive;

    private Timer? _timer;

    private volatile bool _ticking;

    private IAttackable? _target;

    private State _state = State.Idle;

    private DateTime _nextAttackUtc;

    private DateTime _nextRegenUtc;

    private DateTime _returnStartedUtc;

    private int _pathFailures;

    /// <summary>Initializes a new instance of the <see cref="MobaJungleIntelligence"/> class.</summary>
    /// <param name="group">The group (spot) of the monster.</param>
    /// <param name="home">The spot the monster guards and returns to.</param>
    /// <param name="kind">The kind of monster.</param>
    /// <param name="passive">Whether the monster never fights back (spider eggs).</param>
    public MobaJungleIntelligence(MobaJungleGroup group, Point home, MobaJungle.Kind kind, bool passive = false)
    {
        this._group = group;
        this.Home = home;
        this.Kind = kind;
        this._passive = passive;
        group.Members.Add(this);
    }

    private enum State
    {
        Idle,
        Fight,
        Return,
    }

    /// <summary>Gets the spot the monster guards.</summary>
    public Point Home { get; }

    /// <summary>Gets the kind of monster.</summary>
    public MobaJungle.Kind Kind { get; }

    /// <summary>Gets the current target.</summary>
    public IAttackable? Target => this._target;

    /// <summary>Gets a value indicating whether the monster is fighting.</summary>
    public bool IsFighting => this._state == State.Fight;

    /// <summary>Gets a value indicating whether the monster is walking back to its spot.</summary>
    public bool IsReturning => this._state == State.Return;

    /// <summary>Gets the group of the monster.</summary>
    public MobaJungleGroup Group => this._group;

    /// <summary>Gets or sets the game context the monster was spawned in (needed to spawn minions).</summary>
    public IGameContext? GameContext { get; set; }

    /// <summary>Gets the earliest time each special attack of a boss may be used again, by name.</summary>
    public Dictionary<string, DateTime> SkillReadyAt { get; } = new();

    /// <summary>Gets the minions the boss summoned.</summary>
    public List<Monster> Minions { get; } = new();

    /// <summary>Gets or sets a value indicating whether the boss is enraged (Selupan's last skill).</summary>
    public bool Enraged { get; set; }

    /// <summary>Starts the AI timer (the base AI only starts with the first observer).</summary>
    public void Arm()
    {
        if (this._passive)
        {
            return;
        }

        var phase = TimeSpan.FromMilliseconds((this.Monster.Id % 10) * 25);
        this._timer ??= new Timer(_ => this.SafeTick(), null, TickInterval + phase, TickInterval);
    }

    /// <summary>Makes the monster fight the champion, unless it is walking back.</summary>
    /// <param name="attacker">The champion.</param>
    public void Provoke(IAttackable attacker)
    {
        if (this._passive || this._state == State.Return || !this.Monster.IsAlive)
        {
            return;
        }

        if (this._target is null || !this._target.IsActive())
        {
            this._target = attacker;
        }

        this._state = State.Fight;
    }

    /// <inheritdoc />
    public override void RegisterHit(IAttacker attacker)
    {
        // Not calling the base: its own AI must stay idle, this class decides.
        if (this._passive || this._state == State.Return)
        {
            return;
        }

        if (attacker is Player { IsMobaClone: true } champion)
        {
            this._group.Aggro(champion);
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
        if (!monster.IsAlive)
        {
            this._timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            return;
        }

        if (monster.Attributes[Stats.IsStunned] > 0 || monster.Attributes[Stats.IsAsleep] > 0 || monster.Attributes[Stats.IsFrozen] > 0)
        {
            return;
        }

        switch (this._state)
        {
            case State.Idle:
                this.Regenerate();
                if (!monster.IsWalking && monster.Position.EuclideanDistanceTo(this.Home) > 1.5)
                {
                    this.BeginReturn();
                }

                break;
            case State.Fight:
                await this.FightAsync().ConfigureAwait(false);
                break;
            default:
                await this.ReturnAsync().ConfigureAwait(false);
                break;
        }
    }

    private async ValueTask FightAsync()
    {
        var monster = this.Monster;
        var pos = monster.Position;
        var target = this._target;

        if (target is null || !target.IsActive() || target.CurrentMap != monster.CurrentMap || target.IsAtSafezone())
        {
            this._target = null;
            this.BeginReturn();
            return;
        }

        if (pos.EuclideanDistanceTo(this.Home) > LeashTiles)
        {
            this.BeginReturn();
            return;
        }

        var attackRange = monster.Definition.AttackRange;
        if (target.GetDistanceTo(pos) <= attackRange)
        {
            this._pathFailures = 0;
            if (monster.IsWalking)
            {
                await monster.StopWalkingAsync().ConfigureAwait(false);
            }

            var now = DateTime.UtcNow;
            if (now >= this._nextAttackUtc)
            {
                var delay = monster.Definition.AttackDelay;
                this._nextAttackUtc = now + (delay > TimeSpan.Zero ? delay : TimeSpan.FromMilliseconds(1500));
                if (!await MobaJungleSkills.TryUseAsync(this, target).ConfigureAwait(false))
                {
                    await monster.AttackAsync(target).ConfigureAwait(false);
                }
            }

            return;
        }

        if (monster.IsWalking)
        {
            return;
        }

        if (await MobaJungleSkills.TryApproachAsync(this, target).ConfigureAwait(false))
        {
            return;
        }

        var goal = monster.CurrentMap!.Terrain.GetRandomCoordinate(target.Position, attackRange);
        if (!await monster.WalkToAsync(goal).ConfigureAwait(false) && ++this._pathFailures >= 4)
        {
            // Nowhere to go (the target stands where the monster cannot reach): give up.
            this._target = null;
            this.BeginReturn();
        }
    }

    private async ValueTask ReturnAsync()
    {
        var monster = this.Monster;
        var pos = monster.Position;
        if (pos.EuclideanDistanceTo(this.Home) <= 1.5)
        {
            this._state = State.Idle;
            this._nextRegenUtc = DateTime.UtcNow;
            return;
        }

        if (DateTime.UtcNow - this._returnStartedUtc > MaxReturnTime)
        {
            // Could not walk back in time (blocked path): go home at once.
            await monster.StopWalkingAsync().ConfigureAwait(false);
            await monster.MoveAsync(this.Home).ConfigureAwait(false);
            return;
        }

        if (!monster.IsWalking)
        {
            await monster.WalkToAsync(this.Home).ConfigureAwait(false);
        }
    }

    private void BeginReturn()
    {
        this._state = State.Return;
        this._target = null;
        this._returnStartedUtc = DateTime.UtcNow;
        this._pathFailures = 0;
    }

    private void Regenerate()
    {
        var now = DateTime.UtcNow;
        if (now < this._nextRegenUtc)
        {
            return;
        }

        this._nextRegenUtc = now + TimeSpan.FromSeconds(1);
        var attributes = this.Monster.Attributes;
        var max = attributes[Stats.MaximumHealth];
        var current = attributes[Stats.CurrentHealth];
        if (current < max)
        {
            attributes[Stats.CurrentHealth] = Math.Min(max, current + (max * RegenFractionPerSecond));
        }
    }
}
