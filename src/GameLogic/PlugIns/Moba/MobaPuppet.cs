// <copyright file="MobaPuppet.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using System.Threading;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.Offline;
using MUnique.OpenMU.GameLogic.PlayerActions;
using MUnique.OpenMU.GameLogic.PlayerActions.Skills;
using MUnique.OpenMU.GameLogic.Views;
using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The control of a champion whose player left the match, by a teammate (2026-10-25, decided with the user): the disconnected champion stays
/// in the match as a <see cref="MobaGhostPlayer"/>, and any teammate may take it over like the Elf commands her creature: the right click walks it
/// or attacks, the number keys cast <b>its</b> skills (with its ranks, mana and cooldowns), and the recall key sends it home. Only a
/// disconnected champion can be controlled (an idle one that is still connected is only sent to its base by <see cref="MobaAfk"/>), a teammate
/// controls one champion at a time and a champion has one controller. When the owner reconnects the control ends at once and the owner gets
/// its champion back (<see cref="MobaGhostPlayer.TakeOverAsync"/>).
/// </summary>
public static class MobaPuppet
{
    /// <summary>The order kind of the client: walk to a tile.</summary>
    public const byte OrderMove = 1;

    /// <summary>The order kind of the client: attack an object.</summary>
    public const byte OrderAttack = 2;

    private const int TickMilliseconds = 300;
    private const int StatusMilliseconds = 500;
    private const int BasicAttackMilliseconds = 540;
    private const int MaxWalkSteps = 8;
    private const double ArrivedTiles = 1.2;
    private const ushort NoTarget = 0xFFFF;
    private const short NovaSkill = 40;

    private static readonly TargetedSkillDefaultPlugin DefaultSkillPlugin = new();

    private static readonly AreaSkillAttackAction AreaAction = new();

    private static readonly WizardTeleportAction TeleportAction = new();

    private static readonly ConcurrentDictionary<Player, Session> Sessions = new();

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Player, StrongBoxFlag> Shown = new();

    private static readonly object TimerLock = new();

    private static Timer? _timer;

    private static int _ticking;

    /// <summary>The champion a teammate controls, or <see langword="null"/>.</summary>
    /// <param name="controller">The teammate.</param>
    /// <returns>The controlled champion.</returns>
    public static Player? ControlledBy(Player controller) => Sessions.TryGetValue(controller, out var session) ? session.Ghost : null;

    /// <summary>Whether a player is a champion of a match who can control a disconnected teammate.</summary>
    /// <param name="player">The player.</param>
    /// <returns><see langword="true"/> for a human champion with a team.</returns>
    public static bool CanControl(Player player)
        => player.IsMobaClone
           && player is not OfflinePlayer
           && player.CurrentMap is { } map
           && map.MapId == MobaCloneFactory.ArenaMapNumber
           && MobaTeams.GetTeam(player) != MobaTeam.None;

    /// <summary>
    /// Takes over the champion with the given key; if the teammate already controls it, releases it (the toggle of the key).
    /// </summary>
    /// <param name="controller">The teammate.</param>
    /// <param name="key">The object id of the champion for the teammate.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SelectAsync(Player controller, ushort key)
    {
        if (!CanControl(controller))
        {
            return;
        }

        var ghosts = await GhostsOfAsync(controller).ConfigureAwait(false);
        var ghost = ghosts.FirstOrDefault(g => g.GetId(controller) == key);
        if (ghost is null)
        {
            await controller.ShowBlueMessageAsync("[MOBA] Ese campeón ya no se puede controlar.").ConfigureAwait(false);
            return;
        }

        if (Sessions.TryGetValue(controller, out var current))
        {
            var same = ReferenceEquals(current.Ghost, ghost);
            await ReleaseAsync(controller, null).ConfigureAwait(false);
            if (same)
            {
                return;
            }
        }

        ghost.MobaState.Controller = controller;
        Sessions[controller] = new Session(controller, ghost);
        EnsureTimer();
        await MobaAfk.NoteInputAsync(ghost).ConfigureAwait(false);
        await controller.ShowBlueMessageAsync($"[MOBA] Controlas a {NameOf(ghost)}: clic derecho para mover o atacar, 1-4 sus skills, B volver a base, Ctrl para soltarlo.").ConfigureAwait(false);
        await PushAsync(controller).ConfigureAwait(false);
    }

    /// <summary>
    /// Lets go of the champion a teammate controls.
    /// </summary>
    /// <param name="controller">The teammate.</param>
    /// <param name="reason">The line for the teammate, or <see langword="null"/> for the usual one.</param>
    /// <returns>A task.</returns>
    public static async ValueTask ReleaseAsync(Player controller, string? reason)
    {
        if (!Sessions.TryRemove(controller, out var session))
        {
            return;
        }

        if (ReferenceEquals(session.Ghost.MobaState.Controller, controller))
        {
            session.Ghost.MobaState.Controller = null;
        }

        await controller.ShowBlueMessageAsync(reason ?? $"[MOBA] Soltaste a {NameOf(session.Ghost)}.").ConfigureAwait(false);
        await PushAsync(controller).ConfigureAwait(false);
    }

    /// <summary>The owner came back: the controller (if any) loses the champion.</summary>
    /// <param name="state">The state of the champion which got its player back.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OnOwnerReturnedAsync(MobaChampionState state)
    {
        if (state.Controller is { } controller)
        {
            state.Controller = null;
            await ReleaseAsync(controller, "[MOBA] Su jugador volvió: ya no controlas ese campeón.").ConfigureAwait(false);
        }
    }

    /// <summary>An order of the teammate: walk to a tile (kind 1) or attack an object (kind 2).</summary>
    /// <param name="controller">The teammate.</param>
    /// <param name="kind">The kind of order.</param>
    /// <param name="x">The tile X.</param>
    /// <param name="y">The tile Y.</param>
    /// <param name="targetKey">The object id of the target.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OrderAsync(Player controller, byte kind, byte x, byte y, ushort targetKey)
    {
        if (!Sessions.TryGetValue(controller, out var session) || !IsUsable(session, out var map))
        {
            return;
        }

        MobaRecall.Interrupt(session.Ghost);
        await MobaAfk.NoteInputAsync(session.Ghost).ConfigureAwait(false);
        if (kind == OrderMove && map.Terrain.WalkMap[x, y])
        {
            session.Order = Order.MoveTo(new Point(x, y));
            return;
        }

        if (kind == OrderAttack
            && map.GetObject(targetKey) is IAttackable target
            && target.IsAlive
            && MobaTeams.AreEnemies(session.Ghost, target)
            && MobaVision.CanTarget(controller, target))
        {
            session.Order = Order.Attack(target);
        }
    }

    /// <summary>The teammate casts a skill of the controlled champion.</summary>
    /// <param name="controller">The teammate.</param>
    /// <param name="skillNumber">The skill number.</param>
    /// <param name="targetKey">The object id of the target, or 0xFFFF for none.</param>
    /// <param name="x">The tile X of the point aimed at.</param>
    /// <param name="y">The tile Y of the point aimed at.</param>
    /// <returns>A task.</returns>
    public static async ValueTask CastAsync(Player controller, short skillNumber, ushort targetKey, byte x, byte y)
    {
        if (!Sessions.TryGetValue(controller, out var session) || !IsUsable(session, out var map))
        {
            return;
        }

        var ghost = session.Ghost;
        if (ghost.SkillList?.GetSkill((ushort)skillNumber) is not { Skill: { } skill } entry || skill.SkillType == SkillType.PassiveBoost || skillNumber == NovaSkill)
        {
            return;
        }

        if (MobaSkills.IsInactive(ghost, entry))
        {
            await controller.ShowBlueMessageAsync($"[MOBA] {skill.Name} está inactiva: {NameOf(ghost)} no puso puntos en ella.").ConfigureAwait(false);
            return;
        }

        MobaRecall.Interrupt(ghost);
        await MobaAfk.NoteInputAsync(ghost).ConfigureAwait(false);
        var target = targetKey == NoTarget ? null : map.GetObject(targetKey) as IAttackable;
        var aim = target?.Position ?? new Point(x, y);
        if (target is not null)
        {
            ghost.Rotation = ghost.Position.GetDirectionTo(target.Position);
        }

        await CastCoreAsync(ghost, skill, target, targetKey, aim).ConfigureAwait(false);
        await PushAsync(controller).ConfigureAwait(false);
    }

    /// <summary>The teammate sends the controlled champion to its base (key B).</summary>
    /// <param name="controller">The teammate.</param>
    /// <returns>A task.</returns>
    public static async ValueTask RecallAsync(Player controller)
    {
        if (!Sessions.TryGetValue(controller, out var session) || !IsUsable(session, out _))
        {
            return;
        }

        session.Order = Order.None;
        await MobaAfk.NoteInputAsync(session.Ghost).ConfigureAwait(false);
        await MobaRecall.StartRecallAsync(session.Ghost).ConfigureAwait(false);
    }

    /// <summary>
    /// Once a second: tells every champion of a match which disconnected teammates it can control (the panel closes when none is left).
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickStatusAsync(IGameContext gameContext)
    {
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        foreach (var player in players.Where(CanControl))
        {
            try
            {
                var candidates = await GhostsOfAsync(player, players).ConfigureAwait(false);
                var wasShown = Shown.GetValue(player, _ => new StrongBoxFlag());
                if (candidates.Count > 0 || Sessions.ContainsKey(player) || wasShown.Value)
                {
                    wasShown.Value = candidates.Count > 0 || Sessions.ContainsKey(player);
                    await PushAsync(player, candidates).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                player.Logger.LogWarning(ex, "[MOBA-PUPPET] could not report the controllable teammates to '{Name}'.", NameOf(player));
            }
        }
    }

    private static string NameOf(Player player) => player.SelectedCharacter?.Name ?? "?";

    private static async ValueTask<List<Player>> GhostsOfAsync(Player controller, IList<Player>? players = null)
    {
        players ??= await controller.GameContext.GetPlayersAsync().ConfigureAwait(false);
        var team = MobaTeams.GetTeam(controller);
        return players
            .Where(p => p is MobaGhostPlayer
                        && p.IsMobaClone
                        && p.CurrentMap is not null
                        && ReferenceEquals(p.CurrentMap, controller.CurrentMap)
                        && MobaTeams.GetTeam(p) == team
                        && (p.MobaState.Controller is null || ReferenceEquals(p.MobaState.Controller, controller)))
            .OrderBy(p => p.GetDistanceTo(controller.Position))
            .ToList();
    }

    private static bool IsUsable(Session session, out GameMap map)
    {
        map = session.Controller.CurrentMap!;
        return session.Ghost.IsAlive
               && session.Ghost.CurrentMap is not null
               && session.Controller.CurrentMap is not null
               && ReferenceEquals(session.Ghost.CurrentMap, session.Controller.CurrentMap)
               && ReferenceEquals(session.Ghost.MobaState.Controller, session.Controller);
    }

    private static async ValueTask CastCoreAsync(Player ghost, Skill skill, IAttackable? target, ushort targetKey, Point aim)
    {
        if (skill.Number == MobaBlink.SkillNumber)
        {
            await TeleportAction.TryTeleportWithSkillAsync(ghost, aim).ConfigureAwait(false);
            return;
        }

        if (skill.SkillType is SkillType.AreaSkillAutomaticHits or SkillType.AreaSkillExplicitHits or SkillType.AreaSkillExplicitTarget)
        {
            await AreaAction.AttackAsync(ghost, target is null ? NoTarget : targetKey, (ushort)skill.Number, aim, (byte)ghost.Rotation, true).ConfigureAwait(false);
            return;
        }

        var strategy = ghost.GameContext.PlugInManager.GetStrategy<short, ITargetedSkillPlugin>(skill.Number) ?? DefaultSkillPlugin;
        await strategy.PerformSkillAsync(ghost, target ?? ghost, (ushort)skill.Number).ConfigureAwait(false);
    }

    private static void EnsureTimer()
    {
        lock (TimerLock)
        {
            _timer ??= new Timer(_ => TickAll(), null, TickMilliseconds, TickMilliseconds);
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD100:Avoid async void methods", Justification = "Timer callback.")]
    private static async void TickAll()
    {
        if (Interlocked.Exchange(ref _ticking, 1) == 1)
        {
            return;
        }

        try
        {
            foreach (var session in Sessions.Values)
            {
                await TickAsync(session).ConfigureAwait(false);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _ticking, 0);
        }
    }

    private static async ValueTask TickAsync(Session session)
    {
        try
        {
            if (!IsStillValid(session))
            {
                await ReleaseAsync(session.Controller, "[MOBA] Ya no puedes controlar a ese campeón.").ConfigureAwait(false);
                return;
            }

            if (session.Ghost.IsAlive && !MobaRecall.IsChanneling(session.Ghost))
            {
                await RunOrderAsync(session).ConfigureAwait(false);
            }

            if (DateTime.UtcNow >= session.NextStatusUtc)
            {
                await PushAsync(session.Controller).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            session.Ghost.Logger.LogWarning(ex, "[MOBA-PUPPET] the control of '{Name}' failed.", NameOf(session.Ghost));
        }
    }

    private static bool IsStillValid(Session session)
        => session.Controller.CurrentMap is not null
           && CanControl(session.Controller)
           && ReferenceEquals(session.Ghost.MobaState.Controller, session.Controller)
           && ReferenceEquals(session.Ghost.MobaState.Ghost, session.Ghost)
           && session.Ghost.CurrentMap is not null;

    private static async ValueTask RunOrderAsync(Session session)
    {
        var ghost = session.Ghost;
        switch (session.Order.Kind)
        {
            case OrderKind.Move:
                if (ghost.Position.EuclideanDistanceTo(session.Order.Point) <= ArrivedTiles)
                {
                    session.Order = Order.None;
                    return;
                }

                await session.StepTowardAsync(session.Order.Point).ConfigureAwait(false);
                break;
            case OrderKind.Attack:
                await RunAttackAsync(session).ConfigureAwait(false);
                break;
        }
    }

    private static async ValueTask RunAttackAsync(Session session)
    {
        var ghost = session.Ghost;
        var target = session.Order.Target;
        if (target is null || !target.IsAlive || target.CurrentMap is null || !ReferenceEquals(target.CurrentMap, ghost.CurrentMap) || !MobaVision.CanTarget(ghost, target))
        {
            session.Order = Order.None;
            return;
        }

        var reach = Math.Max(2, MobaCombatStats.AttackRangeOf(MobaPassives.FamilyOf(ghost)));
        if (ghost.GetDistanceTo(target.Position) > reach)
        {
            await session.StepTowardAsync(target.Position).ConfigureAwait(false);
            return;
        }

        var now = DateTime.UtcNow;
        if (ghost.IsWalking || now < session.NextBasicUtc)
        {
            return;
        }

        session.NextBasicUtc = now.AddMilliseconds(BasicAttackMilliseconds);
        ghost.Rotation = ghost.Position.GetDirectionTo(target.Position);
        await target.AttackByAsync(ghost, null, false).ConfigureAwait(false);
    }

    private static ValueTask PushAsync(Player controller) => PushAsync(controller, null);

    private static async ValueTask PushAsync(Player controller, IList<Player>? candidates)
    {
        candidates ??= await GhostsOfAsync(controller).ConfigureAwait(false);
        Sessions.TryGetValue(controller, out var session);
        if (session is not null)
        {
            session.NextStatusUtc = DateTime.UtcNow.AddMilliseconds(StatusMilliseconds);
        }

        var entries = candidates
            .Select(g => new MobaPuppetCandidate(
                (ushort)g.GetId(controller),
                MobaMinimapKind.BaseClassIndex(g.SelectedCharacter?.CharacterClass?.Number ?? 0),
                PercentOf(g, GameLogic.Attributes.Stats.CurrentHealth, GameLogic.Attributes.Stats.MaximumHealth),
                PercentOf(g, GameLogic.Attributes.Stats.CurrentMana, GameLogic.Attributes.Stats.MaximumMana),
                session is not null && ReferenceEquals(session.Ghost, g),
                NameOf(g)))
            .ToList();
        var skills = session is null ? new List<MobaPuppetSkill>() : SkillsOf(session.Ghost);
        var position = session?.Ghost.Position ?? default;
        var info = new MobaPuppetInfo(session is not null, position.X, position.Y, entries, skills);
        await controller.InvokeViewPlugInAsync<IMobaPuppetPlugIn>(p => p.ShowPuppetAsync(info)).ConfigureAwait(false);
    }

    private static byte PercentOf(Player player, AttributeSystem.AttributeDefinition current, AttributeSystem.AttributeDefinition maximum)
        => player.Attributes is { } a ? (byte)Math.Clamp(a[current] * 100f / Math.Max(1f, a[maximum]), 0f, 100f) : (byte)0;

    private static List<MobaPuppetSkill> SkillsOf(Player ghost)
    {
        var now = DateTime.UtcNow;
        var list = new List<MobaPuppetSkill>();
        foreach (var entry in ghost.SelectedCharacter?.LearnedSkills ?? Enumerable.Empty<DataModel.Entities.SkillEntry>())
        {
            if (entry.Skill is not { } skill || skill.SkillType == SkillType.PassiveBoost)
            {
                continue;
            }

            var number = (short)skill.Number;
            var left = ghost.MobaSkillCooldowns.TryGetValue(number, out var readyAt) ? (int)Math.Max(0, (readyAt - now).TotalMilliseconds) : 0;
            var full = (int)MobaCooldowns.GetCooldown(ghost, entry).TotalMilliseconds;
            list.Add(new MobaPuppetSkill(number, (byte)Math.Clamp((int)entry.Level, 0, MobaSkills.SkillLevelCap), MobaSkills.IsBuff(number), left, Math.Max(full, left)));
        }

        return list;
    }

    private enum OrderKind
    {
        None,
        Move,
        Attack,
    }

    private readonly record struct Order(OrderKind Kind, Point Point, IAttackable? Target)
    {
        public static Order None => new(OrderKind.None, default, null);

        public static Order MoveTo(Point point) => new(OrderKind.Move, point, null);

        public static Order Attack(IAttackable target) => new(OrderKind.Attack, default, target);
    }

    private sealed class StrongBoxFlag
    {
        public bool Value;
    }

    private sealed class Session
    {
        private List<Point>? _path;
        private Point _pathTarget;

        public Session(Player controller, Player ghost)
        {
            this.Controller = controller;
            this.Ghost = ghost;
        }

        public Player Controller { get; }

        public Player Ghost { get; }

        public Order Order { get; set; } = Order.None;

        public DateTime NextBasicUtc { get; set; }

        public DateTime NextStatusUtc { get; set; }

        /// <summary>Walks a chunk of the A* path toward a tile (the same navigation the bots use).</summary>
        /// <param name="target">The tile.</param>
        /// <returns>A task.</returns>
        public async ValueTask StepTowardAsync(Point target)
        {
            if (this.Ghost.IsWalking || this.Ghost.CurrentMap?.Terrain?.AIgrid is not { } grid)
            {
                return;
            }

            var from = this.Ghost.Position;
            var index = this._path?.IndexOf(from) ?? -1;
            if (this._path is null || index < 0 || this._pathTarget.EuclideanDistanceTo(target) > 3)
            {
                this._path = MobaNavigation.FindPath(grid, from, target);
                this._pathTarget = target;
                index = this._path is null ? -1 : 0;
            }

            if (this._path is null || index < 0)
            {
                this.Ghost.Logger.LogDebug("[MOBA-PUPPET] no path from {From} to {To}.", from, target);
                return;
            }

            var steps = new List<WalkingStep>(MaxWalkSteps);
            var cursor = from;
            for (var i = index + 1; i < this._path.Count && steps.Count < MaxWalkSteps; i++)
            {
                steps.Add(new WalkingStep(cursor, this._path[i], cursor.GetDirectionTo(this._path[i])));
                cursor = this._path[i];
            }

            if (steps.Count > 0)
            {
                await this.Ghost.WalkToAsync(cursor, steps.ToArray()).ConfigureAwait(false);
            }
        }
    }
}
