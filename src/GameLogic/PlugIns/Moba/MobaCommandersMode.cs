// <copyright file="MobaCommandersMode.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Commanders: the lane creeps are not a fixed rhythm but what the commanders send. Each team has one commander (a player
/// who claims the command, or a bot when nobody does) who earns essence every second and spends it on packs of creeps and
/// on income. The champions play as always; the match ends when a nexus falls. Version 0: the commander orders with chat
/// commands (<c>/mobacmd</c>), has no jungle and no panel of its own yet.
/// </summary>
public sealed class MobaCommandersMode : MobaGameModeBase
{
    /// <summary>The identifier of the commanders mode.</summary>
    public const string ModeId = "commanders";

    /// <inheritdoc />
    public override string Id => ModeId;

    /// <inheritdoc />
    public override string DisplayName => "Comandantes";

    /// <inheritdoc />
    public override ValueTask PrepareAsync(GameMap map, IGameContext gameContext)
        => MobaGameModes.Classic.PrepareAsync(map, gameContext);

    /// <inheritdoc />
    public override ValueTask StartAmbientAsync(GameMap map, IGameContext gameContext) => ValueTask.CompletedTask;

    /// <inheritdoc />
    public override ValueTask StartMatchAsync(GameMap map, IGameContext gameContext)
    {
        MobaCommanderOrders.Start(map, gameContext);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask StopAsync(GameMap map, IGameContext gameContext)
    {
        MobaCommanderOrders.Stop(map);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public override ValueTask OnNexusDestroyedAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam)
        => MobaGameModes.Classic.OnNexusDestroyedAsync(map, gameContext, losingTeam);
}
