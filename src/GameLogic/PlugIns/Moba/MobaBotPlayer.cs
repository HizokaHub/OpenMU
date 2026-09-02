// <copyright file="MobaBotPlayer.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.DataModel.Entities;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Offline;
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

    private readonly MobaTeam _team;
    private readonly bool _isDummy;
    private Timer? _brain;
    private int _ticking;
    private int _skillCursor;
    private DateTime _nextActionUtc;
    private Point _homeSpawn;

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

    /// <summary>Gets a snapshot of the currently active bots.</summary>
    public static IReadOnlyCollection<MobaBotPlayer> All => ActiveBots.Keys.ToList();

    /// <summary>
    /// Spawns the bot into the arena on its clone character and starts the brain.
    /// </summary>
    /// <param name="account">A shared throwaway account (persistence is suppressed).</param>
    /// <param name="clone">The clone character (built by <see cref="MobaCloneFactory.BuildForClassAsync"/>).</param>
    /// <param name="spawn">Where to place the bot.</param>
    /// <returns><c>true</c> on success.</returns>
    public async ValueTask<bool> StartMobaAsync(Account account, Character clone, Point spawn)
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

            await this.ClientReadyAfterMapChangeAsync().ConfigureAwait(false);

            MobaTeams.Set(this, this._team);
            this.HuntingOrigin = spawn;
            this._homeSpawn = spawn;
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
        if (!this.IsAlive || this.SelectedCharacter is null || this.CurrentMap is not { } map)
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
        var target = map.GetAttackablesInRange(pos, AcquireRangeTiles)
            .OfType<Player>()
            .Where(p => p.IsAlive && !ReferenceEquals(p, this) && MobaTeams.AreEnemies(this, p))
            .OrderBy(p => p.GetDistanceTo(pos))
            .FirstOrDefault();

        if (target is null)
        {
            return;
        }

        var distance = target.GetDistanceTo(pos);
        var attackRange = Math.Max(2, this.GetMeleeAttackRange());

        if (distance > attackRange)
        {
            await this.MoveAsync(StepToward(pos, target.Position, PreferredRangeTiles)).ConfigureAwait(false);
            return;
        }

        if (DateTime.UtcNow < this._nextActionUtc)
        {
            return;
        }

        this._nextActionUtc = DateTime.UtcNow + ActionCooldown;
        await this.CastNextOrAttackAsync(target).ConfigureAwait(false);
    }

    private async ValueTask CastNextOrAttackAsync(IAttackable target)
    {
        var skills = this.SelectedCharacter?.LearnedSkills
            .Where(s => s.Skill is { } sk && (int)sk.SkillType <= (int)SkillType.AreaSkillExplicitTarget)
            .ToList() ?? new List<SkillEntry>();

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
                    // Face the target so the cast animation points the right way.
                    this.Rotation = this.Position.GetDirectionTo(target.Position);

                    var hit = await target.AttackByAsync(this, entry, false).ConfigureAwait(false);
                    var effectApplied = false;
                    if (hit is { } h)
                    {
                        effectApplied = await target.TryApplyElementalEffectsAsync(this, entry, h).ConfigureAwait(false);
                    }

                    // Broadcast the cast animation so observers see the skill visual (the
                    // bot bypasses the normal skill action which would do this).
                    await this.ForEachWorldObserverAsync<Views.World.IShowSkillAnimationPlugIn>(
                        p => p.ShowSkillAnimationAsync(this, target, entry.Skill, effectApplied), true).ConfigureAwait(false);

                    return;
                }
            }
        }

        // Nothing castable this tick: basic attack.
        await target.AttackByAsync(this, null, false).ConfigureAwait(false);
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

        var scale = (len - stopShortBy) / len;
        var nx = (int)Math.Round(from.X + (dx * scale));
        var ny = (int)Math.Round(from.Y + (dy * scale));
        return new Point((byte)Math.Clamp(nx, 0, 255), (byte)Math.Clamp(ny, 0, 255));
    }
}
