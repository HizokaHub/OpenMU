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

    /// <summary>Gets or sets the extra argument: the format for crear (NvN, NvNaram, aram), the room number for unir, si / no for bots.</summary>
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
[ChatCommandHelp(Command, "Private MOBA rooms (the same as the room browser of the queue master): 'crear [NvN|NvNaram|aram]' creates one, 'unir NUMBER' joins, 'equipo' changes your team, 'bots si|no' (host) fills the free places with bots, 'iniciar' (host) starts the match, 'salir' leaves, no argument shows the room.", typeof(MobaRoomChatCommandArgs))]
public class MobaRoomChatCommandPlugIn : ChatCommandPlugInBase<MobaRoomChatCommandArgs>
{
    private const string Command = "/sala";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <summary>
    /// Gets the size and the map a text names: <c>3v3</c> (three per team, the three lanes), <c>3v3aram</c> (the middle lane only)
    /// or <c>aram</c> (five per team, the middle lane only).
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The players per team and whether only the middle lane is played; 5v5 on three lanes if the text is empty or unknown.</returns>
    public static (int TeamSize, bool MidOnly) ShapeOf(string? text)
    {
        var clean = (text ?? string.Empty).Trim().ToLowerInvariant();
        var midOnly = clean.Contains("aram");
        var size = clean.Length >= 3 && clean[1] == 'v' && int.TryParse(clean[..1], out var parsed) && parsed is >= 1 and <= 5 ? parsed : 5;
        return (size, midOnly);
    }

    private static ValueTask<string> CreateAsync(Player player, string? shape)
    {
        var (size, midOnly) = ShapeOf(shape);
        return MobaRooms.CreateAsync(player, null, size, midOnly, true);
    }

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaRoomChatCommandArgs arguments)
    {
        var extra = arguments.Extra?.Trim();
        var message = arguments.Action?.Trim().ToLowerInvariant() switch
        {
            "crear" => await CreateAsync(player, extra).ConfigureAwait(false),
            "unir" => int.TryParse(extra, out var roomId) ? await MobaRooms.JoinAsync(player, roomId).ConfigureAwait(false) : "Escribe el número de la sala: /sala unir 101.",
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
