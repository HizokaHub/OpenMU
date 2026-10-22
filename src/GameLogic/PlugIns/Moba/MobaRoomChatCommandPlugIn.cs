// <copyright file="MobaRoomChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Arguments of <c>/sala</c>.
/// </summary>
public class MobaRoomChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the action: crear, unir, salir, equipo, bots, iniciar or estado.</summary>
    [Argument("action", false)]
    public string? Action { get; set; }

    /// <summary>Gets or sets the extra argument: the format for crear (5v5, 3v3, 1v1, aram), the code for unir, si / no for bots.</summary>
    [Argument("extra", false)]
    public string? Extra { get; set; }
}

/// <summary>
/// The private rooms by chat: <c>/sala crear [5v5|3v3|1v1|aram]</c>, <c>/sala unir CÓDIGO</c>, <c>/sala equipo</c>,
/// <c>/sala bots si|no</c>, <c>/sala iniciar</c>, <c>/sala salir</c> and <c>/sala</c> for the state. The same actions are in
/// the "private room" menu of the queue master.
/// </summary>
[Guid("3E8A5C72-9D14-4B60-A2F7-6C1B0E4D9835")]
[PlugIn]
[Display(Name = "MOBA: private room command", Description = "Command '/sala crear|unir|equipo|bots|iniciar|salir|estado' - private rooms with a code.")]
[ChatCommandHelp(Command, "Private MOBA rooms with a code: 'crear [5v5|3v3|1v1|aram]' creates one, 'unir CODE' joins, 'equipo' changes your team, 'bots si|no' (host) fills the free places with bots, 'iniciar' (host) starts the match, 'salir' leaves, no argument shows the room.", typeof(MobaRoomChatCommandArgs))]
public class MobaRoomChatCommandPlugIn : ChatCommandPlugInBase<MobaRoomChatCommandArgs>
{
    private const string Command = "/sala";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <summary>
    /// Gets the format a text names.
    /// </summary>
    /// <param name="text">The text (5v5, 3v3, 1v1, aram).</param>
    /// <returns>The format; the classic one if the text is empty or unknown.</returns>
    public static MobaFormat FormatOf(string? text) => text?.Trim().ToLowerInvariant() switch
    {
        "3v3" or "trio" => MobaFormat.Trio,
        "1v1" or "duel" => MobaFormat.Duel,
        "aram" => MobaFormat.Aram,
        _ => MobaFormat.Classic,
    };

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaRoomChatCommandArgs arguments)
    {
        var extra = arguments.Extra?.Trim();
        var message = arguments.Action?.Trim().ToLowerInvariant() switch
        {
            "crear" => await MobaRooms.CreateAsync(player, FormatOf(extra)).ConfigureAwait(false),
            "unir" => await MobaRooms.JoinAsync(player, extra).ConfigureAwait(false),
            "salir" => await MobaRooms.LeaveAsync(player).ConfigureAwait(false),
            "equipo" => await MobaRooms.SwitchTeamAsync(player).ConfigureAwait(false),
            "bots" => await MobaRooms.SetBotsAsync(player, !string.Equals(extra, "no", StringComparison.OrdinalIgnoreCase)).ConfigureAwait(false),
            "iniciar" => await MobaRooms.StartAsync(player).ConfigureAwait(false),
            _ => MobaRooms.Describe(player),
        };

        foreach (var line in message.Split('\n'))
        {
            await player.ShowBlueMessageAsync("[Sala] " + line).ConfigureAwait(false);
        }
    }
}
