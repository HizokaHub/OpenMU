// <copyright file="MobaWavesChatCommandPlugIn.cs" company="MUnique">
// Licensed under the MIT License. See LICENSE file in the project root for full license information.
// </copyright>

namespace MUnique.OpenMU.GameLogic.PlugIns.Moba;

using System.ComponentModel.DataAnnotations;
using System.Runtime.InteropServices;
using MUnique.OpenMU.GameLogic.PlugIns.ChatCommands;
using MUnique.OpenMU.PlugIns;

/// <summary>
/// GM chat command which starts / stops the periodic MOBA wave spawner on the caller's
/// current map: a blue and a red lane wave every N seconds, so a match has a continuous
/// creep rhythm without spamming <c>/mobawave</c>.
/// </summary>
/// <remarks>
/// Test tool for Fase 2 (see GAMEDESIGN.md). Usage: <c>/mobawaves</c> toggles at the
/// default interval; <c>/mobawaves 20</c> (re)starts at 20 s; <c>/mobawaves off</c>
/// stops. A real match context will own this rhythm later.
/// </remarks>
[Guid("D6B4E1A7-2C93-4F58-8A0D-7E1C5B3F9A24")]
[PlugIn]
[Display(Name = "MOBA: periodic wave spawner command", Description = "GM command '/mobawaves [seconds|off]' - toggle continuous lane waves.")]
[ChatCommandHelp(Command, "Start/stop continuous MOBA lane waves ('/mobawaves', '/mobawaves 20', '/mobawaves off').", typeof(MobaWavesChatCommandArgs))]
public class MobaWavesChatCommandPlugIn : ChatCommandPlugInBase<MobaWavesChatCommandArgs>
{
    private const string Command = "/mobawaves";

    private const int MinIntervalSeconds = 5;

    private const int MaxIntervalSeconds = 300;

    /// <inheritdoc />
    public override string Key => Command;

    /// <inheritdoc />
    public override CharacterStatus MinCharacterStatusRequirement => CharacterStatus.GameMaster;

    /// <inheritdoc />
    protected override async ValueTask DoHandleCommandAsync(Player player, MobaWavesChatCommandArgs arguments)
    {
        if (player.CurrentMap is not { } map)
        {
            return;
        }

        var arg = arguments.Interval?.Trim().ToLowerInvariant();
        var running = MobaWavePeriodicSpawner.IsRunning(map.MapId);

        if (arg is "off" or "stop" or "0")
        {
            var stopped = MobaWavePeriodicSpawner.Stop(map.MapId);
            await player.ShowBlueMessageAsync(stopped
                ? $"[MOBA] Periodic waves stopped on '{map.Definition.Name}'."
                : "[MOBA] No periodic waves were running here.").ConfigureAwait(false);
            return;
        }

        // No argument: toggle off if running.
        if (string.IsNullOrEmpty(arg) && running)
        {
            MobaWavePeriodicSpawner.Stop(map.MapId);
            await player.ShowBlueMessageAsync($"[MOBA] Periodic waves stopped on '{map.Definition.Name}'.").ConfigureAwait(false);
            return;
        }

        var seconds = MobaWavePeriodicSpawner.DefaultIntervalSeconds;
        if (!string.IsNullOrEmpty(arg) && int.TryParse(arg, out var parsed))
        {
            seconds = Math.Clamp(parsed, MinIntervalSeconds, MaxIntervalSeconds);
        }

        MobaWavePeriodicSpawner.Start(map, player.GameContext, TimeSpan.FromSeconds(seconds));
        await player.ShowBlueMessageAsync($"[MOBA] Periodic waves every {seconds}s on '{map.Definition.Name}'. Use /mobawaves off to stop.").ConfigureAwait(false);
    }
}
