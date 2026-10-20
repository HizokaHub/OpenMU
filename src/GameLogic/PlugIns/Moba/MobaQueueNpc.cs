// <copyright file="MobaQueueNpc.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using MUnique.OpenMU.DataModel.Configuration;
using MUnique.OpenMU.GameLogic.NPC;
using MUnique.OpenMU.Pathfinding;

/// <summary>
/// The queue master: an NPC in Lorencia through which the players search for a MOBA match. Talking to it
/// opens a menu (the same server-driven menu window the shop uses): search alone, with the party, with a
/// team of five, leave the queue, status. Also answers the "match found" question.
/// </summary>
public static class MobaQueueNpc
{
    /// <summary>
    /// The NPC the queue master looks like: "Leo The Helper", a knight in plate armour which the client draws in Lorencia
    /// (the first try, "Elite Skill Soldier", is a skeleton skin whose model is not loaded there and never showed up).
    /// </summary>
    public const short NpcNumber = 371;

    /// <summary>The map the NPC stands on (Lorencia).</summary>
    public const ushort MapNumber = 0;

    /// <summary>Where the NPC stands, near the plaza of Lorencia; the nearest walkable tile is used.</summary>
    public static readonly Point Position = new(134, 130);

    private const string Title = "Maestro de la Arena";

    private static readonly ConcurrentDictionary<GameMap, NonPlayerCharacter> Npcs = new();

    private static readonly string[] Options =
    [
        "Buscar partida (solo)",
        "Buscar con mi party",
        "Salir de la cola",
        "Estado de la cola",
    ];

    /// <summary>
    /// Puts the queue master into Lorencia of a game server, when it is not there yet.
    /// </summary>
    /// <param name="gameContext">The game context.</param>
    /// <returns>A task.</returns>
    public static async ValueTask EnsureAsync(IGameContext gameContext)
    {
        if (await gameContext.GetMapAsync(MapNumber, false).ConfigureAwait(false) is not { } map
            || Npcs.ContainsKey(map)
            || gameContext.Configuration.Monsters.FirstOrDefault(m => m.Number == NpcNumber) is not { } definition)
        {
            return;
        }

        var spot = map.Terrain.GetRandomCoordinate(Position, 2);
        var area = new MonsterSpawnArea
        {
            GameMap = map.Definition,
            MonsterDefinition = definition,
            SpawnTrigger = SpawnTrigger.OnceAtEventStart,
            Quantity = 1,
            X1 = spot.X,
            X2 = spot.X,
            Y1 = spot.Y,
            Y2 = spot.Y,
            Direction = Direction.South,
        };

        var npc = new NonPlayerCharacter(area, definition, map);
        if (!Npcs.TryAdd(map, npc))
        {
            return;
        }

        npc.Initialize();
        await map.AddAsync(npc).ConfigureAwait(false);
        npc.OnSpawn();
        gameContext.LoggerFactory.CreateLogger(typeof(MobaQueueNpc)).LogInformation("[MOBA-QUEUE] queue master NPC {Number} placed in map {Map} at {X},{Y}.", NpcNumber, MapNumber, spot.X, spot.Y);
    }

    /// <summary>
    /// Gets where the queue master of a map stands.
    /// </summary>
    /// <param name="map">The map (Lorencia of a game server).</param>
    /// <returns>The position, or <see langword="null"/> if there is none.</returns>
    public static Point? PositionOn(GameMap map) => Npcs.TryGetValue(map, out var npc) ? npc.Position : null;

    /// <summary>
    /// Gets whether an NPC is a queue master.
    /// </summary>
    /// <param name="npc">The NPC.</param>
    /// <returns><see langword="true"/> for the queue master.</returns>
    public static bool IsQueueMaster(NonPlayerCharacter npc) => Npcs.Values.Contains(npc);

    /// <summary>
    /// Opens the menu if the player talks to a queue master.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="npc">The NPC.</param>
    /// <returns><see langword="true"/> if the talk was handled.</returns>
    public static async ValueTask<bool> TryOpenMenuAsync(Player player, NonPlayerCharacter npc)
    {
        if (!IsQueueMaster(npc))
        {
            return false;
        }

        var text = "Aquí se busca partida de la Arena (5 contra 5). Necesitas nivel 400 y clase Master. " + MobaQueueService.StatusOf(player);
        await player.InvokeViewPlugInAsync<Views.Moba.IMobaShopPlugIn>(p => p.ShowMenuAsync(MobaQueueService.MenuId, Title, text, Options)).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Handles an option picked in the queue master's menu or in the "match found" question.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="menuId">The menu identifier.</param>
    /// <param name="optionIndex">The index of the chosen option.</param>
    /// <returns><see langword="true"/> if the menu belongs to the queue.</returns>
    public static async ValueTask<bool> SelectOptionAsync(Player player, byte menuId, byte optionIndex)
    {
        string? answer;
        if (menuId == MobaQueueService.ReadyMenuId)
        {
            answer = await MobaQueueService.AnswerAsync(player, optionIndex == 0).ConfigureAwait(false);
        }
        else if (menuId == MobaQueueService.MenuId && player.OpenedNpc is { } npc && IsQueueMaster(npc))
        {
            answer = optionIndex switch
            {
                0 => await MobaQueueService.JoinAsync(player, MobaQueueChoice.Solo).ConfigureAwait(false),
                1 => await MobaQueueService.JoinAsync(player, MobaQueueChoice.Party).ConfigureAwait(false),
                2 => await MobaQueueService.LeaveAsync(player).ConfigureAwait(false),
                _ => MobaQueueService.StatusOf(player),
            };
        }
        else
        {
            return menuId == MobaQueueService.MenuId;
        }

        await player.ShowBlueMessageAsync("[Cola] " + answer).ConfigureAwait(false);
        return true;
    }
}
