// <copyright file="MobaBotPlayer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.AttributeSystem;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.Interfaces;

// Player has a `long MobaExperience` property that shadows the MobaExperience class name
// inside this subclass, so alias the class.
using MobaXp = MUnique.OpenMU.GameLogic.PlugIns.Moba.MobaExperience;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// A server-driven champion bot for MOBA balance testing. It is an
/// <see cref="OfflinePlayer"/> that carries a MOBA clone character; instead of the
/// mob-hunting MU-Helper AI it runs a tiny "walk to the nearest enemy champion and
/// cycle the loadout" brain, so real champion combat (damage, cooldowns, passives,
/// visual effects) can be observed in the <c>[MOBA-DMG]</c> log.
/// </summary>
public sealed class MobaBotPlayer : OfflinePlayer
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(700);
    private static readonly TimeSpan ActionCooldown = TimeSpan.FromMilliseconds(900);
    private const int AcquireRangeTiles = 25;
    private const int PreferredRangeTiles = 2;

    private static readonly ConcurrentDictionary<MobaBotPlayer, byte> ActiveBots = new();

    /// <summary>How close (tiles) counts as "reached" a lane waypoint.</summary>
    private const float WaypointReachedTiles = 3f;

    /// <summary>Within this many tiles of an enemy turret the bot won't dive without allied creeps.</summary>
    private const int TurretDangerTiles = 9;

    /// <summary>How far (Y tiles) short of a live enemy front turret a bot must stop when it has no allied wave tanking that turret.</summary>
    private const int LaneLimitMargin = 8;

    /// <summary>With an allied wave, how far (Y tiles) PAST a live enemy front turret a bot may go - just enough to body it, never a free run to the base.</summary>
    private const int TurretBodyMargin = 3;

    /// <summary>Hard no-go radius around the enemy nexus / spawn - bots never path in here (no diving the fountain for respawn kills).</summary>
    private const int FountainExclusionTiles = 18;

    /// <summary>Fixed structure anchors on the arena (must match <see cref="MobaStructureSpawner"/>).</summary>
    private static readonly Point BlueTurretAnchor = new(116, 92);
    private static readonly Point RedTurretAnchor = new(116, 173);
    private static readonly Point BlueNexusAnchor = new(116, 44);
    private static readonly Point RedNexusAnchor = new(116, 224);
    private static readonly Point BlueSpawnAnchor = new(116, 60);
    private static readonly Point RedSpawnAnchor = new(116, 205);

    /// <summary>After an enemy champion hits it, the bot stays "in combat" (hunts champions, ignores creeps) for this long.</summary>
    private static readonly TimeSpan CombatMemory = TimeSpan.FromSeconds(5);

    /// <summary>How often each bot writes a [MOBA-AI] position/intent heartbeat line.</summary>
    private static readonly TimeSpan AiHeartbeat = TimeSpan.FromSeconds(4);

    /// <summary>An enemy champion within this many tiles is a fight worth entering.</summary>
    private const int EngageTiles = 12;

    /// <summary>Within this many tiles a fight is "point blank" - turning your back just feeds.</summary>
    private const int PointBlankTiles = 6;

    /// <summary>Need at least this much clearance from the nearest enemy to safely disengage.</summary>
    private const int BreakawayTiles = 4;

    /// <summary>Farthest a bot will chase a champion in Fight state before dropping back to farming.</summary>
    private const int PursueTiles = 18;

    // --- macro thresholds ---
    private const float RecallHpPct = 0.15f;
    private const float FightBailPct = 0.18f;
    private const float RetreatRecoverPct = 0.55f;
    private const int RecallSafeTiles = 18;
    private const int RecallCancelTiles = 13;
    private const double DefendBehindLevels = 8;
    private static readonly TimeSpan RecallChannel = TimeSpan.FromMilliseconds(3500);

    /// <summary>
    /// Max tiles a bot relocates per tick. Player.MoveAsync is an INSTANT teleport, so
    /// without a cap a bot marching to a waypoint 50 tiles away would blink straight into
    /// the enemy spawn. ~3 tiles / 700 ms tick reads as fast walking.
    /// </summary>
    private const int MaxStepTiles = 3;

    private readonly MobaTeam _team;
    private readonly bool _isDummy;
    private Timer? _brain;
    private int _ticking;
    private int _skillCursor;
    private DateTime _nextActionUtc;
    private DateTime _nextDevelopUtc;
    private DateTime _comboResetUtc;
    private DateTime _combatUntilUtc;
    private DateTime _recallStartUtc;
    private int _developSkillCursor;
    private int _comboStep;
    private Player? _aggressor;
    private BotState _state = BotState.Lane;
    private Point _homeSpawn;
    private IReadOnlyList<Point> _lane = Array.Empty<Point>();
    private int _laneIndex;
    private int _laneOffset;

    // --- [MOBA-AI] observability heartbeat ---
    private DateTime _nextAiHeartbeatUtc;
    private DateTime _lastCombatSeenUtc;
    private DateTime _stateEnteredUtc;
    private Point _lastHeartbeatPos;
    private string? _lastClampReason;
    private string? _lastEngageNote;

    /// <summary>Initializes a new instance of the <see cref="MobaBotPlayer"/> class.</summary>
    /// <param name="gameContext">The game context.</param>
    /// <param name="team">The team the bot fights for.</param>
    /// <param name="isDummy">
    /// When <c>true</c> the bot never moves and never attacks - it just stands at its spawn
    /// and keeps itself topped up, so a tester can pound on it and watch the damage log.
    /// </param>
    public MobaBotPlayer(IGameContext gameContext, MobaTeam team, bool isDummy = false)
        : base(gameContext)
    {
        this._team = team;
        this._isDummy = isDummy;
    }

    /// <inheritdoc />
    public override bool RespawnAndContinue => true;

    /// <summary>Gets a value indicating whether this bot is a stationary training dummy.</summary>
    public bool IsDummy => this._isDummy;

    /// <summary>Gets a snapshot of the currently active bots.</summary>
    public static IReadOnlyCollection<MobaBotPlayer> All => ActiveBots.Keys.ToList();

    /// <summary>
    /// Spawns the bot into the arena on its clone character and starts the brain.
    /// </summary>
    /// <param name="account">A shared throwaway account (persistence is suppressed).</param>
    /// <param name="clone">The clone character (built by <see cref="MobaCloneFactory.BuildForClassAsync"/>).</param>
    /// <param name="spawn">Where to place the bot.</param>
    /// <returns><c>true</c> on success.</returns>
    public async ValueTask<bool> StartMobaAsync(Account account, Character clone, Point spawn, int laneOffset = 0)
    {
        try
        {
            this.Account = account;
            this.SuppressPersistence = true;
            this.IsMobaClone = true;
            this.MobaLevel = 1;
            this.MobaExperience = 0;
            this.MobaSkillPoints = 0;
            this.MobaSkillCooldowns.Clear();
            this.MobaSkillGraceEnds.Clear();

            clone.PositionX = spawn.X;
            clone.PositionY = spawn.Y;

            await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.LoginScreen).ConfigureAwait(false);
            await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.Authenticated).ConfigureAwait(false);
            await this.PlayerState.TryAdvanceToAsync(GameLogic.PlayerState.CharacterSelection).ConfigureAwait(false);

            await this.GameContext.AddPlayerAsync(this).ConfigureAwait(false);
            await this.SetSelectedCharacterAsync(clone).ConfigureAwait(false);

            // Full HP/mana BEFORE entering the world: ClientReadyAfterMapChangeAsync adds
            // the bot to the map and marks it alive, and observers snapshot it then - a
            // 0-HP bot at that moment renders as a corpse / not at all.
            MobaCloneFactory.OnCloneAttached(this);

            if (this._isDummy && this.Attributes is { } dummyAttr)
            {
                // No shield on a training dummy: every hit lands fully on HP (classic PvP
                // split), so the tester reads the raw skill damage in the [MOBA-DMG] log.
                dummyAttr.AddElement(new SimpleElement(-dummyAttr[Stats.MaximumShield], AggregateType.AddRaw), Stats.MaximumShield);
                dummyAttr[Stats.CurrentShield] = 0;
            }

            await this.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);

            MobaTeams.Set(this, this._team);
            this.HuntingOrigin = spawn;
            this._homeSpawn = spawn;
            this._lane = MobaWaveSpawner.LaneWaypointsFor(this._team);
            this._laneIndex = 0;
            this._laneOffset = Math.Clamp(laneOffset, -7, 7);

            // Bots skip SelectCharacterAction, so wire the champion-death handler here
            // (kill / assist EXP + K/D/A counters). RespawnAtAsync already snaps the bot
            // back to its lane start.
            this.Died += this.OnBotChampionDied;

            ActiveBots.TryAdd(this, 0);
            this._brain = new Timer(_ => this.SafeTickAsync(), null, TickInterval, TickInterval);
            this.Logger.LogInformation("[MOBA-BOT] '{Name}' ({Team}) spawned at {Pos}.", clone.Name, this._team, spawn);
            return true;
        }
        catch (Exception ex)
        {
            this.Logger.LogError(ex, "[MOBA-BOT] failed to start bot '{Name}'.", clone.Name);
            return false;
        }
    }

    /// <inheritdoc />
    public override async ValueTask StopAsync()
    {
        ActiveBots.TryRemove(this, out _);
        if (this._brain is { } brain)
        {
            this._brain = null;
            await brain.DisposeAsync().ConfigureAwait(false);
        }

        MobaTeams.Clear(this);
        await base.StopAsync().ConfigureAwait(false);
    }

    /// <summary>Stops and removes every active bot.</summary>
    /// <returns>The number of bots removed.</returns>
    public static async ValueTask<int> ClearAllAsync()
    {
        var bots = ActiveBots.Keys.ToList();
        foreach (var bot in bots)
        {
            try
            {
                await bot.StopAsync().ConfigureAwait(false);
            }
            catch
            {
                // best effort
            }
        }

        return bots.Count;
    }

    /// <inheritdoc />
    protected override void StartIntelligence()
    {
        // The bot runs its own brain (StartMobaAsync); no mob-hunting MU-Helper.
    }

    /// <inheritdoc />
    public override async ValueTask RespawnAtAsync(MUnique.OpenMU.DataModel.Configuration.ExitGate gate)
    {
        // The engine respawns offline players at the map's spawn gate (far corner of the
        // arena). Snap the bot straight back to its brawl spot so the fight stays put.
        await base.RespawnAtAsync(gate).ConfigureAwait(false);
        try
        {
            this._laneIndex = 0; // restart the lane march from our creep spawn
            // Fresh start: drop any stale combat memory / target so the bot laves cleanly
            // instead of flip-flopping Fight<->Lane chasing a ghost from its last life.
            this._combatUntilUtc = default;
            this._aggressor = null;
            this._state = BotState.Lane;
            this._recallStartUtc = default;
            this._comboStep = 0;
            await this.MoveAsync(this._homeSpawn).ConfigureAwait(false);
        }
        catch
        {
            // best effort
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Timer callback.")]
    private async void SafeTickAsync()
    {
        if (Interlocked.Exchange(ref this._ticking, 1) == 1)
        {
            return;
        }

        try
        {
            await this.TickAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            this.Logger.LogWarning(ex, "[MOBA-BOT] tick error.");
        }
        finally
        {
            Interlocked.Exchange(ref this._ticking, 0);
        }
    }

    private async ValueTask TickAsync()
    {
        if (this.SelectedCharacter is null || this.CurrentMap is not { } map)
        {
            return;
        }

        // Spend earned points even while dead / respawning, so a bot on the losing team
        // that barely lives still keeps its level's worth of stats and skill ranks.
        if (!this._isDummy)
        {
            this.DevelopIfDue();
        }

        if (!this.IsAlive)
        {
            return;
        }

        if (this._isDummy)
        {
            // Training dummy: never move, never attack. Keep HP/mana/shield full so it
            // survives an endless barrage and the tester can read sustained DPS.
            if (this.Attributes is { } a)
            {
                a[Stats.CurrentHealth] = a[Stats.MaximumHealth];
                a[Stats.CurrentMana] = a[Stats.MaximumMana];
                a[Stats.CurrentShield] = a[Stats.MaximumShield];
            }

            return;
        }

        if (this.Attributes?[Stats.IsStunned] > 0 || this.Attributes?[Stats.IsFrozen] > 0)
        {
            return;
        }

        var pos = this.Position;
        var now = DateTime.UtcNow;
        var ctx = this.BuildContext(map, pos, now);

        // --- macro state machine ---------------------------------------------------------
        var prevState = this._state;
        this._state = this.DecideState(ctx);

        if (this._state != prevState)
        {
            var heldFor = this._stateEnteredUtc == default ? 0.0 : (now - this._stateEnteredUtc).TotalSeconds;
            this._stateEnteredUtc = now;
            this.Logger.LogInformation(
                "[MOBA-AI] {Name} {From}({Held:F1}s)->{To} @ {X},{Y} hp={Hp:P0} | enemyNear={En}(nearest {NearDist:F0}t) allyNear={Al} inCombat={Ic} allyPow={AllyPow:F0} enemyPow={EnemyPow:F0} allyLv={AllyLv:F1} enemyLv={EnemyLv:F1} deadAllies={Dead} frontStruct={Struct} waveFront={Wave} alliedCreeps={Creeps}",
                this.Name,
                prevState,
                heldFor,
                this._state,
                pos.X,
                pos.Y,
                ctx.HpPct,
                ctx.EnemyChampsNear.Count,
                ctx.EnemyChampsInRange.Count == 0 ? 999 : ctx.EnemyChampsInRange.Min(e => e.GetDistanceTo(pos)),
                ctx.AllyChampsNear.Count,
                ctx.InCombat,
                ctx.AllyPower,
                ctx.EnemyPower,
                ctx.AllyAvgLevel,
                ctx.EnemyAvgLevel,
                ctx.AllyDeadCount,
                ctx.FrontEnemyStructure is { } fs ? $"{fs.Position.X},{fs.Position.Y}" : "none",
                ctx.WaveAtFront,
                ctx.AlliedCreepsAtPos);
        }

        // Throttled heartbeat: where the bot is and what it intends, even when the state
        // is not changing (this is how "stands under the turret waiting" shows up).
        if (now >= this._nextAiHeartbeatUtc)
        {
            this._nextAiHeartbeatUtc = now + AiHeartbeat;
            var movedTiles = pos.EuclideanDistanceTo(this._lastHeartbeatPos);
            var idleFor = MobaCombatLog.InCombat(this, TimeSpan.FromSeconds(1)) ? 0.0 : (now - this._lastCombatSeenUtc).TotalSeconds;
            if (MobaCombatLog.InCombat(this, TimeSpan.FromSeconds(2)))
            {
                this._lastCombatSeenUtc = now;
            }

            this.Logger.LogInformation(
                "[MOBA-AI] {Name} @ {X},{Y} state={State} hp={Hp:P0} moved={Moved:F1}t/{Beat}s idle~{Idle:F0}s enemyNear={En}",
                this.Name,
                pos.X,
                pos.Y,
                this._state,
                ctx.HpPct,
                movedTiles,
                (int)AiHeartbeat.TotalSeconds,
                idleFor,
                ctx.EnemyChampsNear.Count);

            if (movedTiles < 1.0 && idleFor > 5.0 && this._state is BotState.Lane or BotState.GroupPush or BotState.Fight)
            {
                this.Logger.LogWarning(
                    "[MOBA-AI] {Name} IDLE {Idle:F0}s @ {X},{Y} state={State} - not moving, not fighting",
                    this.Name,
                    idleFor,
                    pos.X,
                    pos.Y,
                    this._state);
            }

            this._lastHeartbeatPos = pos;
        }

        switch (this._state)
        {
            case BotState.Recalling:
                await this.TickRecallAsync(ctx).ConfigureAwait(false);
                return;
            case BotState.Retreat:
                await this.TickRetreatAsync(ctx).ConfigureAwait(false);
                return;
            case BotState.DefendBase:
                await this.TickDefendAsync(ctx).ConfigureAwait(false);
                return;
            case BotState.Fight:
                await this.TickFightAsync(ctx).ConfigureAwait(false);
                return;
            case BotState.GroupPush:
                await this.TickPushAsync(ctx).ConfigureAwait(false);
                return;
            default:
                await this.TickLaneAsync(ctx).ConfigureAwait(false);
                return;
        }
    }

    // ==================================================================================
    //  Macro brain
    // ==================================================================================
    private enum BotState
    {
        Lane,
        Fight,
        GroupPush,
        DefendBase,
        Retreat,
        Recalling,
    }

    private readonly record struct BotContext(
        GameMap Map,
        Point Pos,
        DateTime Now,
        float HpPct,
        List<Player> EnemyChampsInRange,
        List<Player> AllyChampsNear,
        List<Player> EnemyChampsNear,
        double AllyPower,
        double EnemyPower,
        double AllyAvgLevel,
        double EnemyAvgLevel,
        int AllyDeadCount,
        bool InCombat,
        Player? Aggressor,
        NPC.Monster? FrontEnemyStructure,
        bool AlliedCreepsAtPos,
        bool WaveAtFront);

    private BotContext BuildContext(GameMap map, Point pos, DateTime now)
    {
        var attackRange = Math.Max(2, this.GetMeleeAttackRange());

        var enemiesInAcquire = map.GetAttackablesInRange(pos, AcquireRangeTiles)
            .Where(a => a.IsAlive && !ReferenceEquals(a, this) && MobaTeams.AreEnemies(this, a))
            .ToList();

        var enemyChampsInRange = enemiesInAcquire.OfType<Player>()
            .Where(p => p is not MobaBotPlayer { IsDummy: true })
            .ToList();

        // Everyone in the match, split by side and by "near me" (a local fight radius).
        var everyone = map.GetAttackablesInRange(new Point(128, 128), 400).OfType<Player>()
            .Where(p => p.IsMobaClone && p is not MobaBotPlayer { IsDummy: true })
            .ToList();
        var myTeam = MobaTeams.GetTeam(this);
        var allies = everyone.Where(p => !ReferenceEquals(p, this) && MobaTeams.GetTeam(p) == myTeam).ToList();
        var enemies = everyone.Where(p => MobaTeams.AreEnemies(this, p)).ToList();

        const double LocalFightTiles = 18;
        var allyNear = allies.Where(p => p.IsAlive && p.GetDistanceTo(pos) <= LocalFightTiles).Append(this).ToList();
        var enemyNear = enemies.Where(p => p.IsAlive && p.GetDistanceTo(pos) <= LocalFightTiles).ToList();

        var recentAttackers = MobaCombatLog.RecentAttackersOf(this, CombatMemory);
        var aggressor = recentAttackers.OfType<Player>()
            .FirstOrDefault(p => p.IsAlive && MobaTeams.AreEnemies(this, p) && p is not MobaBotPlayer { IsDummy: true });
        if (aggressor is not null)
        {
            this._combatUntilUtc = now + CombatMemory;
            this._aggressor = aggressor;
        }

        // The FRONT enemy objective: the live enemy structure nearest to mid (turret before
        // nexus), not just whatever the bot is standing next to.
        var frontEnemyStructure = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<NPC.Monster>()
            .Where(m => m.IsAlive && MobaStructures.IsStructure(m) && MobaTeams.AreEnemies(this, m))
            .OrderBy(m => Math.Abs(m.Position.Y - 128))
            .FirstOrDefault();

        var alliedCreepsAtPos = map.GetAttackablesInRange(pos, TurretDangerTiles + 2)
            .OfType<NPC.Monster>()
            .Any(m => !MobaStructures.IsStructure(m) && MobaTeams.AreAllies(this, m));

        // A friendly wave is at the front objective (needed to actually push a turret).
        var waveAtFront = frontEnemyStructure is { } fs
            && map.GetAttackablesInRange(fs.Position, TurretDangerTiles + 3)
                .OfType<NPC.Monster>()
                .Any(m => !MobaStructures.IsStructure(m) && MobaTeams.AreAllies(this, m));

        var a = this.Attributes;
        var hpPct = a is null ? 1f : Math.Clamp(a[Stats.CurrentHealth] / Math.Max(1f, a[Stats.MaximumHealth]), 0f, 1f);

        return new BotContext(
            map,
            pos,
            now,
            hpPct,
            enemyChampsInRange,
            allyNear,
            enemyNear,
            Power(allyNear),
            Power(enemyNear),
            allies.Append(this).Average(p => p.MobaLevel),
            enemies.Count > 0 ? enemies.Average(p => p.MobaLevel) : this.MobaLevel,
            allies.Count(p => !p.IsAlive),
            now < this._combatUntilUtc,
            this._aggressor,
            frontEnemyStructure,
            alliedCreepsAtPos,
            waveAtFront);

        static double Power(IEnumerable<Player> champs) => champs.Sum(p =>
        {
            var aa = p.Attributes;
            var hp = aa is null ? 1f : Math.Clamp(aa[Stats.CurrentHealth] / Math.Max(1f, aa[Stats.MaximumHealth]), 0f, 1f);
            return Math.Max(1, p.MobaLevel) * (0.4 + (0.6 * hp));
        });
    }

    private BotState DecideState(BotContext c)
    {
        // Already channelling a recall: keep it unless an enemy champion gets close
        // (cancel -> retreat) or it is done.
        if (this._state == BotState.Recalling)
        {
            if (c.EnemyChampsNear.Any(e => e.GetDistanceTo(c.Pos) <= RecallCancelTiles))
            {
                return BotState.Retreat;
            }

            return BotState.Recalling;
        }

        // The nearest enemy champion that actually matters right now.
        var nearestEnemyDist = c.EnemyChampsInRange.Count == 0
            ? double.MaxValue
            : c.EnemyChampsInRange.Min(e => e.GetDistanceTo(c.Pos));

        // "committed" = we are trading blows AND an enemy is close enough that turning our
        // back just feeds. You finish the trade / peel - you do not run while being hit.
        var committed = c.InCombat && nearestEnemyDist <= PointBlankTiles;

        // A fight worth entering: an enemy champ is genuinely in engage range, OR we are
        // already trading and one is still within reach. Stale combat memory with nobody
        // around does NOT count (that was the Fight<->Lane flip-flop).
        var forcedFight = nearestEnemyDist <= EngageTiles
            || (c.InCombat && nearestEnemyDist <= AcquireRangeTiles);

        // Critically low and able to break away -> get out. If already safe, recall.
        if (c.HpPct <= RecallHpPct && !committed)
        {
            return this.IsSafeSpot(c) ? BotState.Recalling : BotState.Retreat;
        }

        // Team is being run over: play defence at our own turret, never walk out to feed.
        if (c.EnemyAvgLevel - c.AllyAvgLevel >= DefendBehindLevels)
        {
            return forcedFight && c.HpPct > FightBailPct ? BotState.Fight : BotState.DefendBase;
        }

        // Two-plus allies dead -> don't START anything new; but if we're already committed,
        // fight it out (running is worse).
        if (c.AllyDeadCount >= 2 && !committed)
        {
            return BotState.Lane;
        }

        if (committed)
        {
            // Only bail a committed fight if we're nearly dead AND can actually disengage
            // (no enemy in melee). Losing a team-mate / a bad power ratio is NOT a reason
            // to turn around mid-trade.
            if (c.HpPct <= FightBailPct && nearestEnemyDist > BreakawayTiles)
            {
                return BotState.Retreat;
            }

            return BotState.Fight;
        }

        if (forcedFight)
        {
            // Not yet locked in: we can still decline a clearly lost fight and keep farming.
            if (c.EnemyPower > c.AllyPower * 1.4 && c.HpPct < 0.6)
            {
                return BotState.Lane;
            }

            return BotState.Fight;
        }

        // Nobody to fight: push the front objective as long as we have creeps with us (at
        // the turret OR right next to us on the way in) and we're not badly behind.
        if (c.FrontEnemyStructure is not null
            && (c.WaveAtFront || c.AlliedCreepsAtPos)
            && c.AllyAvgLevel >= c.EnemyAvgLevel - 3
            && !c.EnemyChampsNear.Any())
        {
            return BotState.GroupPush;
        }

        return BotState.Lane;
    }

    /// <summary>A spot is safe to start / continue a recall: our own half of the lane and no enemy champion nearby.</summary>
    private bool IsSafeSpot(BotContext c)
    {
        var ownHalf = MobaTeams.GetTeam(this) == MobaTeam.Blue ? c.Pos.Y < 122 : c.Pos.Y > 134;
        var noEnemyClose = !c.EnemyChampsNear.Any(e => e.GetDistanceTo(c.Pos) <= RecallSafeTiles);
        return ownHalf && noEnemyClose;
    }

    private async ValueTask TickRecallAsync(BotContext c)
    {
        if (this._recallStartUtc == default)
        {
            this._recallStartUtc = c.Now;
        }

        // Stand still and channel. Done -> blink home, full heal, back to lane.
        if (c.Now - this._recallStartUtc >= RecallChannel)
        {
            this._recallStartUtc = default;
            this._laneIndex = 0;
            if (this.Attributes is { } a)
            {
                a[Stats.CurrentHealth] = a[Stats.MaximumHealth];
                a[Stats.CurrentMana] = a[Stats.MaximumMana];
                a[Stats.CurrentShield] = a[Stats.MaximumShield];
            }

            await this.MoveAsync(this._homeSpawn).ConfigureAwait(false);
            this._state = BotState.Lane;
        }
    }

    private async ValueTask TickRetreatAsync(BotContext c)
    {
        this._recallStartUtc = default;

        // Recovered enough and safe -> resume laning.
        if (c.HpPct >= RetreatRecoverPct && this.IsSafeSpot(c))
        {
            this._state = BotState.Lane;
            await this.TickLaneAsync(c).ConfigureAwait(false);
            return;
        }

        // Reached a safe spot while still hurt -> recall.
        if (c.HpPct < RecallHpPct * 2 && this.IsSafeSpot(c))
        {
            this._state = BotState.Recalling;
            this._recallStartUtc = c.Now;
            return;
        }

        await this.WalkTowardAsync(this._homeSpawn).ConfigureAwait(false);
    }

    private async ValueTask TickDefendAsync(BotContext c)
    {
        var myTurret = this.OwnFrontTurret(c.Map) ?? (IAttackable?)null;
        var anchor = myTurret?.Position ?? this._homeSpawn;

        // Fight only what walks into our turret's shadow; never chase out.
        var target = c.EnemyChampsInRange
            .Where(e => e.GetDistanceTo(anchor) <= TurretDangerTiles + 4)
            .OrderBy(e => e.Attributes?[Stats.CurrentHealth] ?? float.MaxValue)
            .FirstOrDefault() as IAttackable
            ?? c.Map.GetAttackablesInRange(c.Pos, this.GetMeleeAttackRange() + 1)
                .OfType<NPC.Monster>().FirstOrDefault(m => !MobaStructures.IsStructure(m) && MobaTeams.AreEnemies(this, m));

        if (target is not null)
        {
            await this.EngageAsync(c, target).ConfigureAwait(false);
            return;
        }

        if (c.Pos.EuclideanDistanceTo(anchor) > 6)
        {
            await this.WalkTowardAsync(anchor).ConfigureAwait(false);
        }
    }

    private async ValueTask TickFightAsync(BotContext c)
    {
        // Shared focus: everyone piles the lowest-effective-HP enemy champion in range,
        // falling back to the aggressor, then nearest.
        var focus = c.EnemyChampsInRange
            .OrderBy(e => (e.Attributes?[Stats.CurrentHealth] ?? float.MaxValue))
            .FirstOrDefault() as IAttackable
            ?? (this._aggressor is { IsAlive: true } agg && agg.GetDistanceTo(c.Pos) <= AcquireRangeTiles ? agg : null)
            ?? c.EnemyChampsNear.OrderBy(e => e.GetDistanceTo(c.Pos)).FirstOrDefault();

        // Peel: if an allied carry near me is low and someone is on them, switch to that attacker.
        var alliedCarryUnderThreat = c.AllyChampsNear
            .Where(ally => !ReferenceEquals(ally, this)
                           && MobaPassives.FamilyOf(ally) is MobaFamily.Elf or MobaFamily.Wizard or MobaFamily.Summoner
                           && (ally.Attributes?[Stats.CurrentHealth] ?? 1f) / Math.Max(1f, ally.Attributes?[Stats.MaximumHealth] ?? 1f) < 0.4f)
            .SelectMany(ally => MobaCombatLog.RecentAttackersOf(ally, TimeSpan.FromSeconds(2)).OfType<Player>())
            .FirstOrDefault(atk => atk.IsAlive && MobaTeams.AreEnemies(this, atk) && atk.GetDistanceTo(c.Pos) <= AcquireRangeTiles);
        if (alliedCarryUnderThreat is not null)
        {
            focus = alliedCarryUnderThreat;
        }

        // Nothing worth chasing (no champion, or the only one is way out of reach) -> go
        // back to farming instead of walking blindly across the lane ignoring creeps.
        if (focus is null || focus.GetDistanceTo(c.Pos) > PursueTiles)
        {
            this._state = BotState.Lane;
            await this.TickLaneAsync(c).ConfigureAwait(false);
            return;
        }

        await this.EngageAsync(c, focus).ConfigureAwait(false);
    }

    private async ValueTask TickPushAsync(BotContext c)
    {
        var structure = c.FrontEnemyStructure;
        if (structure is null || !(c.WaveAtFront || c.AlliedCreepsAtPos))
        {
            this._state = BotState.Lane;
            await this.TickLaneAsync(c).ConfigureAwait(false);
            return;
        }

        // Any enemy champ shows up -> stop pushing, fight.
        if (c.EnemyChampsNear.Count > 0)
        {
            this._state = BotState.Fight;
            await this.TickFightAsync(c).ConfigureAwait(false);
            return;
        }

        await this.EngageAsync(c, structure).ConfigureAwait(false);
    }

    private async ValueTask TickLaneAsync(BotContext c)
    {
        // Behind on levels -> hold at mid, don't push into the enemy half.
        var behind = c.AllyAvgLevel < c.EnemyAvgLevel - 3;

        var target = c.Map.GetAttackablesInRange(c.Pos, AcquireRangeTiles)
            .Where(a => a.IsAlive && !ReferenceEquals(a, this) && MobaTeams.AreEnemies(this, a))
            .OfType<NPC.Monster>()
            .Where(m => !MobaStructures.IsStructure(m))
            .OrderBy(m => m.GetDistanceTo(c.Pos))
            .FirstOrDefault() as IAttackable;

        if (target is null && !behind)
        {
            target = c.EnemyChampsInRange.OfType<MobaBotPlayer>().OrderBy(b => b.GetDistanceTo(c.Pos)).FirstOrDefault();
        }

        // No creeps / bots to hit but our wave is here and the front structure is close:
        // chip the turret instead of standing around (this is what "waits under turret" was).
        if (target is null && !behind
            && c.FrontEnemyStructure is { } frontStruct
            && c.AlliedCreepsAtPos
            && frontStruct.GetDistanceTo(c.Pos) <= AcquireRangeTiles)
        {
            await this.EngageAsync(c, frontStruct).ConfigureAwait(false);
            return;
        }

        if (target is not null)
        {
            // Under an enemy turret without a wave -> back off.
            if (c.FrontEnemyStructure is { } t
                && t.GetDistanceTo(c.Pos) <= TurretDangerTiles
                && !c.AlliedCreepsAtPos)
            {
                await this.WalkTowardAsync(this._homeSpawn).ConfigureAwait(false);
                return;
            }

            await this.EngageAsync(c, target).ConfigureAwait(false);
            return;
        }

        if (behind)
        {
            // Hold near mid / our side of it.
            var holdY = MobaTeams.GetTeam(this) == MobaTeam.Blue ? (byte)120 : (byte)136;
            var hold = new Point((byte)Math.Clamp(116 + this._laneOffset, 5, 250), holdY);
            if (c.Pos.EuclideanDistanceTo(hold) > 4)
            {
                await this.WalkTowardAsync(hold).ConfigureAwait(false);
            }

            return;
        }

        await this.MarchLaneAsync(c.Pos).ConfigureAwait(false);
    }

    /// <summary>Logs (deduped) what EngageAsync is doing / why it is not attacking - helps macro tuning.</summary>
    private void EngageNote(string note, IAttackable target)
    {
        if (this._lastEngageNote == note)
        {
            return;
        }

        this._lastEngageNote = note;
        this.Logger.LogInformation(
            "[MOBA-AI] {Name} engage: {Note} (target {Target} @ {Tx},{Ty})",
            this.Name,
            note,
            (target as Player)?.Name ?? (target as NPC.Monster)?.Definition?.Designation ?? "?",
            target.Position.X,
            target.Position.Y);
    }

    /// <summary>Approach to attack range (kiting for ranged classes) then cast / basic attack.</summary>
    private async ValueTask EngageAsync(BotContext c, IAttackable target)
    {
        var pos = c.Pos;
        var dist = target.GetDistanceTo(pos);
        var range = Math.Max(2, this.GetMeleeAttackRange());
        var ranged = range >= 5;

        // Don't tower-dive: if we're chasing a CHAMPION that is next to a live enemy
        // turret, and we don't have a wave + level lead to make it safe, hold at the
        // turret's edge (or step out if we're already in its shadow) instead of walking in.
        if (target is Player && c.FrontEnemyStructure is { IsAlive: true } turret)
        {
            var toTurret = turret.GetDistanceTo(pos);
            var targetUnderTurret = turret.GetDistanceTo(target.Position) <= TurretDangerTiles;
            var safeDive = c.WaveAtFront && c.AllyAvgLevel >= c.EnemyAvgLevel + 2 && c.AllyPower > c.EnemyPower * 1.3;
            if (targetUnderTurret && !safeDive)
            {
                if (toTurret <= TurretDangerTiles + 1)
                {
                    await this.WalkTowardAsync(StepAway(pos, turret.Position, 4)).ConfigureAwait(false);
                }
                else if (dist > range)
                {
                    // Hold: creep to the turret edge but no closer.
                    var edge = StepAway(target.Position, turret.Position, TurretDangerTiles + 2);
                    if (edge.EuclideanDistanceTo(pos) > 2)
                    {
                        await this.WalkTowardAsync(edge).ConfigureAwait(false);
                    }
                }

                if (dist > range)
                {
                    this.EngageNote("tower-dive hold at edge", target);
                    return;
                }
            }
        }

        if (dist > range)
        {
            // Opportunistic last-hit: while closing on the real target, if an enemy creep
            // is already in our face, hit it instead of eating free creep damage. This is
            // what stops the "creeps beat on the bot and it never hits back" behaviour.
            if (DateTime.UtcNow >= this._nextActionUtc
                && this.CurrentMap is { } m
                && m.GetAttackablesInRange(pos, range)
                    .OfType<NPC.Monster>()
                    .FirstOrDefault(x => x.IsAlive && !MobaStructures.IsStructure(x) && MobaTeams.AreEnemies(this, x)) is { } creep)
            {
                this._nextActionUtc = DateTime.UtcNow + ActionCooldown;
                this.EngageNote("last-hit creep while closing", creep);
                await this.CastNextOrAttackAsync(creep).ConfigureAwait(false);
                return;
            }

            this.EngageNote($"walking in (dist {dist:F0} > range {range})", target);
            await this.WalkTowardAsync(target.Position).ConfigureAwait(false);
            return;
        }

        // Ranged kiting: if the target is basically on top of us and we can act again
        // soon, take a step back toward our side instead of standing still.
        if (ranged && dist < range - 2 && DateTime.UtcNow < this._nextActionUtc && target is Player)
        {
            var away = StepAway(pos, target.Position, 3);
            this.EngageNote("kiting back", target);
            await this.WalkTowardAsync(away).ConfigureAwait(false);
            return;
        }

        if (DateTime.UtcNow < this._nextActionUtc)
        {
            this.EngageNote("waiting on action cooldown", target);
            return;
        }

        var agi = this.Attributes is { } a2 ? Math.Max(0, a2[Stats.TotalAgility] - MobaCloneFactory.BaselineStatValue) : 0f;
        var speedFactor = Math.Clamp(1.0 - (agi / 60000.0), 0.45, 1.0);
        this._nextActionUtc = DateTime.UtcNow + (ActionCooldown * speedFactor);
        this.EngageNote($"attacking (dist {dist:F0}, range {range})", target);
        await this.CastNextOrAttackAsync(target).ConfigureAwait(false);
    }

    private NPC.Monster? OwnFrontTurret(GameMap map)
    {
        var myTeam = MobaTeams.GetTeam(this);
        var turrets = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<NPC.Monster>()
            .Where(m => m.IsAlive && MobaStructures.IsStructure(m) && MobaTeams.GetTeam(m) == myTeam)
            .ToList();

        // "Front" = closest turret to mid (highest Y for Blue in the north, lowest for Red).
        return myTeam == MobaTeam.Blue
            ? turrets.OrderByDescending(t => t.Position.Y).FirstOrDefault()
            : turrets.OrderBy(t => t.Position.Y).FirstOrDefault();
    }

    private static Point StepAway(Point from, Point threat, int tiles)
    {
        var dx = from.X - threat.X;
        var dy = from.Y - threat.Y;
        var len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 0.1)
        {
            return from;
        }

        var nx = (int)Math.Round(from.X + (dx / len * tiles));
        var ny = (int)Math.Round(from.Y + (dy / len * tiles));
        return new Point((byte)Math.Clamp(nx, 5, 250), (byte)Math.Clamp(ny, 5, 250));
    }

    private void OnBotChampionDied(object? sender, DeathInformation death)
    {
        _ = MobaXp.HandleChampionDeathAsync(this, death);
    }

    /// <summary>Walks along the lane waypoints toward the enemy creep spawn, spread out by lane offset.</summary>
    /// <param name="pos">Current position.</param>
    private async ValueTask MarchLaneAsync(Point pos)
    {
        if (this._lane.Count == 0 || this.IsWalking || this.CurrentMap is not { } map)
        {
            return;
        }

        while (this._laneIndex < this._lane.Count - 1
               && this._lane[this._laneIndex].EuclideanDistanceTo(pos) <= WaypointReachedTiles + 4)
        {
            this._laneIndex++;
        }

        var wp = this._lane[this._laneIndex];

        // Do not walk past a LIVE enemy turret without a friendly wave - hold just short of
        // it (this is what keeps a winning team from marching straight into the enemy base).
        var frontTurret = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<NPC.Monster>()
            .Where(m => m.IsAlive && MobaStructures.IsStructure(m) && MobaTeams.AreEnemies(this, m))
            .OrderBy(m => Math.Abs(m.Position.Y - 128))
            .FirstOrDefault();
        if (frontTurret is not null)
        {
            var goingSouth = MobaTeams.GetTeam(this) == MobaTeam.Blue;
            var turretY = frontTurret.Position.Y;
            var beyond = goingSouth ? wp.Y > turretY - (TurretDangerTiles - 1) : wp.Y < turretY + (TurretDangerTiles - 1);
            var wave = map.GetAttackablesInRange(new Point(116, turretY), TurretDangerTiles + 3)
                .OfType<NPC.Monster>()
                .Any(m => !MobaStructures.IsStructure(m) && MobaTeams.AreAllies(this, m));
            if (beyond && !wave)
            {
                var holdY = (byte)(goingSouth ? turretY - (TurretDangerTiles + 1) : turretY + (TurretDangerTiles + 1));
                wp = new Point(wp.X, holdY);
            }
        }

        // Spread the bots across the lane width instead of all stacking on the x=116 column.
        var next = new Point(
            (byte)Math.Clamp(wp.X + this._laneOffset, 5, 250),
            wp.Y);

        if (next.EuclideanDistanceTo(pos) > 1.5)
        {
            await this.WalkTowardAsync(next).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Starts an animated walk toward <paramref name="target"/> - a short chunk of 1-tile
    /// steps built straight toward it (same simple approach the lane creeps use, which is
    /// known to work on the arena map), NOT the instant MoveAsync teleport. No-op while
    /// already walking so the step plays out.
    /// </summary>
    private async ValueTask WalkTowardAsync(Point target)
    {
        if (this.IsWalking || this.CurrentMap is not { } map || this.Position.EuclideanDistanceTo(target) < 1.0)
        {
            return;
        }

        // Every bot movement is funnelled through here, so this is where the "respect
        // turrets and waves" rule lives: no bot ever paths past a live enemy front turret
        // without an allied wave tanking it, and no bot ever paths into the enemy fountain.
        target = this.ClampToLaneLimit(target, map);
        if (this.Position.EuclideanDistanceTo(target) < 1.0)
        {
            return;
        }

        if (map.Terrain?.AIgrid is not { } grid)
        {
            // No AI grid: fall back to an instant hop so the bot at least isn't frozen.
            await this.MoveAsync(target).ConfigureAwait(false);
            return;
        }

        const int maxSteps = 8;
        var steps = new List<WalkingStep>(maxSteps);
        var cursor = this.Position;
        for (var i = 0; i < maxSteps && cursor != target; i++)
        {
            var dx = Math.Sign(target.X - cursor.X);
            var dy = Math.Sign(target.Y - cursor.Y);
            Point? Try(int sx, int sy)
            {
                if (sx == 0 && sy == 0)
                {
                    return null;
                }

                var p = new Point((byte)(cursor.X + sx), (byte)(cursor.Y + sy));
                return grid[p.X, p.Y] != 0 ? p : (Point?)null;
            }

            var next = Try(dx, dy)
                ?? (dx != 0 && dy != 0 ? Try(dx, 0) ?? Try(0, dy) : null)
                ?? (dx == 0 ? Try(1, dy) ?? Try(-1, dy) : null)
                ?? (dy == 0 ? Try(dx, 1) ?? Try(dx, -1) : null);

            if (next is not { } step)
            {
                break;
            }

            steps.Add(new WalkingStep(cursor, step, cursor.GetDirectionTo(step)));
            cursor = step;
        }

        if (steps.Count == 0)
        {
            return;
        }

        await this.WalkToAsync(cursor, steps.ToArray()).ConfigureAwait(false);
    }

    /// <summary>
    /// Pulls a desired destination back so the bot never over-extends. The hard rule: while a
    /// LIVE enemy front turret stands, a bot may approach it (to the turret edge without an
    /// allied wave, or just far enough past it to body it WITH a wave) but never run past it
    /// deeper into enemy territory. Only once that turret is dead does the front line advance,
    /// and the enemy fountain (nexus / spawn) is always off-limits. Moves toward the bot's OWN
    /// side are never clamped (retreat / recall / defend stay valid).
    /// </summary>
    private Point ClampToLaneLimit(Point target, GameMap map)
    {
        var team = MobaTeams.GetTeam(this);
        if (team == MobaTeam.None)
        {
            return target;
        }

        var goingSouth = team == MobaTeam.Blue; // Blue advances toward higher Y, Red toward lower Y.
        var enemyTurretAnchor = goingSouth ? RedTurretAnchor : BlueTurretAnchor;
        var enemyNexus = goingSouth ? RedNexusAnchor : BlueNexusAnchor;
        var enemySpawn = goingSouth ? RedSpawnAnchor : BlueSpawnAnchor;

        var enemyStructures = map.GetAttackablesInRange(new Point(128, 128), 400)
            .OfType<NPC.Monster>()
            .Where(m => m.IsAlive && MobaStructures.IsStructure(m) && MobaTeams.AreEnemies(this, m))
            .ToList();

        // Front enemy turret = live enemy turret closest to mid (the nexus sits far behind, |Y-128| ~ 96).
        var frontTurret = enemyStructures
            .Where(m => Math.Abs(m.Position.Y - 128) < 60)
            .OrderBy(m => Math.Abs(m.Position.Y - 128))
            .FirstOrDefault();

        int limitY;
        if (frontTurret is { } turret)
        {
            // Turret alive: the wave only decides whether we stop SHORT of it or may body it -
            // never a pass to run past.
            var waveTanking = map.GetAttackablesInRange(turret.Position, TurretDangerTiles + 3)
                .OfType<NPC.Monster>()
                .Any(m => !MobaStructures.IsStructure(m) && MobaTeams.AreAllies(this, m));
            var margin = waveTanking ? TurretBodyMargin : -LaneLimitMargin;
            limitY = goingSouth ? turret.Position.Y + margin : turret.Position.Y - margin;
        }
        else
        {
            // Front turret is down. Advance to just short of the enemy fountain, but only with
            // a wave past the old turret line; the nexus siege itself isn't a bot job yet.
            var wavePastRuin = map.GetAttackablesInRange(enemyTurretAnchor, TurretDangerTiles + 3)
                .OfType<NPC.Monster>()
                .Any(m => !MobaStructures.IsStructure(m) && MobaTeams.AreAllies(this, m));
            limitY = wavePastRuin
                ? (goingSouth ? enemySpawn.Y - FountainExclusionTiles : enemySpawn.Y + FountainExclusionTiles)
                : (goingSouth ? enemyTurretAnchor.Y - LaneLimitMargin : enemyTurretAnchor.Y + LaneLimitMargin);
        }

        // The enemy fountain (nexus + spawn) is ALWAYS off-limits - never dive it for respawn kills.
        var nexusLimit = goingSouth ? enemyNexus.Y - FountainExclusionTiles : enemyNexus.Y + FountainExclusionTiles;
        var spawnLimit = goingSouth ? enemySpawn.Y - FountainExclusionTiles : enemySpawn.Y + FountainExclusionTiles;
        limitY = goingSouth
            ? Math.Min(limitY, Math.Min(nexusLimit, spawnLimit))
            : Math.Max(limitY, Math.Max(nexusLimit, spawnLimit));

        var clampedY = goingSouth ? Math.Min(target.Y, limitY) : Math.Max(target.Y, limitY);
        if (clampedY != target.Y)
        {
            var reason = frontTurret is not null ? "front-turret" : "fountain/ruin";
            var key = $"{reason}:{limitY}";
            if (this._lastClampReason != key)
            {
                this._lastClampReason = key;
                this.Logger.LogInformation(
                    "[MOBA-AI] {Name} movement clamped {Tx},{Ty} -> {Cx},{Cy} (limitY={Limit}, {Reason})",
                    this.Name,
                    target.X,
                    target.Y,
                    target.X,
                    clampedY,
                    limitY,
                    reason);
            }

            target = new Point(target.X, (byte)Math.Clamp(clampedY, 5, 250));
        }

        return target;
    }

    /// <summary>
    /// Spends the bot's accrued champion skill points and stat points as it levels, so a
    /// developed bot is a real "full build" opponent to fight against. Skill points rank
    /// the loadout abilities round-robin toward the cap; stat points dump into the class's
    /// primary stat (up to <see cref="MobaStatEconomy.MaxPerStat"/>).
    /// </summary>
    private void DevelopIfDue()
    {
        var now = DateTime.UtcNow;
        if (now < this._nextDevelopUtc)
        {
            return;
        }

        this._nextDevelopUtc = now + TimeSpan.FromSeconds(1.5);

        if (this.SelectedCharacter is not { } character || this.Attributes is not { } attributes)
        {
            return;
        }

        // Rank up loadout skills round-robin.
        var learned = character.LearnedSkills
            .Where(s => s.Skill is { } sk && (int)sk.SkillType <= (int)SkillType.AreaSkillExplicitTarget && s.Level < MobaSkills.SkillLevelCap)
            .ToList();
        var guard = 0;
        while (this.MobaSkillPoints > 0 && learned.Count > 0 && guard++ < 64)
        {
            var entry = learned[this._developSkillCursor % learned.Count];
            this._developSkillCursor++;
            if (MobaSkills.TryLevelUp(this, entry.Skill!.Number) != MobaSkills.SkillUpResult.Ok)
            {
                learned.Remove(entry);
            }
        }

        // Dump stat points into the primary stat.
        var available = (int)Math.Max(0, character.LevelUpPoints);
        if (available > 0)
        {
            var primary = MobaPassives.FamilyOf(this) switch
            {
                MobaFamily.Knight or MobaFamily.RageFighter => Stats.BaseStrength,
                MobaFamily.Elf => Stats.BaseAgility,
                MobaFamily.DarkLord => Stats.BaseLeadership,
                _ => Stats.BaseEnergy,
            };

            var invested = (int)Math.Round(attributes[primary] - MobaCloneFactory.BaselineStatValue);
            var room = Math.Max(0, MobaStatEconomy.MaxPerStat - invested);
            var applied = Math.Min(available, room);
            if (applied > 0)
            {
                attributes[primary] += applied;
                character.LevelUpPoints -= applied;
            }
        }
    }

    // Blade Knight combo: step 1 (a basic slash) -> step 2 (a heavy) -> step 3 (Twisting
    // Slash / Rageful Blow / Death Stab, which lands the combo hit), all within 3s.
    private static readonly short[] ComboStep1 = { 23, 22, 20, 19, 21 };
    private static readonly short[] ComboStep2 = { 232, 43, 42, 41 };
    private static readonly short[] ComboStep3 = { 41, 42, 43 };

    private async ValueTask CastNextOrAttackAsync(IAttackable target)
    {
        var skills = this.SelectedCharacter?.LearnedSkills
            .Where(s => s.Skill is { } sk && (int)sk.SkillType <= (int)SkillType.AreaSkillExplicitTarget)
            .ToList() ?? new List<SkillEntry>();

        // A combo class deliberately walks step1 -> step2 -> step3 so it visibly combos,
        // falling back to the round-robin below if the wanted step is all on cooldown.
        if (skills.Count > 0 && this.ComboState is not null && this._comboStep < 3)
        {
            var pool = this._comboStep switch { 0 => ComboStep1, 1 => ComboStep2, _ => ComboStep3 };
            var comboEntry = skills.FirstOrDefault(s =>
                Array.IndexOf(pool, (short)s.Skill!.Number) >= 0
                && !MobaCooldowns.IsOnCooldown(this, s.Skill!.Number, DateTime.UtcNow));

            if (comboEntry is not null && await this.TryConsumeForSkillAsync(comboEntry).ConfigureAwait(false))
            {
                this._comboStep++;
                this._comboResetUtc = DateTime.UtcNow + TimeSpan.FromSeconds(3);
                await this.FireSkillAsync(comboEntry, target).ConfigureAwait(false);
                return;
            }
        }

        if (this._comboStep >= 3 || DateTime.UtcNow > this._comboResetUtc)
        {
            this._comboStep = 0;
        }

        if (skills.Count > 0)
        {
            for (var i = 0; i < skills.Count; i++)
            {
                var entry = skills[(this._skillCursor + i) % skills.Count];
                var number = entry.Skill!.Number;
                if (MobaCooldowns.IsOnCooldown(this, number, DateTime.UtcNow))
                {
                    continue;
                }

                this._skillCursor = (this._skillCursor + i + 1) % skills.Count;
                if (await this.TryConsumeForSkillAsync(entry).ConfigureAwait(false))
                {
                    await this.FireSkillAsync(entry, target).ConfigureAwait(false);
                    return;
                }
            }
        }

        // Nothing castable this tick: basic attack.
        await target.AttackByAsync(this, null, false).ConfigureAwait(false);
    }

    /// <summary>
    /// Applies a skill the bot already paid for: faces the target, registers it with the
    /// combo state machine (the bot bypasses the handlers that normally do this), deals the
    /// damage and broadcasts the cast animation.
    /// </summary>
    private async ValueTask FireSkillAsync(SkillEntry entry, IAttackable target)
    {
        if (entry.Skill is not { } skill)
        {
            return;
        }

        this.Rotation = this.Position.GetDirectionTo(target.Position);

        var isCombo = false;
        if (this.ComboState is { } combo)
        {
            isCombo = await combo.RegisterSkillAsync(skill).ConfigureAwait(false);
        }

        var mana = this.Attributes?[Stats.CurrentMana] ?? 0f;
        this.Logger.LogInformation(
            "[MOBA-AI] {Name} cast {Skill}#{Num}{Combo} -> {Target} @ {Tx},{Ty} (comboStep {Step}, mana {Mana:F0})",
            this.Name,
            skill.Name,
            skill.Number,
            isCombo ? " [combo]" : string.Empty,
            (target as Player)?.Name ?? (target as NPC.Monster)?.Definition?.Designation ?? "?",
            target.Position.X,
            target.Position.Y,
            this._comboStep,
            mana);

        var hit = await target.AttackByAsync(this, entry, isCombo).ConfigureAwait(false);
        var effectApplied = false;
        if (hit is { } h)
        {
            effectApplied = await target.TryApplyElementalEffectsAsync(this, entry, h).ConfigureAwait(false);
        }

        await this.ForEachWorldObserverAsync<Views.World.IShowSkillAnimationPlugIn>(
            p => p.ShowSkillAnimationAsync(this, target, skill, effectApplied), true).ConfigureAwait(false);
    }

    private int GetMeleeAttackRange()
        => MobaCombatStats.AttackRangeOf(MobaPassives.FamilyOf(this));

    private static Point StepToward(Point from, Point to, int stopShortBy)
    {
        var dx = to.X - from.X;
        var dy = to.Y - from.Y;
        var len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len <= stopShortBy)
        {
            return from;
        }

        // Clamp the hop to MaxStepTiles - MoveAsync teleports, so an uncapped step blinks
        // the bot the whole way to `to`.
        var travel = Math.Min(len - stopShortBy, MaxStepTiles);
        var scale = travel / len;
        var nx = (int)Math.Round(from.X + (dx * scale));
        var ny = (int)Math.Round(from.Y + (dy * scale));
        return new Point((byte)Math.Clamp(nx, 0, 255), (byte)Math.Clamp(ny, 0, 255));
    }
}
