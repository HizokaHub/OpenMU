// <copyright file="MobaClassicMode.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

/// <summary>
/// The MOBA as it always was: vendors, turrets and nexuses, the jungle, a blue and a red lane wave on a rhythm, and the
/// match ends when a nexus falls.
/// </summary>
public sealed class MobaClassicMode : MobaGameModeBase
{
    /// <summary>The identifier of the classic mode.</summary>
    public const string ModeId = "classic";

    /// <inheritdoc />
    public override string Id => ModeId;

    /// <inheritdoc />
    public override string DisplayName => "MOBA clásico";

    /// <inheritdoc />
    public override async ValueTask PrepareAsync(GameMap map, IGameContext gameContext)
    {
        await MobaShop.EnsureVendorsAsync(map, gameContext).ConfigureAwait(false);
        await MobaStructureSpawner.EnsureStructuresAsync(map, gameContext).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask StartAmbientAsync(GameMap map, IGameContext gameContext)
        => await MobaJungle.EnsureStartedAsync(map, gameContext).ConfigureAwait(false);

    /// <inheritdoc />
    public override async ValueTask StartMatchAsync(GameMap map, IGameContext gameContext)
    {
        await this.StartAmbientAsync(map, gameContext).ConfigureAwait(false);
        MobaWavePeriodicSpawner.Start(map, gameContext, TimeSpan.FromSeconds(MobaWavePeriodicSpawner.DefaultIntervalSeconds));
    }

    /// <inheritdoc />
    public override async ValueTask StopAsync(GameMap map, IGameContext gameContext)
    {
        MobaWavePeriodicSpawner.Stop(map);
        await MobaJungle.StopAsync(map).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public override async ValueTask OnNexusDestroyedAsync(GameMap map, IGameContext gameContext, MobaTeam losingTeam)
        => await MobaMatchEnder.EndMatchAsync(map, gameContext, losingTeam).ConfigureAwait(false);
}
