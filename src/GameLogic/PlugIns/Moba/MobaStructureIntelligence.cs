// <copyright file="MobaStructureIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Threading;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// Self-contained AI for a MOBA structure (lane turret / nexus): it never moves, and
/// shoots the highest-priority enemy inside its attack range.
/// </summary>
/// <remarks>
/// Priority, LoL-style:
/// <list type="number">
///   <item>an enemy champion that damaged an allied champion inside turret range in the
///   last <see cref="DefendWindow"/> ("turret aggro" - switches even off a locked creep);</item>
///   <item>otherwise keep the current target until it dies or leaves range (turret lock -
///   this is what lets a player last-hit under turret);</item>
///   <item>otherwise the nearest enemy creep;</item>
///   <item>otherwise the nearest enemy champion.</item>
/// </list>
/// Runs on its own timer, started by the spawner, so it fires even with no player
/// observing (the base AI only starts on the first observer).
/// </remarks>
public sealed class MobaStructureIntelligence : BasicMonsterIntelligence
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan DefendWindow = TimeSpan.FromSeconds(4);

    private readonly MobaTeam _team;

    private readonly MobaStructureType _type;

    private Timer? _timer;

    private volatile bool _ticking;

    private IAttackable? _target;

    private DateTime _nextAttackUtc;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaStructureIntelligence"/> class.
    /// </summary>
    /// <param name="team">The structure's team.</param>
    /// <param name="type">The structure type (turret / nexus).</param>
    public MobaStructureIntelligence(MobaTeam team, MobaStructureType type)
    {
        this._team = team;
        this._type = type;
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        base.OnStart();
        MobaTeams.Set(this.Monster, this._team);
        MobaStructures.Mark(this.Monster, this._type);
        this._timer ??= new Timer(_ => this.SafeTick(), null, TickInterval, TickInterval);
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
        if (!monster.IsAlive || monster.CurrentMap is not { } map)
        {
            return;
        }

        var pos = monster.Position;
        var range = monster.Definition.AttackRange;

        // Drop a dead / out-of-range target.
        if (this._target is { } current && (!current.IsActive() || current.GetDistanceTo(pos) > range))
        {
            this._target = null;
        }

        var inRange = map.GetAttackablesInRange(pos, range)
            .Where(a => a.IsActive() && !ReferenceEquals(a, monster) && MobaTeams.AreEnemies(monster, a))
            .ToList();

        // 1) Turret aggro: an enemy champion that just hit an allied champion in range.
        var alliedChampions = map.GetAttackablesInRange(pos, range)
            .Where(a => MobaTeams.AreAllies(monster, a))
            .OfType<Player>()
            .Cast<object>()
            .ToList();

        if (alliedChampions.Count > 0)
        {
            var aggro = inRange.OfType<Player>()
                .Where(c => MobaCombatLog.HitAnyOf(c, alliedChampions, DefendWindow))
                .MinBy(monster.GetDistanceTo);
            if (aggro is not null)
            {
                this._target = aggro;
            }
        }

        // 2) Keep the locked target; 3) nearest creep; 4) nearest champion.
        if (this._target is null)
        {
            this._target = inRange.OfType<Monster>().Where(m => !MobaStructures.IsStructure(m)).MinBy(monster.GetDistanceTo)
                           ?? (IAttackable?)inRange.OfType<Player>().MinBy(monster.GetDistanceTo);
        }

        if (this._target is { } target
            && target.GetDistanceTo(pos) <= range
            && DateTime.UtcNow >= this._nextAttackUtc)
        {
            var delay = monster.Definition.AttackDelay;
            this._nextAttackUtc = DateTime.UtcNow + (delay > TimeSpan.Zero ? delay : TimeSpan.FromSeconds(1));
            await monster.AttackAsync(target).ConfigureAwait(false);
        }
    }
}
