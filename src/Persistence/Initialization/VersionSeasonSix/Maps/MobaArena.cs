// <copyright file="MobaArena.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.Persistence.Initialization.VersionSeasonSix.Maps;

using MUnique.OpenMU.DataModel.Configuration;

/// <summary>
/// Initialization for the MOBA Arena map (custom game mode).
/// </summary>
/// <remarks>
/// Dedicated map for the custom MOBA game mode (see GAMEDESIGN.md). Its terrain is
/// a copy of Crywolf Fortress (map 34) via <c>Resources/Terrain201.att</c> (OpenMU
/// names terrain resources by server map number + 1), so the
/// public Crywolf map and its event stay untouched. Per-match instancing, NPC
/// spawns, lane mobs, turrets and the nexus structure are added by the MOBA mode
/// plugin in later blocks; this initializer only registers the base map so it can
/// be reached and rendered.
/// </remarks>
internal class MobaArena : BaseMapInitializer
{
    /// <summary>
    /// The number of the map.
    /// </summary>
    internal const byte Number = 200;

    /// <summary>
    /// The name of the map.
    /// </summary>
    internal const string Name = "MOBA Arena";

    /// <summary>
    /// Initializes a new instance of the <see cref="MobaArena"/> class.
    /// </summary>
    /// <param name="context">The context.</param>
    /// <param name="gameConfiguration">The game configuration.</param>
    public MobaArena(IContext context, GameConfiguration gameConfiguration)
        : base(context, gameConfiguration)
    {
    }

    /// <inheritdoc/>
    protected override byte MapNumber => Number;

    /// <inheritdoc/>
    protected override string MapName => Name;
}
