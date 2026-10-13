// <copyright file="MobaFountains.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.GameLogic.Attributes;
using MUnique.OpenMU.GameLogic.Views.Moba;

/// <summary>
/// The capture state of one fountain: the owner and the seconds each team has accumulated. A team
/// accumulates while at least one of its champions channels (and the other team does not); the progress is
/// kept when the channel is released, so a fight can interrupt it and it can be resumed later.
/// </summary>
public sealed class MobaFountainState
{
    /// <summary>Seconds of channelling a team needs to take the fountain.</summary>
    public const double CaptureSeconds = 15;

    /// <summary>Gets the team that owns the fountain (<see cref="MobaTeam.None"/> = neutral).</summary>
    public MobaTeam Owner { get; private set; }

    /// <summary>Gets the seconds the blue team has accumulated.</summary>
    public double BlueProgress { get; private set; }

    /// <summary>Gets the seconds the red team has accumulated.</summary>
    public double RedProgress { get; private set; }

    /// <summary>Gets a value indicating whether both teams channelled in the last step (nobody progresses).</summary>
    public bool Contested { get; private set; }

    /// <summary>Gets the seconds a team has accumulated.</summary>
    /// <param name="team">The team.</param>
    /// <returns>The accumulated seconds.</returns>
    public double ProgressOf(MobaTeam team) => team == MobaTeam.Blue ? this.BlueProgress : team == MobaTeam.Red ? this.RedProgress : 0;

    /// <summary>Advances the capture.</summary>
    /// <param name="seconds">The elapsed seconds.</param>
    /// <param name="blueChanneling">Whether a blue champion channels in range.</param>
    /// <param name="redChanneling">Whether a red champion channels in range.</param>
    /// <returns>The team that captured the fountain in this step, or <see cref="MobaTeam.None"/>.</returns>
    public MobaTeam Advance(double seconds, bool blueChanneling, bool redChanneling)
    {
        this.Contested = blueChanneling && redChanneling;
        if (this.Contested || seconds <= 0)
        {
            return MobaTeam.None;
        }

        var team = blueChanneling ? MobaTeam.Blue : redChanneling ? MobaTeam.Red : MobaTeam.None;
        if (team == MobaTeam.None || team == this.Owner)
        {
            return MobaTeam.None;
        }

        var progress = this.ProgressOf(team) + seconds;
        if (progress >= CaptureSeconds)
        {
            this.Owner = team;
            this.BlueProgress = 0;
            this.RedProgress = 0;
            return team;
        }

        if (team == MobaTeam.Blue)
        {
            this.BlueProgress = progress;
        }
        else
        {
            this.RedProgress = progress;
        }

        return MobaTeam.None;
    }

    /// <summary>Makes the fountain neutral again with no progress.</summary>
    public void Reset()
    {
        this.Owner = MobaTeam.None;
        this.BlueProgress = 0;
        this.RedProgress = 0;
        this.Contested = false;
    }
}

/// <summary>
/// The two capturable fountains on the horizontal road in the middle of the arena (20 % and 80 % of its length).
/// Holding the capture key (F on the client) next to a fountain channels the capture; a team that accumulates
/// <see cref="MobaFountainState.CaptureSeconds"/> owns it, and an owned fountain heals the allied champions around it
/// by <see cref="HealFraction"/> of their maximum health every second. The client mirrors the positions
/// (<c>g_MobaFountains</c> in <c>MapManager.cpp</c>): keep both in sync.
/// </summary>
public static class MobaFountains
{
    /// <summary>The channel kind sent to the client for the fountain capture bar.</summary>
    public const byte KindFountain = 5;

    /// <summary>Tiles around the fountain centre from which a champion can channel the capture.</summary>
    public const double CaptureRadius = 5.5;

    /// <summary>Tiles around an owned fountain in which allies are healed (and the light reaches).</summary>
    public const double HealRadius = 12;

    /// <summary>The fraction of the maximum health healed per second.</summary>
    public const double HealFraction = 0.10;

    /// <summary>Seconds a held-key signal stays valid without a refresh (the client repeats it while the key is held).</summary>
    public const double SignalSeconds = 2.5;

    /// <summary>The fountain centres (tile x, tile y): 20 % and 80 % of the horizontal road, a 180 degree turn of each other.</summary>
    public static readonly (byte X, byte Y)[] Positions = { (52, 128), (184, 127) };

    private static readonly string[] Names = { "oeste", "este" };

    private static readonly ConditionalWeakTable<Player, Signal> Signals = new();

    private static MobaFountainState[] _states = Enumerable.Range(0, Positions.Length).Select(_ => new MobaFountainState()).ToArray();

    private static double[] _healClock = new double[Positions.Length];

    private static DateTime _lastTickUtc = DateTime.MinValue;

    /// <summary>Gets the number of fountains.</summary>
    public static int Count => Positions.Length;

    /// <summary>Gets the state of a fountain.</summary>
    /// <param name="index">The fountain index.</param>
    /// <returns>The state.</returns>
    public static MobaFountainState StateOf(int index) => _states[index];

    /// <summary>Makes every fountain neutral again (new match).</summary>
    public static void Reset()
    {
        foreach (var state in _states)
        {
            state.Reset();
        }

        Array.Clear(_healClock);
        _lastTickUtc = DateTime.MinValue;
    }

    /// <summary>Gets the index of the fountain a champion could channel at its position, or -1.</summary>
    /// <param name="player">The champion.</param>
    /// <returns>The fountain index or -1.</returns>
    public static int FountainInReach(Player player)
    {
        for (var i = 0; i < Positions.Length; i++)
        {
            if (Distance(player.Position.X, player.Position.Y, Positions[i]) <= CaptureRadius)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>The client pressed (<paramref name="on"/>) or released the capture key.</summary>
    /// <param name="player">The champion.</param>
    /// <param name="on">Whether the key is held.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SignalAsync(Player player, bool on)
    {
        var logger = player.GameContext.LoggerFactory.CreateLogger(typeof(MobaFountains));
        if (!player.IsMobaClone || MobaTeams.GetTeam(player) == MobaTeam.None)
        {
            logger.LogInformation("[MOBA-FOUNTAIN] key {State} from {Name} ignored: not a champion of a match (clone={Clone}, team={Team}).", on ? "down" : "up", player.Name, player.IsMobaClone, MobaTeams.GetTeam(player));
            return;
        }

        if (!on)
        {
            if (Signals.TryGetValue(player, out var previous) && previous.Active)
            {
                previous.Active = false;
                await player.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(KindFountain, 0)).ConfigureAwait(false);
            }

            return;
        }

        if (!player.IsAlive || player.CurrentMap is null || FountainInReach(player) < 0)
        {
            logger.LogInformation("[MOBA-FOUNTAIN] key down from {Name} ignored: alive={Alive}, map={Map}, position=({X},{Y}), no fountain within {Radius} tiles.", player.Name, player.IsAlive, player.CurrentMap?.Definition.Number, player.Position.X, player.Position.Y, CaptureRadius);
            return;
        }

        var signal = Signals.GetOrCreateValue(player);
        if (!signal.Active)
        {
            logger.LogInformation("[MOBA-FOUNTAIN] {Name} ({Team}) starts channelling fountain {Index}.", player.Name, MobaTeams.GetTeam(player), FountainInReach(player));
        }

        signal.Active = true;
        signal.LastUtc = DateTime.UtcNow;
    }

    /// <summary>Runs one step (about once a second): channels, captures, healing and the capture bar.</summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask TickAsync(GameContext gameContext)
    {
        var players = await gameContext.GetPlayersAsync().ConfigureAwait(false);
        var champions = players.Where(p => p.IsMobaClone && MobaTeams.GetTeam(p) != MobaTeam.None).ToList();
        if (champions.Count == 0)
        {
            if (_lastTickUtc != DateTime.MinValue)
            {
                Reset();
            }

            return;
        }

        var now = DateTime.UtcNow;
        var seconds = _lastTickUtc == DateTime.MinValue ? 1.0 : Math.Clamp((now - _lastTickUtc).TotalSeconds, 0.05, 2.5);
        _lastTickUtc = now;

        for (var i = 0; i < Positions.Length; i++)
        {
            var channelers = new List<Player>();
            foreach (var champion in champions)
            {
                if (Signals.TryGetValue(champion, out var signal) && signal.Active)
                {
                    var fresh = (now - signal.LastUtc).TotalSeconds <= SignalSeconds;
                    if (!fresh || !champion.IsAlive || FountainInReach(champion) < 0)
                    {
                        signal.Active = false;
                        await champion.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(KindFountain, 0)).ConfigureAwait(false);
                    }
                    else if (FountainInReach(champion) == i)
                    {
                        channelers.Add(champion);
                    }
                }
            }

            var state = _states[i];
            var blue = channelers.Any(p => MobaTeams.GetTeam(p) == MobaTeam.Blue);
            var red = channelers.Any(p => MobaTeams.GetTeam(p) == MobaTeam.Red);
            var captured = state.Advance(seconds, blue, red);

            foreach (var channeler in channelers)
            {
                var team = MobaTeams.GetTeam(channeler);
                var remaining = state.Contested || team == state.Owner
                    ? 0
                    : (int)Math.Round((MobaFountainState.CaptureSeconds - state.ProgressOf(team)) * 1000);
                if (captured != MobaTeam.None)
                {
                    remaining = 0;
                }

                var ms = remaining;
                await channeler.InvokeViewPlugInAsync<IMobaChannelPlugIn>(p => p.ShowChannelAsync(KindFountain, ms)).ConfigureAwait(false);
            }

            if (channelers.Count > 0)
            {
                gameContext.LoggerFactory.CreateLogger(typeof(MobaFountains)).LogInformation(
                    "[MOBA-FOUNTAIN] fountain {Index}: owner={Owner} blue={Blue:F1}s red={Red:F1}s contested={Contested} channelers={Count}",
                    i,
                    state.Owner,
                    state.BlueProgress,
                    state.RedProgress,
                    state.Contested,
                    channelers.Count);
            }

            if (captured != MobaTeam.None)
            {
                var message = $"[MOBA] ¡El equipo {(captured == MobaTeam.Blue ? "azul" : "rojo")} capturó la fuente {Names[i]}!";
                foreach (var champion in champions)
                {
                    await champion.ShowBlueMessageAsync(message).ConfigureAwait(false);
                }
            }

            if (state.Owner != MobaTeam.None)
            {
                _healClock[i] += seconds;
                while (_healClock[i] >= 1.0)
                {
                    _healClock[i] -= 1.0;
                    Heal(champions, i, state.Owner);
                }
            }
            else
            {
                _healClock[i] = 0;
            }
        }
    }

    private static void Heal(IEnumerable<Player> champions, int index, MobaTeam owner)
    {
        foreach (var champion in champions)
        {
            if (MobaTeams.GetTeam(champion) != owner
                || !champion.IsAlive
                || champion.Attributes is not { } attributes
                || Distance(champion.Position.X, champion.Position.Y, Positions[index]) > HealRadius)
            {
                continue;
            }

            var maximum = attributes[Stats.MaximumHealth];
            attributes[Stats.CurrentHealth] = (float)Math.Min(maximum, attributes[Stats.CurrentHealth] + (maximum * HealFraction));
        }
    }

    private static double Distance(int x, int y, (byte X, byte Y) centre)
        => Math.Sqrt(Math.Pow(x - centre.X, 2) + Math.Pow(y - centre.Y, 2));

    private sealed class Signal
    {
        public bool Active;

        public DateTime LastUtc;
    }
}
