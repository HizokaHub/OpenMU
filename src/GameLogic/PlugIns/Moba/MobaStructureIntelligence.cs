// <copyright file="MobaStructureIntelligence.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;
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

    private readonly bool _attacks;

    private Timer? _timer;

    private volatile bool _ticking;

    private IAttackable? _target;

    private DateTime _nextAttackUtc;

    private ushort _mapId;

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaStructureIntelligence"/> class.
    /// </summary>
    /// <param name="team">The structure's team.</param>
    /// <param name="type">The structure type (turret / nexus).</param>
    /// <param name="attacks">Whether the structure shoots (true for turrets, false for the nexus).</param>
    public MobaStructureIntelligence(MobaTeam team, MobaStructureType type, bool attacks = true)
    {
        this._team = team;
        this._type = type;
        this._attacks = attacks;
    }

    /// <inheritdoc />
    protected override void OnStart()
    {
        base.OnStart();
        this._mapId = this.Monster.CurrentMap.MapId;
        MobaTeams.Set(this.Monster, this._team);
        MobaStructures.Mark(this.Monster, this._type);
        this.ClaimFootprint();
        if (this._attacks)
        {
            this._timer ??= new Timer(_ => this.SafeTick(), null, TickInterval, TickInterval);
        }
    }

    /// <summary>Permanently reserves a 3x3 footprint in the occupancy grid so lane creeps path around the structure.</summary>
    private void ClaimFootprint()
    {
        var mapId = this._mapId;
        var p = this.Monster.Position;
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                MobaOccupancyGrid.TryClaim(mapId, new Point((byte)(p.X + dx), (byte)(p.Y + dy)), this);
            }
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
        MobaOccupancyGrid.ReleaseAll(this._mapId, this);

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

            // Against a champion, a turret hits for a big fraction of the target's MAX HP
            // (LoL-style escalating tower damage), and each CONSECUTIVE shot on the same
            // champion ramps up - a dive that lasts 3+ shots is lethal. Creeps just take
            // the flat weapon damage above.
            if (target is Player { IsMobaClone: true } champion
                && champion.IsAlive
                && champion.Attributes is { } a)
            {
                var now = DateTime.UtcNow;
                var ramp = RampByChampion.GetOrCreateValue(champion);
                if (now - ramp.LastShotUtc > RampReset)
                {
                    ramp.Shots = 0;
                }

                ramp.Shots++;
                ramp.LastShotUtc = now;
                var rampMul = ramp.Shots <= 1 ? 1.0f : ramp.Shots == 2 ? 1.4f : Math.Min(2.4f, 1.4f + (0.5f * (ramp.Shots - 2)));

                var bonus = (uint)Math.Max(1, a[Stats.MaximumHealth] * TurretChampionMaxHealthFraction * rampMul);
                await champion.ApplyPoisonDamageAsync(monster, bonus).ConfigureAwait(false);
                champion.Logger.LogInformation(
                    "[MOBA-STRUCT] {Team} turret shot #{Shots} x{Mul:F1} -> {Champ} for {Bonus} true ({Hp:F0}/{MaxHp:F0} left)",
                    this._team,
                    ramp.Shots,
                    rampMul,
                    champion.Name,
                    bonus,
                    Math.Max(0f, a[Stats.CurrentHealth]),
                    a[Stats.MaximumHealth]);
            }
        }
    }

    /// <summary>Fraction of a champion's MAX HP a single (first) turret shot deals (on top of the flat weapon hit).</summary>
    private const float TurretChampionMaxHealthFraction = 0.16f;

    /// <summary>Consecutive turret shots on the same champion ramp; the count resets after this gap out of range.</summary>
    private static readonly TimeSpan RampReset = TimeSpan.FromSeconds(4);

    private static readonly ConditionalWeakTable<Player, TurretRamp> RampByChampion = new();

    private sealed class TurretRamp
    {
        public int Shots;

        public DateTime LastShotUtc;
    }
}
