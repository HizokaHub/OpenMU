// <copyright file="MobaCommandersMode.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// Commanders: the lane creeps are not a fixed rhythm but what the commanders send. Each team has one commander (a player
/// who claims the command, or a bot when nobody does) who earns essence every second and spends it on packs of creeps and
/// on income. The packs wait, held, in the base of the team (<see cref="MobaCommanderQueue"/>) and go out together with the wave
/// timer of the arena (Direct Strike), or at once when the human commander uses "send now". The human commander does not play
/// a hero: his body is parked (<see cref="MobaCommanderBody"/>). The match ends when a nexus falls; there is no jungle.
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
    public override async ValueTask StartMatchAsync(GameMap map, IGameContext gameContext)
    {
        MobaCommanderOrders.Start(map, gameContext);

        // A player who claimed the command before the match began is parked as soon as it runs.
        await MobaCommanderBody.SyncAllAsync(map, gameContext).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask StopAsync(GameMap map, IGameContext gameContext)
    {
        MobaCommanderOrders.Stop(map);
        await MobaCommanderBody.RestoreAllAsync(map, gameContext).ConfigureAwait(false);
        await MobaCommanderPanel.HideAllAsync(map, gameContext).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override ValueTask OnNexusDestroyedAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam)
        => MobaGameModes.Classic.OnNexusDestroyedAsync(map, gameContext, losingTeam);
}
