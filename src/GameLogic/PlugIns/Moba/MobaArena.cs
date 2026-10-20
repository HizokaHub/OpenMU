// <copyright file="MobaArena.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Runtime.CompilerServices;

/// <summary>
/// The state of one MOBA arena: the match timeline (clock, phase, leader level) and whatever else
/// belongs to "this match on this map" rather than to the process. It is found through the
/// <see cref="GameMap"/> instance the match is played on (not through the map number, which every
/// server of the process shares), so an arena never sees the state of another one.
/// </summary>
/// <remarks>
/// Stage 1 of the instancing plan: there is one arena per game server (the map 200 of its
/// <see cref="GameContext"/>). Stage 2 will create extra <see cref="GameMap"/> instances per match;
/// everything that already asks <see cref="Of(GameMap?)"/> keeps working unchanged.
/// </remarks>
public sealed class MobaArena
{
    private static readonly ConditionalWeakTable<GameMap, MobaArena> Arenas = new();

    /// <summary>Stand-in for objects which are not on any map; it never belongs to a real match.</summary>
    private static readonly MobaArena Detached = new(null);

    private volatile int _leaderLevel = 1;

    private MobaArena(GameMap? map)
    {
        this.Map = map;
        this.FountainStates = Enumerable.Range(0, MobaFountains.PointCount)
            .Select(i => new MobaFountainState(i < MobaFountains.Count ? MobaFountainState.CaptureSeconds : MobaFountains.TotemCaptureSeconds))
            .ToArray();
        this.FountainHealClock = new double[MobaFountains.PointCount];
    }

    /// <summary>Gets the bot objective coordinator state of the arena.</summary>
    public MobaObjectiveBoard ObjectiveBoard { get; } = new();

    /// <summary>Gets the capture state of every capture point (the fountains first, then the jungle totems).</summary>
    public MobaFountainState[] FountainStates { get; }

    /// <summary>Gets the seconds accumulated toward the next heal pulse of each fountain.</summary>
    public double[] FountainHealClock { get; }

    /// <summary>Gets or sets the UTC instant of the last fountain step.</summary>
    public DateTime FountainLastTickUtc { get; set; } = DateTime.MinValue;

    /// <summary>Gets or sets the UTC instant the fountain step last saw a champion.</summary>
    public DateTime FountainLastChampionUtc { get; set; } = DateTime.MinValue;

    /// <summary>Gets or sets the format of the match played in the arena (teams, lanes, jungle).</summary>
    public MobaFormat Format { get; set; } = MobaFormat.Classic;

    /// <summary>Gets the map the match is played on (<see langword="null"/> for the detached stand-in).</summary>
    public GameMap? Map { get; }

    /// <summary>Gets or sets the UTC instant the countdown before the match ends (<see cref="DateTime.MinValue"/> when there is none).</summary>
    public DateTime PreMatchUntilUtc { get; set; } = DateTime.MinValue;

    /// <summary>Gets a value indicating whether the arena is in the countdown before a match: only the champions move.</summary>
    public bool IsPreMatch => DateTime.UtcNow < this.PreMatchUntilUtc;

    /// <summary>Gets the terrain tiles which hold the champions in during the countdown.</summary>
    public List<MUnique.OpenMU.Pathfinding.Point> BarrierTiles { get; } = new();

    /// <summary>Gets or sets a value indicating whether the Crywolf safezone flags of this map were stripped (a MOBA arena has no safe tiles).</summary>
    public bool SafezoneCleared { get; set; }

    /// <summary>Gets or sets the creep-drop / structure phase of the match (never regresses within a match).</summary>
    public MobaShopTier Phase { get; set; } = MobaShopTier.T1;

    /// <summary>Gets or sets the highest champion level in the match (at least 1).</summary>
    public int LeaderLevel
    {
        get => this._leaderLevel;
        set => this._leaderLevel = value;
    }

    /// <summary>Gets or sets the UTC instant the match clock started, or <see cref="DateTime.MinValue"/> if it has not.</summary>
    public DateTime MatchStartUtc { get; set; } = DateTime.MinValue;

    /// <summary>Gets or sets the UTC instant since which the arena has had no champion (or <see cref="DateTime.MinValue"/>).</summary>
    public DateTime EmptySinceUtc { get; set; } = DateTime.MinValue;

    /// <summary>Gets or sets the UTC instant of the last <c>[MOBA-SCORE]</c> log line.</summary>
    public DateTime LastScoreLogUtc { get; set; } = DateTime.MinValue;

    /// <summary>Gets the time since the match started (zero if it has not).</summary>
    public TimeSpan Elapsed => this.MatchStartUtc == DateTime.MinValue ? TimeSpan.Zero : DateTime.UtcNow - this.MatchStartUtc;

    /// <summary>
    /// Gets the arena played on the given map.
    /// </summary>
    /// <param name="map">The map; <see langword="null"/> gives a detached stand-in.</param>
    /// <returns>The arena.</returns>
    public static MobaArena Of(GameMap? map) => map is null ? Detached : Arenas.GetValue(map, m => new MobaArena(m));

    /// <summary>
    /// Gets the arena a located object (champion, creep, structure, ...) is in.
    /// </summary>
    /// <param name="located">The object.</param>
    /// <returns>The arena.</returns>
    public static MobaArena Of(ILocateable? located) => Of(located?.CurrentMap);

    /// <summary>
    /// Creates an arena which belongs to no map. For tests of rules which only need an arena's state.
    /// </summary>
    /// <returns>A new, empty arena.</returns>
    public static MobaArena Standalone() => new(null);

    /// <summary>
    /// Restarts the match clock: the next champion to appear starts a new match timeline.
    /// </summary>
    public void ResetClock() => this.MatchStartUtc = DateTime.MinValue;
}
