// <copyright file="MobaGameMode.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The rules of a match: what the arena is filled with, what runs once the clock starts, what it takes to stop it
/// and what ends the match. A <see cref="MobaFormat"/> says how many play and which lanes are open; a mode says what
/// they play (the classic nexus match, a wave of zombies, commanders who send the creeps, ...).
/// </summary>
/// <remarks>
/// Every arena has one mode (<see cref="MobaArena.Mode"/>, <see cref="MobaGameModes.Classic"/> by default). The
/// engine calls these hooks at fixed points of the life of a match: <see cref="MobaArenaBoot.EnsureAsync"/>,
/// the end of the countdown (<see cref="MobaPreMatch"/>), the death of a nexus and the end or take-down of the arena.
/// A mode only decides; the clock, the countdown, the results table and the return to town stay in the engine.
/// </remarks>
public interface IMobaGameMode
{
    /// <summary>Gets the short, unique, lowercase identifier of the mode (what a GM types and what the ranking stores).</summary>
    string Id { get; }

    /// <summary>Gets the name of the mode for the players.</summary>
    string DisplayName { get; }

    /// <summary>
    /// Fills a fresh arena with what the mode needs before anyone plays (vendors, structures, ...). Does nothing for
    /// what already exists: it can be called again on an arena which is already prepared.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    ValueTask PrepareAsync(GameMap map, IGameContext gameContext);

    /// <summary>
    /// Starts what lives in the arena whether or not a match runs (the jungle). Called when the arena is opened outside
    /// of a countdown, and again at the start of the match; it must be safe to call twice.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    ValueTask StartAmbientAsync(GameMap map, IGameContext gameContext);

    /// <summary>
    /// Starts the match proper: the countdown is over and the clock runs. Starts the ambient systems and whatever
    /// feeds the match (the lane waves).
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    ValueTask StartMatchAsync(GameMap map, IGameContext gameContext);

    /// <summary>
    /// Stops everything <see cref="StartAmbientAsync"/> and <see cref="StartMatchAsync"/> started. Called when the match
    /// ends and when the arena is taken down; it must be safe to call when nothing runs.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    ValueTask StopAsync(GameMap map, IGameContext gameContext);

    /// <summary>
    /// Reacts to the death of the nexus of a team. The mode decides whether that ends the match.
    /// </summary>
    /// <param name="map">The arena map.</param>
    /// <param name="gameContext">The game context.</param>
    /// <param name="losingTeam">The team whose nexus fell.</param>
    /// <returns>A task.</returns>
    ValueTask OnNexusDestroyedAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam);
}

/// <summary>
/// Base of the modes: every hook does nothing, so a mode overrides only what it changes.
/// </summary>
public abstract class MobaGameModeBase : IMobaGameMode
{
    /// <inheritdoc />
    public abstract string Id { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public virtual ValueTask PrepareAsync(GameMap map, IGameContext gameContext) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask StartAmbientAsync(GameMap map, IGameContext gameContext) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask StartMatchAsync(GameMap map, IGameContext gameContext) => this.StartAmbientAsync(map, gameContext);

    /// <inheritdoc />
    public virtual ValueTask StopAsync(GameMap map, IGameContext gameContext) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public virtual ValueTask OnNexusDestroyedAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam) => ValueTask.CompletedTask;
}
