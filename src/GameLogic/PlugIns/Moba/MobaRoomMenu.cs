// <copyright file="MobaRoomMenu.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using MUnique.OpenMU.GameLogic.Views.Moba;
using MUnique.OpenMU.Persistence;

/// <summary>
/// The menu of the private rooms: what the queue master shows after the "private room" option. The options depend on
/// whether the player is in a room (and on whether it is the host), so the same list is rebuilt to read the option picked.
/// </summary>
public static class MobaRoomMenu
{
    private const string Title = "Partida privada";

    private enum Action
    {
        Create5v5,
        Create3v3,
        Create1v1,
        CreateAram,
        HowToJoin,
        SwitchTeam,
        Refresh,
        ToggleBots,
        Start,
        Leave,
        Close,
    }

    /// <summary>
    /// Shows the room menu.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>A task.</returns>
    public static async ValueTask OpenAsync(Player player)
    {
        var options = OptionsOf(player);
        var text = MobaRooms.RoomOf(player) is null
            ? "Juega con tus amigos sin cola: crea una sala y comparte el código, o únete a la de un amigo con su código."
            : $"Estás en la sala {MobaRooms.RoomOf(player)!.Code}. Sus botones están en el panel de la izquierda.";
        await player.InvokeViewPlugInAsync<IMobaShopPlugIn>(p => p.ShowMenuAsync(MobaRooms.MenuId, Title, text, options.Select(o => o.Label).ToList())).ConfigureAwait(false);
    }

    /// <summary>
    /// Handles an option of the room menu.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="optionIndex">The index of the option.</param>
    /// <returns>A task.</returns>
    public static async ValueTask SelectOptionAsync(Player player, byte optionIndex)
    {
        var options = OptionsOf(player);
        if (optionIndex >= options.Count)
        {
            return;
        }

        var action = options[optionIndex].Action;
        string? answer = action switch
        {
            Action.Create5v5 => await MobaRooms.CreateAsync(player, MobaFormat.Classic).ConfigureAwait(false),
            Action.Create3v3 => await MobaRooms.CreateAsync(player, MobaFormat.Trio).ConfigureAwait(false),
            Action.Create1v1 => await MobaRooms.CreateAsync(player, MobaFormat.Duel).ConfigureAwait(false),
            Action.CreateAram => await MobaRooms.CreateAsync(player, MobaFormat.Aram).ConfigureAwait(false),
            Action.HowToJoin => "Pide el código al anfitrión y escribe /sala unir CÓDIGO en el chat.",
            Action.SwitchTeam => await MobaRooms.SwitchTeamAsync(player).ConfigureAwait(false),
            Action.ToggleBots => await MobaRooms.SetBotsAsync(player, !(MobaRooms.RoomOf(player)?.FillWithBots ?? true)).ConfigureAwait(false),
            Action.Start => await MobaRooms.StartAsync(player).ConfigureAwait(false),
            Action.Leave => await MobaRooms.LeaveAsync(player).ConfigureAwait(false),
            _ => null,
        };

        if (answer is not null)
        {
            await player.ShowBlueMessageAsync("[Sala] " + answer).ConfigureAwait(false);
        }

        if (action is Action.Close or Action.Leave or Action.Start || (action == Action.HowToJoin))
        {
            await CloseDialogAsync(player).ConfigureAwait(false);
            return;
        }

        await OpenAsync(player).ConfigureAwait(false);
    }

    private static List<(string Label, Action Action)> OptionsOf(Player player)
    {
        if (MobaRooms.RoomOf(player) is not { } room)
        {
            return
            [
                ("Crear sala 5v5", Action.Create5v5),
                ("Crear sala 3v3", Action.Create3v3),
                ("Crear sala 1v1", Action.Create1v1),
                ("Crear sala ARAM 5v5", Action.CreateAram),
                ("#Unirme con un código", Action.HowToJoin),
                ("Cerrar", Action.Close),
            ];
        }

        // The panel on the left of the screen has the team, bots and start buttons.
        return [("Salir de la sala", Action.Leave), ("Cerrar", Action.Close)];
    }

    /// <summary>
    /// Joins the room whose code was typed in the box of the menu panel.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <param name="code">The code.</param>
    /// <returns>A task.</returns>
    public static async ValueTask JoinByCodeAsync(Player player, string code)
    {
        await player.ShowBlueMessageAsync("[Sala] " + await MobaRooms.JoinAsync(player, code).ConfigureAwait(false)).ConfigureAwait(false);
        await CloseDialogAsync(player).ConfigureAwait(false);
    }

    private static async ValueTask CloseDialogAsync(Player player)
    {
        if (player.PlayerState.CurrentState == PlayerState.NpcDialogOpened
            && await player.PlayerState.TryAdvanceToAsync(PlayerState.EnteredWorld).ConfigureAwait(false))
        {
            player.OpenedNpc = null;
        }
    }
}
