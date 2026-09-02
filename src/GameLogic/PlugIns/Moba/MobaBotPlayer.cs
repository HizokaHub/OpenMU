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
    private int _developSkillCursor;
    private int _comboStep;
    private Point _homeSpawn;
    private IReadOnlyList<Point> _lane = Array.Empty<Point>();
    private int _laneIndex;
    private int _laneOffset;

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

        // Target priority (for balance testing): clear the LANE CREEPS first, then enemy
        // bot champions, and only then the human champion (so a human tester can watch a
        // fight without being focus-fired).
        var inRange = map.GetAttackablesInRange(pos, AcquireRangeTiles)
            .Where(a => a.IsAlive && !ReferenceEquals(a, this) && MobaTeams.AreEnemies(this, a))
            .ToList();

        var target = inRange.OfType<NPC.Monster>().Where(m => !MobaStructures.IsStructure(m))
                .OrderBy(m => m.GetDistanceTo(pos)).FirstOrDefault() as IAttackable
            ?? inRange.OfType<MobaBotPlayer>().Where(b => !b.IsDummy)
                .OrderBy(b => b.GetDistanceTo(pos)).FirstOrDefault()
            ?? inRange.OfType<Player>().OrderBy(p => p.GetDistanceTo(pos)).FirstOrDefault() as IAttackable;

        if (target is null)
        {
            // Nothing to fight: push the lane from our creep spawn toward the enemy one.
            await this.MarchLaneAsync(pos).ConfigureAwait(false);
            return;
        }

        var distance = target.GetDistanceTo(pos);
        var attackRange = Math.Max(2, this.GetMeleeAttackRange());

        if (distance > attackRange)
        {
            await this.WalkTowardAsync(target.Position).ConfigureAwait(false);
            return;
        }

        if (DateTime.UtcNow < this._nextActionUtc)
        {
            return;
        }

        this._nextActionUtc = DateTime.UtcNow + ActionCooldown;
        await this.CastNextOrAttackAsync(target).ConfigureAwait(false);
    }

    private void OnBotChampionDied(object? sender, DeathInformation death)
    {
        _ = MobaXp.HandleChampionDeathAsync(this, death);
    }

    /// <summary>Walks along the lane waypoints toward the enemy creep spawn, spread out by lane offset.</summary>
    /// <param name="pos">Current position.</param>
    private async ValueTask MarchLaneAsync(Point pos)
    {
        if (this._lane.Count == 0 || this.IsWalking)
        {
            return;
        }

        while (this._laneIndex < this._lane.Count - 1
               && this._lane[this._laneIndex].EuclideanDistanceTo(pos) <= WaypointReachedTiles + 4)
        {
            this._laneIndex++;
        }

        var wp = this._lane[this._laneIndex];

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
    {
        // Champions use a small range; ranged loadouts (bow / staff) still work from 2.
        return 3;
    }

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
