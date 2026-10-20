// <copyright file="MobaQueueChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands.Arguments;
using MUnique.OpenMU.Pathfinding;
using MUnique.OpenMU.Persistence;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// Arguments of <c>/mobaqueue</c>.
/// </summary>
public class MobaQueueChatCommandArgs : ArgumentsBase
{
    /// <summary>Gets or sets the action: solo, party, leave, status or fill.</summary>
    [Argument("action", false)]
    public string? Action { get; set; }

    /// <summary>Gets or sets the extra argument: "force" for solo / party, the number of fillers for fill, the character name for penalty / pardon.</summary>
    [Argument("extra", false)]
    public string? Extra { get; set; }
}

/// <summary>
/// GM test tool for the MOBA queue; the players use the queue master NPC in Lorencia.
/// </summary>
[Guid("7C2E9A41-8D35-4B16-A0F7-3E5B1C9D6A82")]
[PlugIn]
[Display(Name = "MOBA: queue command", Description = "GM command '/mobaqueue solo|party|bots|coop|leave|status|fill|penalty|pardon [force|N|name]' - tests the match queue.")]
[ChatCommandHelp(Command, "Test the match queue: solo, party, bots (search accepting bots), coop (against five bots), leave, status, npc (go to the queue master), clear (remove the fake players), window N (seconds to accept), fill N (adds N fake players which accept on their own), penalty [name] (shows the warnings and the block), pardon [name] (clears them). 'force' skips the level 400 / Master requirement.", typeof(MobaQueueChatCommandArgs), CharacterStatus.GameMaster)]
public class MobaQueueChatCommandPlugIn : ChatCommandPlugInBase<MobaQueueChatCommandArgs>
{
    private const string Command = "/mobaqueue";

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    private static string SetWindow(string? seconds)
    {
        MobaReadyCheck.Window = int.TryParse(seconds, out var value) ? TimeSpan.FromSeconds(Math.Clamp(value, 5, 120)) : MobaReadyCheck.DefaultWindow;
        return $"Tiempo para aceptar: {(int)MobaReadyCheck.Window.TotalSeconds} s.";
    }

    private static async ValueTask<string> PenaltyAsync(Player gm, string? name, bool pardon)
    {
        var target = string.IsNullOrWhiteSpace(name) ? gm : gm.GameContext.GetPlayerByCharacterName(name.Trim());
        if (target?.Account is not { } account)
        {
            return $"No encuentro al jugador conectado '{name}'.";
        }

        var who = target.SelectedCharacter?.Name ?? "?";
        if (pardon)
        {
            return await MobaQueuePenalties.PardonAsync(account.GetId()).ConfigureAwait(false)
                ? $"Advertencias y bloqueo de {who} borrados."
                : $"{who} no tenía advertencias.";
        }

        return $"{who}: {await MobaQueuePenalties.DescribeAsync(account.GetId()).ConfigureAwait(false)}";
    }

    private static async ValueTask GoToNpcAsync(Player player)
    {
        if (await player.GameContext.GetMapAsync(MobaQueueNpc.MapNumber, false).ConfigureAwait(false) is not { } lorencia
            || MobaQueueNpc.PositionOn(lorencia) is not { } spot)
        {
            await player.ShowBlueMessageAsync("[Cola] El Maestro de la Arena todavía no está en Lorencia.").ConfigureAwait(false);
            return;
        }

        await player.ShowBlueMessageAsync($"[Cola] El Maestro de la Arena está en Lorencia ({spot.X},{spot.Y}).").ConfigureAwait(false);
        var next = new Point((byte)Math.Min(spot.X + 2, 255), spot.Y);
        if (ReferenceEquals(player.CurrentMap, lorencia))
        {
            await player.MoveAsync(next).ConfigureAwait(false);
        }
        else
        {
            await player.TeleportToMapAsync(lorencia, next).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaQueueChatCommandArgs arguments)
    {
        var force = string.Equals(arguments.Extra?.Trim(), "force", StringComparison.OrdinalIgnoreCase);
        if (arguments.Action?.Trim().ToLowerInvariant() == "npc")
        {
            await GoToNpcAsync(player).ConfigureAwait(false);
            return;
        }

        var message = arguments.Action?.Trim().ToLowerInvariant() switch
        {
            "solo" => await MobaQueueService.JoinAsync(player, MobaQueueChoice.Solo, force).ConfigureAwait(false),
            "bots" => await MobaQueueService.JoinAsync(player, MobaQueueChoice.WithBots, force).ConfigureAwait(false),
            "coop" => await MobaQueueService.JoinAsync(player, MobaQueueChoice.Coop, force).ConfigureAwait(false),
            "help" => MobaQueueService.ModesHelp(),
            "party" => await MobaQueueService.JoinAsync(player, MobaQueueChoice.Party, force).ConfigureAwait(false),
            "leave" => await MobaQueueService.LeaveAsync(player).ConfigureAwait(false),
            "clear" => $"{await MobaQueueService.ClearFillersAsync().ConfigureAwait(false)} relleno(s) quitados de la cola.",
            "window" => SetWindow(arguments.Extra),
            "penalty" => await PenaltyAsync(player, arguments.Extra, pardon: false).ConfigureAwait(false),
            "pardon" => await PenaltyAsync(player, arguments.Extra, pardon: true).ConfigureAwait(false),
            "fill" => $"{MobaQueueService.AddFillers(int.TryParse(arguments.Extra, out var count) ? Math.Clamp(count, 1, 20) : 1)} jugador(es) de relleno agregados.",
            _ => MobaQueueService.StatusOf(player),
        };

        await player.ShowBlueMessageAsync("[Cola] " + message).ConfigureAwait(false);
    }
}

/// <summary>
/// Accepts the match the queue found (the same as the "Aceptar" option of the question).
/// </summary>
[Guid("0F4D8B27-6A93-4E51-9C2A-5B7E1D3F8A64")]
[PlugIn]
[Display(Name = "MOBA: accept match command", Description = "Command '/mobaaccept' - accepts the match found by the queue.")]
[ChatCommandHelp(AcceptCommand, "Accept the match found by the MOBA queue.", typeof(EmptyChatCommandArgs))]
public class MobaAcceptChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string AcceptCommand = "/mobaaccept";

    /// <inheritdoc />
    public override string Key => AcceptCommand;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
        => await player.ShowBlueMessageAsync("[Cola] " + await MobaQueueService.AnswerAsync(player, true).ConfigureAwait(false)).ConfigureAwait(false);
}

/// <summary>
/// Declines the match the queue found (the same as the "Rechazar" option of the question).
/// </summary>
[Guid("5A9C3E72-1B48-4D06-8F2D-7C0A4E6B9D15")]
[PlugIn]
[Display(Name = "MOBA: decline match command", Description = "Command '/mobadecline' - declines the match found by the queue.")]
[ChatCommandHelp(DeclineCommand, "Decline the match found by the MOBA queue.", typeof(EmptyChatCommandArgs))]
public class MobaDeclineChatCommandPlugIn : ChatCommandPlugInBase<EmptyChatCommandArgs>
{
    private const string DeclineCommand = "/mobadecline";

    /// <inheritdoc />
    public override string Key => DeclineCommand;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.Normal;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, EmptyChatCommandArgs arguments)
        => await player.ShowBlueMessageAsync("[Cola] " + await MobaQueueService.AnswerAsync(player, false).ConfigureAwait(false)).ConfigureAwait(false);
}
